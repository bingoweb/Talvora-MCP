using static SmokeSupport;

internal static partial class SmokeScenarios
{
    private static async Task RunWorkspaceHttpNetworkAsync(SmokeWorkspaceContext context)
    {
        var byName = context.ByName;
        var smokeId = context.SmokeId;
        var httpMockAutoPort = GetFreeLoopbackTcpPort();
                var httpMockAutoPrefix = $"http://127.0.0.1:{httpMockAutoPort}/talvora-auto/";
                var httpMockAutoStart = await EnsureSuccess(byName["talvora_http_mock_start"], new()
                {
                    ["prefixes"] = new[] { httpMockAutoPrefix },
                    ["autoReply"] = true,
                    ["defaultStatusCode"] = 201,
                    ["defaultBody"] = "talvora-auto-reply",
                    ["defaultContentType"] = "text/plain; charset=utf-8",
                    ["defaultHeaders"] = new Dictionary<string, string>
                    {
                        ["X-Talvora-Mock"] = "auto",
                    },
                    ["maxQueuedRequests"] = 0,
                });
                if (httpMockAutoStart.StructuredContent is not { } httpMockAutoStartJson ||
                    string.IsNullOrWhiteSpace(httpMockAutoStartJson.GetProperty("listenerId").GetString()))
                {
                    throw new InvalidOperationException("http_mock_start did not create the auto-reply listener.");
                }
                
                var httpMockAutoId = httpMockAutoStartJson.GetProperty("listenerId").GetString()!;
                try
                {
                    var httpMockGetResult = await EnsureSuccess(byName["talvora_http_mock_get"], new()
                    {
                        ["listenerId"] = httpMockAutoId,
                    });
                    if (httpMockGetResult.StructuredContent is not { } httpMockGetJson ||
                        !httpMockGetJson.GetProperty("isListening").GetBoolean() ||
                        !httpMockGetJson.GetProperty("autoReply").GetBoolean())
                    {
                        throw new InvalidOperationException("http_mock_get did not report the auto listener as active.");
                    }
                
                    var httpMockListResult = await EnsureSuccess(byName["talvora_http_mock_list"], new());
                    if (httpMockListResult.StructuredContent is not { } httpMockListJson ||
                        !httpMockListJson.GetProperty("listeners").EnumerateArray().Any(listener =>
                            string.Equals(
                                listener.GetProperty("listenerId").GetString(),
                                httpMockAutoId,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException("http_mock_list did not include the auto listener.");
                    }
                
                    var autoHttpResult = await EnsureSuccess(byName["talvora_http_request"], new()
                    {
                        ["method"] = "POST",
                        ["url"] = httpMockAutoPrefix + "capture?mode=auto",
                        ["headers"] = new Dictionary<string, string>
                        {
                            ["X-Smoke-Header"] = "auto-value",
                        },
                        ["body"] = "auto-request-body",
                        ["contentType"] = "text/plain; charset=utf-8",
                        ["timeoutSeconds"] = 10,
                        ["responseMode"] = "text",
                        ["maxResponseBytes"] = 65536L,
                    });
                    if (autoHttpResult.StructuredContent is not { } autoHttpJson ||
                        autoHttpJson.GetProperty("statusCode").GetInt32() != 201 ||
                        !string.Equals(
                            autoHttpJson.GetProperty("body").GetString(),
                            "talvora-auto-reply",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("HTTP mock auto reply did not return the configured response.");
                    }

                    var boundedHttpResult = await EnsureSuccess(byName["talvora_http_request"], new()
                    {
                        ["method"] = "GET",
                        ["url"] = httpMockAutoPrefix + "bounded",
                        ["timeoutSeconds"] = 10,
                        ["responseMode"] = "text",
                        ["maxResponseBytes"] = 4L,
                    });
                    if (boundedHttpResult.StructuredContent is not { } boundedHttpJson ||
                        !boundedHttpJson.GetProperty("bodyTruncated").GetBoolean() ||
                        boundedHttpJson.GetProperty("bodyBytes").GetInt64() != 4 ||
                        boundedHttpJson.GetProperty("captureLimitBytes").GetInt64() != 4 ||
                        boundedHttpJson.GetProperty("continuationSupported").GetBoolean() ||
                        !string.Equals(
                            boundedHttpJson.GetProperty("body").GetString(),
                            "talv",
                            StringComparison.Ordinal) ||
                        boundedHttpJson.GetProperty("omittedBodyBytes").GetInt64() <= 0)
                    {
                        throw new InvalidOperationException("http_request did not expose bounded truncation metadata.");
                    }
                
                    var capturedAuto = false;
                    for (var attempt = 0; attempt < 30 && !capturedAuto; attempt++)
                    {
                        await Task.Delay(50);
                        var readResult = await EnsureSuccess(byName["talvora_http_mock_read"], new()
                        {
                            ["listenerId"] = httpMockAutoId,
                            ["maxRequests"] = 0,
                            ["consume"] = false,
                        });
                
                        if (readResult.StructuredContent is not { } readJson)
                        {
                            continue;
                        }
                
                        capturedAuto = readJson.GetProperty("requests").EnumerateArray().Any(request =>
                            string.Equals(request.GetProperty("method").GetString(), "POST", StringComparison.Ordinal) &&
                            (request.GetProperty("url").GetString() ?? string.Empty).Contains("mode=auto", StringComparison.Ordinal) &&
                            string.Equals(request.GetProperty("body").GetString(), "auto-request-body", StringComparison.Ordinal));
                    }
                
                    if (!capturedAuto)
                    {
                        throw new InvalidOperationException("http_mock_read did not capture the auto-reply request.");
                    }
                }
                finally
                {
                    var stopResult = await EnsureSuccess(byName["talvora_http_mock_stop"], new()
                    {
                        ["listenerId"] = httpMockAutoId,
                    });
                    if (stopResult.StructuredContent is not { } stopJson ||
                        !stopJson.GetProperty("found").GetBoolean() ||
                        !stopJson.GetProperty("stopped").GetBoolean())
                    {
                        throw new InvalidOperationException("http_mock_stop did not stop the auto listener.");
                    }
                }
                
                var httpMockManualPort = GetFreeLoopbackTcpPort();
                var httpMockManualPrefix = $"http://127.0.0.1:{httpMockManualPort}/talvora-manual/";
                var httpMockManualStart = await EnsureSuccess(byName["talvora_http_mock_start"], new()
                {
                    ["prefixes"] = new[] { httpMockManualPrefix },
                    ["autoReply"] = false,
                    ["defaultStatusCode"] = 504,
                    ["defaultBody"] = "manual-timeout",
                    ["pendingResponseTimeoutSeconds"] = 10,
                    ["maxQueuedRequests"] = 0,
                });
                if (httpMockManualStart.StructuredContent is not { } httpMockManualStartJson ||
                    string.IsNullOrWhiteSpace(httpMockManualStartJson.GetProperty("listenerId").GetString()))
                {
                    throw new InvalidOperationException("http_mock_start did not create the manual listener.");
                }
                
                var httpMockManualId = httpMockManualStartJson.GetProperty("listenerId").GetString()!;
                try
                {
                    using var manualClient = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(15),
                    };
                    using var manualContent = new StringContent(
                        "manual-request-body",
                        System.Text.Encoding.UTF8,
                        "text/plain");
                
                    var manualRequestTask = manualClient.PostAsync(
                        httpMockManualPrefix + "pending?mode=manual",
                        manualContent);
                
                    string? pendingRequestId = null;
                    for (var attempt = 0; attempt < 60 && pendingRequestId is null; attempt++)
                    {
                        await Task.Delay(50);
                        var readResult = await EnsureSuccess(byName["talvora_http_mock_read"], new()
                        {
                            ["listenerId"] = httpMockManualId,
                            ["maxRequests"] = 0,
                            ["consume"] = false,
                        });
                
                        if (readResult.StructuredContent is not { } readJson)
                        {
                            continue;
                        }
                
                        var pendingRequest = readJson
                            .GetProperty("requests")
                            .EnumerateArray()
                            .FirstOrDefault(request =>
                                request.GetProperty("pendingResponse").GetBoolean() &&
                                string.Equals(request.GetProperty("method").GetString(), "POST", StringComparison.Ordinal) &&
                                string.Equals(request.GetProperty("body").GetString(), "manual-request-body", StringComparison.Ordinal));
                
                        if (pendingRequest.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            pendingRequestId = pendingRequest.GetProperty("requestId").GetString();
                        }
                    }
                
                    if (string.IsNullOrWhiteSpace(pendingRequestId))
                    {
                        throw new InvalidOperationException("http_mock_read did not expose a pending manual request.");
                    }
                
                    var replyResult = await EnsureSuccess(byName["talvora_http_mock_reply"], new()
                    {
                        ["listenerId"] = httpMockManualId,
                        ["requestId"] = pendingRequestId,
                        ["statusCode"] = 202,
                        ["body"] = "talvora-manual-reply",
                        ["contentType"] = "text/plain; charset=utf-8",
                        ["headers"] = new Dictionary<string, string>
                        {
                            ["X-Talvora-Mock"] = "manual",
                        },
                    });
                    if (replyResult.StructuredContent is not { } replyJson ||
                        !replyJson.GetProperty("found").GetBoolean() ||
                        !replyJson.GetProperty("replied").GetBoolean() ||
                        replyJson.GetProperty("statusCode").GetInt32() != 202)
                    {
                        throw new InvalidOperationException("http_mock_reply did not reply to the pending request.");
                    }
                
                    using var manualResponse = await manualRequestTask;
                    var manualResponseBody = await manualResponse.Content.ReadAsStringAsync();
                    if ((int)manualResponse.StatusCode != 202 ||
                        !string.Equals(manualResponseBody, "talvora-manual-reply", StringComparison.Ordinal) ||
                        !manualResponse.Headers.TryGetValues("X-Talvora-Mock", out var manualHeaderValues) ||
                        !manualHeaderValues.Contains("manual", StringComparer.Ordinal))
                    {
                        throw new InvalidOperationException("manual HTTP mock client did not receive the configured reply.");
                    }
                }
                finally
                {
                    var stopResult = await EnsureSuccess(byName["talvora_http_mock_stop"], new()
                    {
                        ["listenerId"] = httpMockManualId,
                    });
                    if (stopResult.StructuredContent is not { } stopJson ||
                        !stopJson.GetProperty("found").GetBoolean() ||
                        !stopJson.GetProperty("stopped").GetBoolean())
                    {
                        throw new InvalidOperationException("http_mock_stop did not stop the manual listener.");
                    }
                }
                
                var httpResult = await EnsureSuccess(byName["talvora_http_request"], new()
                {
                    ["method"] = "GET",
                    ["url"] = "http://127.0.0.1:7676/healthz",
                    ["responseMode"] = "text",
                    ["maxResponseBytes"] = 0,
                });
                if (httpResult.StructuredContent is not { } httpJson ||
                    httpJson.GetProperty("statusCode").GetInt32() != 200 ||
                    !(httpJson.GetProperty("body").GetString() ?? string.Empty).Contains("Talvora", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("http_request did not return the Talvora health response.");
                }
                
                var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                portProbe.Start();
                var httpMockPort = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;
                portProbe.Stop();
                
                var httpMockPrefix = $"http://127.0.0.1:{httpMockPort}/";
                var httpMockBody = "talvora-http-mock-" + smokeId;
                
                var httpMockStartResult = await EnsureSuccess(byName["talvora_http_mock_start"], new()
                {
                    ["prefixes"] = new[] { httpMockPrefix },
                    ["autoReply"] = true,
                    ["defaultStatusCode"] = 202,
                    ["defaultBody"] = "mock-accepted",
                    ["defaultContentType"] = "text/plain; charset=utf-8",
                    ["requestBodyMode"] = "text",
                    ["maxRequestBodyBytes"] = 0,
                    ["maxQueuedRequests"] = 0,
                });
                if (httpMockStartResult.StructuredContent is not { } httpMockStartJson ||
                    string.IsNullOrWhiteSpace(httpMockStartJson.GetProperty("listenerId").GetString()))
                {
                    throw new InvalidOperationException("http_mock_start did not return a listener ID.");
                }
                
                var httpMockListenerId = httpMockStartJson.GetProperty("listenerId").GetString()!;
                
                try
                {
                    var httpMockGetResult = await EnsureSuccess(byName["talvora_http_mock_get"], new()
                    {
                        ["listenerId"] = httpMockListenerId,
                    });
                    if (httpMockGetResult.StructuredContent is not { } httpMockGetJson ||
                        !httpMockGetJson.GetProperty("isListening").GetBoolean())
                    {
                        throw new InvalidOperationException("http_mock_get did not report the listener as active.");
                    }
                
                    var httpMockListResult = await EnsureSuccess(byName["talvora_http_mock_list"], new());
                    if (httpMockListResult.StructuredContent is not { } httpMockListJson ||
                        !httpMockListJson.GetProperty("listeners").EnumerateArray().Any(listener =>
                            listener.TryGetProperty("listenerId", out var listedId) &&
                            string.Equals(listedId.GetString(), httpMockListenerId, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException("http_mock_list did not include the active listener.");
                    }
                
                    var httpMockRequestResult = await EnsureSuccess(byName["talvora_http_request"], new()
                    {
                        ["method"] = "POST",
                        ["url"] = httpMockPrefix + "webhook?kind=smoke",
                        ["headers"] = new Dictionary<string, string>
                        {
                            ["X-Talvora-Smoke"] = smokeId,
                        },
                        ["body"] = httpMockBody,
                        ["contentType"] = "text/plain; charset=utf-8",
                        ["responseMode"] = "text",
                        ["maxResponseBytes"] = 65536L,
                        ["timeoutSeconds"] = 10,
                    });
                    if (httpMockRequestResult.StructuredContent is not { } httpMockRequestJson ||
                        httpMockRequestJson.GetProperty("statusCode").GetInt32() != 202 ||
                        !string.Equals(
                            httpMockRequestJson.GetProperty("body").GetString(),
                            "mock-accepted",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("http mock listener did not return its configured response.");
                    }
                
                    var httpMockReadResult = await EnsureSuccess(byName["talvora_http_mock_read"], new()
                    {
                        ["listenerId"] = httpMockListenerId,
                        ["afterSequence"] = 0L,
                        ["maxRequests"] = 0,
                        ["consume"] = true,
                    });
                    if (httpMockReadResult.StructuredContent is not { } httpMockReadJson ||
                        httpMockReadJson.GetProperty("count").GetInt32() < 1 ||
                        !httpMockReadJson.GetProperty("requests").EnumerateArray().Any(request =>
                            string.Equals(request.GetProperty("method").GetString(), "POST", StringComparison.OrdinalIgnoreCase) &&
                            (request.GetProperty("rawUrl").GetString() ?? string.Empty).Contains("/webhook?kind=smoke", StringComparison.Ordinal) &&
                            string.Equals(request.GetProperty("body").GetString(), httpMockBody, StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException("http_mock_read did not capture the smoke request.");
                    }
                }
                finally
                {
                    var httpMockStopResult = await EnsureSuccess(byName["talvora_http_mock_stop"], new()
                    {
                        ["listenerId"] = httpMockListenerId,
                    });
                    if (httpMockStopResult.StructuredContent is not { } httpMockStopJson ||
                        !httpMockStopJson.GetProperty("found").GetBoolean() ||
                        !httpMockStopJson.GetProperty("stopped").GetBoolean())
                    {
                        throw new InvalidOperationException("http_mock_stop did not stop the listener.");
                    }
                }
                
                var tcpListenersResult = await EnsureSuccess(byName["talvora_tcp_listeners"], new()
                {
                    ["localPort"] = 7676,
                });
                if (tcpListenersResult.StructuredContent is not { } tcpListenersJson ||
                    tcpListenersJson.GetProperty("count").GetInt32() < 1 ||
                    !tcpListenersJson.GetProperty("connections").EnumerateArray().Any(connection =>
                        connection.GetProperty("localPort").GetInt32() == 7676 &&
                        connection.GetProperty("processId").GetInt32() > 0))
                {
                    throw new InvalidOperationException("tcp_listeners did not report the Talvora listener.");
                }
                
                var tcpConnectionsResult = await EnsureSuccess(byName["talvora_tcp_connections"], new()
                {
                    ["localPort"] = 7676,
                });
                if (tcpConnectionsResult.StructuredContent is not { } tcpConnectionsJson ||
                    tcpConnectionsJson.GetProperty("count").GetInt32() < 1)
                {
                    throw new InvalidOperationException("tcp_connections did not report Talvora TCP rows.");
                }
                
                var waitTcpResult = await EnsureSuccess(byName["talvora_wait_tcp"], new()
                {
                    ["host"] = "127.0.0.1",
                    ["port"] = 7676,
                    ["timeoutSeconds"] = 5,
                });
                if (waitTcpResult.StructuredContent is not { } waitTcpJson ||
                    !waitTcpJson.GetProperty("connected").GetBoolean())
                {
                    throw new InvalidOperationException("wait_tcp did not connect to the Talvora listener.");
                }
                
                var networkInterfacesResult = await EnsureSuccess(byName["talvora_network_interfaces"], new());
                if (networkInterfacesResult.StructuredContent is not { } networkInterfacesJson ||
                    networkInterfacesJson.GetProperty("count").GetInt32() < 1)
                {
                    throw new InvalidOperationException("network_interfaces did not return any network interfaces.");
                }
                
                var dnsLookupResult = await EnsureSuccess(byName["talvora_dns_lookup"], new()
                {
                    ["host"] = "localhost",
                });
                if (dnsLookupResult.StructuredContent is not { } dnsLookupJson ||
                    dnsLookupJson.GetProperty("addresses").GetArrayLength() < 1)
                {
                    throw new InvalidOperationException("dns_lookup did not resolve localhost.");
                }
                
                var pingResult = await EnsureSuccess(byName["talvora_ping"], new()
                {
                    ["host"] = "127.0.0.1",
                    ["timeoutMilliseconds"] = 3000,
                    ["payloadBytes"] = 16,
                });
                if (pingResult.StructuredContent is not { } pingJson ||
                    !string.Equals(
                        pingJson.GetProperty("status").GetString(),
                        "Success",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("ping did not reach loopback.");
                }
                
                var tcpExchangeResult = await EnsureSuccess(byName["talvora_tcp_exchange"], new()
                {
                    ["host"] = "127.0.0.1",
                    ["port"] = 7676,
                    ["text"] = "GET /healthz HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n",
                    ["responseMode"] = "text",
                    ["maxResponseBytes"] = 65536L,
                    ["timeoutSeconds"] = 10,
                    ["idleReadTimeoutMilliseconds"] = 2000,
                });
                if (tcpExchangeResult.StructuredContent is not { } tcpExchangeJson ||
                    tcpExchangeJson.GetProperty("bytesReceived").GetInt64() < 1 ||
                    !(tcpExchangeJson.GetProperty("response").GetString() ?? string.Empty)
                        .Contains("200 OK", StringComparison.OrdinalIgnoreCase) ||
                    !(tcpExchangeJson.GetProperty("response").GetString() ?? string.Empty)
                        .Contains("Talvora", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("tcp_exchange did not return the Talvora health response.");
                }

                var boundedTcpExchangeResult = await EnsureSuccess(byName["talvora_tcp_exchange"], new()
                {
                    ["host"] = "127.0.0.1",
                    ["port"] = 7676,
                    ["text"] = "GET /healthz HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n",
                    ["responseMode"] = "text",
                    ["maxResponseBytes"] = 8L,
                    ["timeoutSeconds"] = 10,
                    ["idleReadTimeoutMilliseconds"] = 2000,
                });
                if (boundedTcpExchangeResult.StructuredContent is not { } boundedTcpExchangeJson ||
                    boundedTcpExchangeJson.GetProperty("bytesReceived").GetInt64() != 8 ||
                    !boundedTcpExchangeJson.GetProperty("responseTruncated").GetBoolean() ||
                    boundedTcpExchangeJson.GetProperty("captureLimitBytes").GetInt64() != 8 ||
                    boundedTcpExchangeJson.GetProperty("continuationSupported").GetBoolean())
                {
                    throw new InvalidOperationException("tcp_exchange did not expose bounded truncation metadata.");
                }
    }
}
