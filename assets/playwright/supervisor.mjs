import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import { spawn, spawnSync } from 'node:child_process';

const parentPid = Number(process.env.TALVORA_PARENT_PID || 0);
const backendPort = Number(process.env.PLAYWRIGHT_MCP_BACKEND_PORT || 8931);
const listenPort = Number(process.env.PLAYWRIGHT_MCP_PROXY_PORT || 8932);
const processStatePath = process.env.PLAYWRIGHT_MCP_PROCESS_STATE || '';
const serverLogPath = process.env.PLAYWRIGHT_MCP_SERVER_LOG || '';
const serverLogMaxBytes = Number(process.env.PLAYWRIGHT_MCP_SERVER_LOG_MAX_BYTES || 5242880);
const backendReadyTimeoutMs = Number(process.env.PLAYWRIGHT_MCP_BACKEND_READY_TIMEOUT_MS || 30000);
const nodeExe = process.execPath;
const cli = process.env.PLAYWRIGHT_MCP_CLI;

if (!cli) {
  throw new Error('PLAYWRIGHT_MCP_CLI is required.');
}

function rotateLogIfNeeded(incomingBytes) {
  if (!serverLogPath) return;

  try {
    fs.mkdirSync(path.dirname(serverLogPath), { recursive: true });
    let size = 0;
    try {
      size = fs.statSync(serverLogPath).size;
    } catch (error) {
      if (error?.code !== 'ENOENT') throw error;
    }

    if (size + incomingBytes < serverLogMaxBytes) return;

    const backup = serverLogPath + '.1';
    try { fs.unlinkSync(backup); } catch (error) {
      if (error?.code !== 'ENOENT') throw error;
    }

    try {
      fs.renameSync(serverLogPath, backup);
    } catch (error) {
      if (error?.code !== 'ENOENT') throw error;
    }
  } catch {
    // Logging must never crash the supervisor.
  }
}

function writeLog(level, message) {
  const line = '[' + new Date().toISOString() + '] [' + level + '] ' + String(message).trimEnd() + '\r\n';
  const bytes = Buffer.byteLength(line, 'utf8');
  rotateLogIfNeeded(bytes);

  if (serverLogPath) {
    try {
      fs.appendFileSync(serverLogPath, line, { encoding: 'utf8' });
      return;
    } catch {
      // Fall through to stderr only if file logging is unavailable.
    }
  }

  if (level === 'ERROR') process.stderr.write(line);
}

const childArgs = [
  cli,
  '--port', String(backendPort),
  '--host', '127.0.0.1',
  '--allowed-hosts', '127.0.0.1:' + backendPort + ',localhost:' + backendPort,
  '--browser', 'chrome',
  '--headless',
  '--user-data-dir', process.env.PLAYWRIGHT_MCP_PROFILE,
  '--shared-browser-context',
  '--allow-unrestricted-file-access',
  '--caps', 'vision,pdf,devtools',
  '--idle-timeout', '0',
  '--snapshot-mode', 'full',
  '--file-paths', 'absolute',
  '--output-dir', process.env.PLAYWRIGHT_MCP_OUTPUT,
  '--output-max-size', '67108864'
];

const child = spawn(nodeExe, childArgs, {
  stdio: ['ignore', 'pipe', 'pipe'],
  windowsHide: true,
  env: process.env
});

child.stdout?.on('data', chunk => writeLog('BACKEND', chunk.toString()));
child.stderr?.on('data', chunk => writeLog('BACKEND-ERR', chunk.toString()));

function writeProcessState() {
  if (!processStatePath) return;

  fs.mkdirSync(path.dirname(processStatePath), { recursive: true });
  const temp = processStatePath + '.tmp';
  fs.writeFileSync(temp, JSON.stringify({
    parentPid,
    supervisorPid: process.pid,
    backendPid: child.pid,
    backendPort,
    proxyPort: listenPort,
    startedAtUtc: new Date().toISOString()
  }));
  fs.renameSync(temp, processStatePath);
}

function clearProcessState() {
  if (!processStatePath) return;
  try {
    fs.unlinkSync(processStatePath);
  } catch (error) {
    if (error?.code !== 'ENOENT') {
      writeLog('WARN', 'Process state cleanup failed: ' + (error?.message || error));
    }
  }
}

function forceKillBackendTree() {
  if (!child.pid || child.exitCode !== null) return;

  const systemRoot = process.env.SystemRoot || 'C:\\Windows';
  const taskkill = path.join(systemRoot, 'System32', 'taskkill.exe');

  try {
    spawnSync(taskkill, ['/PID', String(child.pid), '/T', '/F'], {
      windowsHide: true,
      stdio: 'ignore',
      timeout: 5000
    });
  } catch (error) {
    writeLog('WARN', 'Backend tree force-stop failed: ' + (error?.message || error));
  }
}

let stopping = false;
let exitCodeAfterStop = 0;
let server = null;

function finish(code) {
  clearProcessState();
  process.exit(code);
}

function stop(code = 0, reason = 'shutdown') {
  if (stopping) {
    if (code !== 0) exitCodeAfterStop = code;
    return;
  }

  stopping = true;
  exitCodeAfterStop = code;
  clearInterval(parentWatch);
  writeLog('INFO', 'Supervisor stopping: ' + reason);

  try { server?.close(); } catch {}

  if (child.exitCode === null) {
    try { child.kill('SIGTERM'); } catch {}
  }

  const hardStop = setTimeout(() => {
    forceKillBackendTree();
    finish(exitCodeAfterStop);
  }, 1500);
  hardStop.unref();

  child.once('exit', () => {
    clearTimeout(hardStop);
    finish(exitCodeAfterStop);
  });
}

function fail(reason, error) {
  const detail = error?.stack || error?.message || String(error || '');
  writeLog('ERROR', reason + (detail ? ': ' + detail : ''));
  stop(1, reason);
}

child.once('error', error => fail('Backend process error', error));

writeProcessState();


function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function sendBackendMcpRequest(method, message, sessionId, timeoutMs) {
  return new Promise(resolve => {
    const payload = message === null || message === undefined
      ? ''
      : JSON.stringify(message);
    const headers = {
      host: '127.0.0.1:' + backendPort,
      accept: 'application/json, text/event-stream',
      'mcp-protocol-version': '2025-11-25'
    };

    if (payload) {
      headers['content-type'] = 'application/json';
      headers['content-length'] = Buffer.byteLength(payload, 'utf8');
    }
    if (sessionId) {
      headers['mcp-session-id'] = sessionId;
    }

    const request = http.request({
      host: '127.0.0.1',
      port: backendPort,
      method,
      path: '/mcp',
      headers
    }, response => {
      const chunks = [];
      response.on('data', chunk => chunks.push(Buffer.from(chunk)));
      response.on('end', () => {
        resolve({
          ok: true,
          statusCode: response.statusCode || 0,
          body: Buffer.concat(chunks).toString('utf8'),
          sessionId: response.headers['mcp-session-id'] || ''
        });
      });
    });

    request.setTimeout(timeoutMs, () => {
      request.destroy(new Error('backend MCP request timed out'));
    });
    request.on('error', error => {
      resolve({
        ok: false,
        statusCode: 0,
        body: '',
        sessionId: '',
        error
      });
    });

    if (payload) {
      request.write(payload);
    }
    request.end();
  });
}

async function cleanupBackendProbeSession(sessionId) {
  if (!sessionId) return;

  await sendBackendMcpRequest(
    'POST',
    {
      jsonrpc: '2.0',
      method: 'notifications/initialized'
    },
    sessionId,
    1000);

  await sendBackendMcpRequest(
    'DELETE',
    null,
    sessionId,
    1000);
}

async function probeBackendMcp(timeoutMs) {
  const startedAt = Date.now();
  const response = await sendBackendMcpRequest(
    'POST',
    {
      jsonrpc: '2.0',
      id: 1,
      method: 'initialize',
      params: {
        protocolVersion: '2025-11-25',
        capabilities: {},
        clientInfo: {
          name: 'talvora-playwright-supervisor',
          version: '1.0'
        }
      }
    },
    '',
    timeoutMs);
  const elapsedMs = Date.now() - startedAt;

  const ready =
    response.ok &&
    response.statusCode === 200 &&
    response.body.includes('"result"') &&
    response.body.includes('"serverInfo"');

  if (ready) {
    await cleanupBackendProbeSession(response.sessionId);
  }

  return {
    ready,
    elapsedMs,
    detail: response.ok
      ? 'HTTP ' + response.statusCode
      : (response.error?.code || response.error?.message || 'request failed')
  };
}

async function waitForBackendMcpReady() {
  const timeoutMs = Number.isFinite(backendReadyTimeoutMs) && backendReadyTimeoutMs > 0
    ? backendReadyTimeoutMs
    : 30000;
  const deadline = Date.now() + timeoutMs;
  let warmed = false;
  let lastDetail = 'not attempted';

  while (!stopping && Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error('Playwright MCP backend exited before readiness preflight.');
    }

    const remaining = Math.max(1, deadline - Date.now());
    const requestTimeout = warmed
      ? Math.min(1500, remaining)
      : Math.min(5000, remaining);
    const probe = await probeBackendMcp(requestTimeout);
    lastDetail = probe.detail + '; elapsed=' + probe.elapsedMs + 'ms';

    if (probe.ready) {
      if (probe.elapsedMs <= 1500) {
        writeLog(
          'INFO',
          'Playwright MCP backend readiness preflight passed in ' +
            probe.elapsedMs +
            'ms; compatibility proxy may now accept tunnel-client startup probes.');
        return;
      }

      warmed = true;
      writeLog(
        'INFO',
        'Playwright MCP backend initialized but exceeded fast-probe budget (' +
          probe.elapsedMs +
          'ms); verifying warmed initialize latency.');
    }

    await delay(150);
  }

  throw new Error(
    'Playwright MCP backend did not satisfy the <1500ms initialize readiness budget within ' +
      timeoutMs +
      'ms. Last=' +
      lastDetail);
}

server = http.createServer((req, res) => {
  let url;
  try {
    url = new URL(req.url || '/', 'http://127.0.0.1');
  } catch {
    res.statusCode = 400;
    res.setHeader('content-type', 'text/plain; charset=utf-8');
    res.end('Bad Request');
    return;
  }

  const method = String(req.method || 'GET').toUpperCase();
  const discoveryPath =
    url.pathname.startsWith('/.well-known/oauth-protected-resource') ||
    url.pathname.startsWith('/.well-known/oauth-authorization-server') ||
    url.pathname.startsWith('/.well-known/openid-configuration');

  if (discoveryPath || ((method === 'GET' || method === 'HEAD') && url.pathname !== '/mcp')) {
    // tunnel-client treats an HTTP 404 from every OAuth metadata candidate as
    // an explicit no-auth/optional-discovery result. Keep the body empty so
    // no downstream parser can mistake a text payload for metadata JSON.
    res.statusCode = 404;
    res.setHeader('content-length', '0');
    res.end();
    return;
  }

  const headers = {
    ...req.headers,
    host: '127.0.0.1:' + backendPort
  };

  const upstream = http.request({
    host: '127.0.0.1',
    port: backendPort,
    method: req.method,
    path: req.url,
    headers
  }, upstreamRes => {
    res.writeHead(upstreamRes.statusCode || 502, upstreamRes.headers);
    // Streamable HTTP standalone SSE may legitimately remain silent for a
    // long time. Forward the upstream headers immediately instead of waiting
    // for the first body byte; Go's MCP client waits for these headers before
    // Client.Connect can complete.
    res.flushHeaders();
    upstreamRes.pipe(res);
  });

  upstream.on('error', error => {
    writeLog('WARN', 'Proxy upstream error: ' + (error?.code || error?.message || error));
    if (!res.headersSent) {
      res.statusCode = 502;
      res.setHeader('content-type', 'text/plain; charset=utf-8');
    }
    res.end('Playwright MCP backend unavailable');
  });

  req.on('error', error => {
    writeLog('WARN', 'Proxy request error: ' + (error?.message || error));
    upstream.destroy();
  });

  req.pipe(upstream);
});

server.on('clientError', (_error, socket) => {
  if (socket.writable) {
    socket.end('HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n');
  }
});

server.once('error', error => fail('Compatibility proxy error', error));

const parentWatch = setInterval(() => {
  if (!parentPid) return;
  try {
    process.kill(parentPid, 0);
  } catch {
    stop(0, 'launcher exited');
  }
}, 1000);
parentWatch.unref();

child.once('exit', code => {
  if (!stopping) {
    fail('Backend exited unexpectedly with code ' + String(code ?? 1));
  }
});

process.on('uncaughtException', error =>
  fail('Uncaught supervisor exception', error));
process.on('unhandledRejection', reason =>
  fail('Unhandled supervisor rejection', reason));
process.on('SIGINT', () => stop(0, 'SIGINT'));
process.on('SIGTERM', () => stop(0, 'SIGTERM'));

waitForBackendMcpReady()
  .then(() => {
    if (stopping) return;

    server.listen(listenPort, '127.0.0.1', () => {
      writeLog(
        'INFO',
        'Talvora Playwright MCP compatibility proxy listening on http://127.0.0.1:' +
          listenPort +
          ' -> http://127.0.0.1:' +
          backendPort);
    });
  })
  .catch(error => fail('Backend MCP readiness preflight failed', error));
