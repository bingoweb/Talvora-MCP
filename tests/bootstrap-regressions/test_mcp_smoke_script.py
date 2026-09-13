from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'scripts' / 'Test-McpSmoke.ps1'
VERIFY = ROOT / 'scripts' / 'verify.ps1'


def test_mcp_smoke_script_covers_modern_2026_protocol_flow():
    assert SCRIPT.exists(), 'Test-McpSmoke.ps1 must exist'
    text = SCRIPT.read_text(encoding='utf-8-sig')
    required = [
        'server/discover',
        'tools/list',
        'tools/call',
        'get_system_info',
        'MCP-Protocol-Version',
        'Mcp-Method',
        'Mcp-Name',
        '2026-07-28',
    ]
    for token in required:
        assert token in text, f'missing smoke-test token: {token}'


def test_verify_runs_mcp_smoke_after_unit_tests():
    text = VERIFY.read_text(encoding='utf-8-sig')
    assert 'Test-McpSmoke.ps1' in text


def test_powershell_resolver_prefers_pwsh_windowsapps_before_windows_powershell():
    resolver = ROOT / 'scripts' / 'Resolve-PowerShell.cmd'
    text = resolver.read_text(encoding='utf-8-sig')
    windowsapps = '%LOCALAPPDATA%\\Microsoft\\WindowsApps\\pwsh.exe'
    legacy = '%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe'
    assert windowsapps in text, 'resolver must probe the per-user WindowsApps pwsh alias'
    assert legacy in text, 'legacy Windows PowerShell fallback must remain available'
    assert text.index(windowsapps) < text.index(legacy), 'PowerShell 7 must be preferred over Windows PowerShell 5.1'


def test_mcp_smoke_is_windows_powershell_51_compatible():
    text = SCRIPT.read_text(encoding='utf-8-sig')
    assert 'ConvertFrom-Json -Depth' not in text, 'ConvertFrom-Json -Depth is unavailable in Windows PowerShell 5.1'
    assert '-UseBasicParsing' in text, 'Invoke-WebRequest must avoid the Windows PowerShell 5.1 script-parsing prompt'


def test_cmd_and_bat_launchers_are_utf8_without_bom():
    launchers = list(ROOT.glob('*.bat')) + list((ROOT / 'scripts').glob('*.cmd'))
    assert launchers, 'expected at least one cmd/bat launcher'
    for path in launchers:
        data = path.read_bytes()
        assert not data.startswith(b'\xef\xbb\xbf'), f'{path.name} must not contain a UTF-8 BOM because cmd.exe treats it as command text'
