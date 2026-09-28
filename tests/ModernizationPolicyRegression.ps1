$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$regexOptions =
    [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
    [Text.RegularExpressions.RegexOptions]::CultureInvariant

function Assert-Contains([string]$Path, [string]$Pattern, [string]$Message) {
    $text = [IO.File]::ReadAllText((Join-Path $root $Path))
    if (-not [Text.RegularExpressions.Regex]::IsMatch($text, $Pattern, $regexOptions)) {
        throw $Message
    }
}

function Assert-NotContains([string]$Path, [string]$Pattern, [string]$Message) {
    $text = [IO.File]::ReadAllText((Join-Path $root $Path))
    if ([Text.RegularExpressions.Regex]::IsMatch($text, $Pattern, $regexOptions)) {
        throw $Message
    }
}

if (-not [Text.RegularExpressions.Regex]::IsMatch(
        'Installer',
        'installer',
        $regexOptions)) {
    throw 'Modernization policy regex checks must be culture-invariant across local and GitHub runners.'
}

Assert-Contains 'global.json' '"version"\s*:\s*"10\.0\.4\d{2}"' 'global.json must pin the current .NET 10.0.4xx feature band.'
Assert-Contains 'global.json' '"rollForward"\s*:\s*"latestFeature"' 'global.json must permit current servicing SDKs in the pinned feature band.'
Assert-Contains 'Directory.Packages.props' '<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>' 'Central Package Management must remain enabled.'
Assert-Contains 'Directory.Build.props' '<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>' 'NuGet lock files must remain enabled.'
Assert-Contains 'Directory.Build.props' '<NuGetAuditMode>all</NuGetAuditMode>' 'NuGet audit must cover transitive dependencies.'
Assert-Contains 'AGENTS.md' 'Context7 is mandatory' 'Context7 must remain an explicit development quality gate.'
Assert-Contains 'docs/MODERNIZATION-POLICY.md' 'Gitea issue' 'Modernization phases must remain tracked in Gitea.'
Assert-Contains 'AGENTS.md' 'Mandatory Windows desktop progress reporting' 'Desktop progress reporting must remain a durable engineering rule.'
Assert-Contains 'src/Talvora/Program.cs' 'AddCallToolFilter' 'Every MCP tool call must remain wrapped by the desktop progress contract.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'FirstProgressDelay\s*=\s*\r?\n?\s*TimeSpan\.FromSeconds\(2\)' 'Desktop progress must become visibly live within two seconds.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'ProgressInterval\s*=\s*\r?\n?\s*TimeSpan\.FromSeconds\(5\)' 'Long-running work must refresh often enough to feel continuously live.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'attempt <= 3' 'Desktop progress IPC must retry transient tray connection misses instead of silently losing updates.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' '_recentWorklogUpdates' 'The single live worklog card must retain recent real work history instead of overwriting every prior step.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'Son yaptıklarım:' 'The worklog must expose recent activity in ordinary Turkish.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'Gerçek kod değişikliği:' 'Real patch line counts must be translated into understandable user-facing detail.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' '"Şimdi bunu yapıyorum"' 'Desktop worklog must use plain conversational Turkish.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' '"Hâlâ bununla uğraşıyorum"' 'Desktop worklog must use plain conversational Turkish for long work.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' '"Bitti"' 'Desktop worklog completion must be plain and immediately understandable.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'CallToolResult' 'Desktop worklog must recognize MCP tool error results, not only thrown exceptions.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'IsError\s+is\s+true' 'MCP tool error results must become a visible failure state.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'StructuredContent' 'Structured tool results must be inspected for non-throwing failures.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'exitCode' 'Nonzero command and test results must become a visible failure state.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' '"Bir hata buldum"' 'Detected failures must use plain-language error reporting.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'SharedWorklogOperationId' 'Meaningful tool calls must coalesce into one live desktop worklog card.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' '_activeWorklog' 'Parallel meaningful operations must be tracked so one call cannot report premature overall completion.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' '_pendingFailure' 'A failure must survive concurrent work until the worklog reaches a safe terminal state.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'DesktopProgressEvidence' 'Desktop worklog must carry structured real evidence separately from conversational copy.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressEvidence.cs' 'RedactSensitivePreviewLine' 'Code evidence preview must redact likely secret-bearing lines.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'KANIT // GERÇEK VERİ' 'Desktop worklog must expose a clearly labeled real-evidence panel.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'TALVORA // LIVE TRACE' 'Desktop worklog must retain the dark terminal visual identity without inventing fake telemetry.'
Assert-Contains 'src/Talvora.Tray/ControlCenterUserMessage.cs' 'Teknik ayrıntılar günlüğe kaydedildi' 'Control Center must provide plain-language user errors while retaining technical detail in logs.'
Assert-NotContains 'src/Talvora.Tray/ControlCenterWindow.Actions.cs' 'return\s+exception\.Message|return\s+message' 'Control Center dialogs must not fall back to raw exception text.'
Assert-NotContains 'src/Talvora.Tray/TrayApplicationContext.cs' 'ShowNotification\([^;]{0,220}ex\.Message' 'Tray notifications must not expose raw exception text.'
Assert-NotContains 'src/Talvora.Tray/ControlCenterDashboardService.cs' 'Browser doğrulaması|browser navigate|accessibility snapshot' 'Primary dashboard copy must use ordinary Turkish instead of browser test jargon.'
Assert-NotContains 'src/Talvora.Tray/ControlCenterComponentHealthService.cs' '"Browser hazır değil"|runtime generation|accessibility snapshot smoke' 'Component health copy must keep browser diagnostics understandable for nontechnical users.'
Assert-Contains 'src/Talvora.Tray/ControlCenterLifecycleService.Penpot.cs' '"down"' 'Penpot stop must bring down its Docker Compose stack.'
Assert-Contains 'src/Talvora.Tray/ControlCenterLifecycleService.Penpot.cs' '"up"' 'Penpot start must bring up its Docker Compose stack on demand.'
Assert-NotContains 'src/Talvora.Tray/ControlCenterLifecycleService.Penpot.cs' '--volumes' 'Penpot lifecycle must never remove persistent data volumes.'
Assert-Contains 'src/Talvora.Tray/TrayApplicationContext.cs' 'EnsureOnDemandStoppedAsync' 'Degraded optional Penpot state must be cleaned up instead of being left half-running.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'result classification failed without affecting the tool result' 'Worklog classification failures must never change the underlying tool result.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'TryBeginWorklog' 'Worklog startup failures must be isolated from the underlying tool call.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'TryUpdateWorklogProgress' 'Worklog progress failures must be isolated from the underlying tool call.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'TryEndWorklog' 'Worklog terminal-update failures must be isolated from the underlying tool call.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'heartbeat failed without affecting the tool result' 'Heartbeat failures must never replace or fail the underlying tool result.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'BuildPlainCompletion' 'Completion messages must describe completed work in completed-action language.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'TALVORA_UPDATE_DETACHED' 'Detached self-update completion must be detected from the real installer handoff marker.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' '"Kurucuya devrettim"' 'Detached self-update must not falsely claim the new runtime is already active.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'TryBuildDeferredCompletion' 'Desktop worklog must use truthful deferred completion copy when installation continues outside the tool call.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'Talvora işlemi:' 'Unknown tools must never leak raw technical tool names into the user-facing worklog.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'installer|commit|repo|regresyon|derleme|kaynak kod|Git işlemi|servis ve tray' 'User-facing desktop worklog language must not expose developer jargon.'
Assert-Contains 'src/Talvora/Program.cs' 'request\.Params\.Arguments' 'Desktop progress may use tool arguments to create a meaningful user-facing work subject.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'BuildNarrative\(toolName, arguments\)' 'Desktop progress must translate technical tool calls into meaningful work subjects.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'ShouldNotifyToolCall' 'Desktop progress must suppress low-value internal inspection chatter.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'WTSGetActiveConsoleSessionId' 'Desktop progress must target the active console session.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'NamedPipeClientStream' 'The service must hand desktop progress to the interactive tray over local IPC.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressPipeListener.cs' 'NamedPipeServerStreamAcl\.Create' 'The tray progress IPC endpoint must use an explicit named-pipe ACL.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressPipeListener.cs' 'WellKnownSidType\.LocalSystemSid' 'The tray progress IPC ACL must explicitly admit the LocalSystem service.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'TextWrapping\s*=\s*TextWrapping\.Wrap' 'Desktop progress text must wrap instead of being arbitrarily truncated.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'VerticalScrollBarVisibility\s*=\s*ScrollBarVisibility\.Auto' 'Long desktop progress text must remain fully viewable.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'ShowActivated\s*=\s*false' 'Desktop progress must not steal keyboard focus.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'StartedLifetime\s*=\s*\r?\n?\s*TimeSpan\.FromMinutes\(15\)' 'Active worklog cards must remain available through long work.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'RunningLifetime\s*=\s*\r?\n?\s*TimeSpan\.FromMinutes\(15\)' 'Running worklog cards must remain available through long work.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'CompletedLifetime\s*=\s*\r?\n?\s*TimeSpan\.FromMinutes\(10\)' 'Completion worklog cards must remain readable for ten minutes.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'FailureLifetime\s*=\s*\r?\n?\s*TimeSpan\.FromMinutes\(15\)' 'Failure worklog cards must remain visible long enough to inspect safely.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' '!IsTerminal' 'Active worklog cards must not auto-dismiss before the work state changes.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'DoubleAnimation' 'Desktop progress cards must retain polished enter/exit motion.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'DragMove\(\)' 'Desktop progress cards must remain directly draggable by the user.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'PinStateChangeRequested' 'Desktop progress cards must expose an explicit position pin control.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' '_isPointerOver\s*\|\|\s*_isClosing' 'Hover must keep dismissal paused even when a progress update arrives.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'MaximumVisibleCards\s*=\s*1' 'Desktop progress must behave as one live ChatGPT-style worklog card instead of a notification stack.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'ActiveStaleThreshold\s*=\s*\r?\n?\s*TimeSpan\.FromSeconds\(15\)' 'Active work must flag stale data quickly instead of looking frozen.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' '"Yeni durum bekliyorum"' 'Stale active work must explain that fresh real data is unavailable without inventing a result.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'SetTextIfChanged' 'Repeated evidence updates must avoid unnecessary WPF text/layout churn.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'message\.Kind == DesktopProgressKind\.Failed' 'Evidence auto-expansion must be reserved for failures instead of every code-preview update.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'OrderByDescending\(static window => window\.IsTerminal\)' 'Completed cards must retire before active work when the visible-card budget is full.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'desktop-progress-placement\.json' 'Desktop progress pin state and position must persist across tray restarts.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'GetWindowRect' 'Desktop progress placement must capture native Windows pixel coordinates.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'SetWindowPos' 'Desktop progress placement must use native no-activate pixel positioning across mixed-DPI monitors.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'Desktop progress notification publish failed' 'Desktop progress UI failures must be isolated and logged instead of terminating the tray application.'
Assert-NotContains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'workArea\.Bottom\s*/\s*dpi\.DpiScaleY' 'Mixed-DPI desktop progress placement must not divide global screen coordinates by one window DPI scale.'
Assert-Contains 'src/Talvora.Tray/TrayApplicationContext.cs' '_desktopProgressPresenter\.Publish' 'Legacy tray status notifications must use the full-text progress-card surface.'
Assert-NotContains 'src/Talvora.Tray/TrayApplicationContext.cs' 'BalloonTipText\s*=\s*text\.Length\s*<=\s*240' 'Arbitrary 240-character tray notification truncation is forbidden.'
Assert-NotContains 'src/Talvora.Tray/TrayApplicationContext.cs' 'ShowBalloon|showBalloon' 'Legacy balloon terminology must not survive after routing notifications through the modern progress-card surface.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'IsTruthy\(Environment\.GetEnvironmentVariable\("CI"\)\)' 'Desktop progress must stay noninteractive in CI.'
Assert-Contains '.github/workflows/windows-ci.yml' '--locked-mode' 'CI restore must use locked mode.'
Assert-Contains '.github/workflows/windows-ci.yml' 'ModernizationPolicyRegression\.ps1' 'CI must enforce the modernization policy regression.'
Assert-Contains '.github/workflows/windows-ci.yml' 'Validate-Context7Gate\.ps1' 'CI must enforce fresh Context7 evidence.'
Assert-Contains '.github/workflows/windows-ci.yml' 'actions/checkout@[0-9a-f]{40}\s+#\s+v\d+\.\d+\.\d+' 'actions/checkout must be pinned to a full release SHA.'
Assert-Contains '.github/workflows/windows-ci.yml' 'actions/setup-dotnet@[0-9a-f]{40}\s+#\s+v\d+\.\d+\.\d+' 'actions/setup-dotnet must be pinned to a full release SHA.'
Assert-Contains '.github/dependabot.yml' 'package-ecosystem:\s*"dotnet-sdk"' 'Dependabot must keep global.json current.'
Assert-Contains '.context7/verification.json' '"schemaVersion"\s*:\s*1' 'Context7 evidence schema must remain versioned.'

$projectVersions = Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Filter '*.csproj' -Recurse |
    Select-String -Pattern 'PackageReference.+\sVersion="'
if ($projectVersions) {
    $projectVersions | ForEach-Object { Write-Host $_.Line }
    throw 'PackageReference versions must be centralized in Directory.Packages.props.'
}

[xml]$centralPackages = Get-Content -LiteralPath (Join-Path $root 'Directory.Packages.props') -Raw
foreach ($packageVersion in @($centralPackages.Project.ItemGroup.PackageVersion)) {
    $version = [string]$packageVersion.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw "Central package version must be an exact stable version: $($packageVersion.Include)=$version"
    }
}
$projects = Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Filter '*.csproj' -Recurse
foreach ($project in $projects) {
    $lock = Join-Path $project.DirectoryName 'packages.lock.json'
    if (-not (Test-Path -LiteralPath $lock -PathType Leaf)) {
        throw "Missing NuGet lock file for $($project.FullName)"
    }
}

Write-Host 'MODERNIZATION_POLICY_GREEN'
