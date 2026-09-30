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
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'Amaç:' 'Desktop worklog must explain the current action and purpose in plain Turkish.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'İşlem devam ediyor\. Geçen süre:' 'Long-running work must use compact, non-repetitive Turkish heartbeat copy.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'Program değişikliğini uyguladım\.' 'Desktop worklog completion must report a concrete completed outcome rather than a generic Bitti label.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'Şimdi sıradaki adıma geçiyorum' 'Desktop worklog must not promise an unverified next step.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'yeniden deneyeceğim|düzelteceğim' 'Desktop worklog failure copy must not promise future work that has not happened.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'CallToolResult' 'Desktop worklog must recognize MCP tool error results, not only thrown exceptions.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'IsError\s+is\s+true' 'MCP tool error results must become a visible failure state.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'StructuredContent' 'Structured tool results must be inspected for non-throwing failures.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'exitCode' 'Nonzero command and test results must become a visible failure state.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' '"Bir hata buldum"' 'Detected failures must use plain-language error reporting.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'SharedWorklogOperationId' 'Meaningful tool calls must coalesce into one live desktop worklog card.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' '_activeWorklog' 'Parallel meaningful operations must be tracked so one call cannot report premature overall completion.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' '_pendingFailure' 'A failure must survive concurrent work until the worklog reaches a safe terminal state.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' '_pendingCancellation' 'A cancellation must survive concurrent work until the worklog reaches a safe terminal state.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'SharedWorklogOperationIdPrefix' 'Each logical worklog burst must receive a fresh generation identity.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'DesktopProgressEvidence' 'Desktop worklog must carry structured real evidence separately from conversational copy.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'Version\s*=\s*3' 'Desktop progress wire format must remain explicitly versioned.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'MaximumFrameBytes\s*=\s*64 \* 1024' 'Desktop progress IPC must retain a hard frame-size ceiling.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'WriteFrameAsync' 'Desktop progress sender must use cancellation-aware framed writes.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'ReadFrameAsync' 'Desktop progress receiver must use bounded framed reads.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'SerializeToBoundedPayload' 'Desktop progress aggregate payload size must be deterministically bounded.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'RedactAndClip' 'Desktop progress wire fields and evidence must pass canonical sensitive-data redaction before transport.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressEvidence.cs' 'RedactSensitivePreviewLine' 'Code evidence preview must redact likely secret-bearing lines.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'DIFF // GERÇEK DEĞİŞİKLİK' 'Desktop worklog must expose real code evidence as one compact diff surface.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'TALVORA // TRACE' 'Desktop worklog must retain a compact professional trace identity without fake telemetry.'
Assert-NotContains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'TERMINAL // REAL EXECUTION|KANIT // GERÇEK VERİ|SUMMARY|RESULT|FILES' 'Desktop notification UI must not duplicate the same state across terminal, evidence-summary, result, and files panels.'
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
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'regresyon|derleme|kaynak kod|Git işlemi|repository işlemi|runtime kanıt|process veya job|tunnel işlemi|servis ve tray' 'User-facing desktop worklog language must not expose developer jargon; canonical technical identity belongs only in the compact metadata line.'
Assert-Contains 'src/Talvora/Program.cs' 'request\.Params\.Arguments' 'Desktop progress may use tool arguments to create a meaningful user-facing work subject.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'BuildNarrative\(toolName, arguments\)' 'Desktop progress must translate technical tool calls into meaningful work subjects.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'ShouldNotifyToolCall' 'Desktop progress must suppress low-value internal inspection chatter.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'WTSGetActiveConsoleSessionId' 'Desktop progress must target the active console session.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'NamedPipeClientStream' 'The service must hand desktop progress to the interactive tray over local IPC.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'PipeWriteTimeout' 'Desktop progress pipe writes must have a bounded timeout.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'TerminalQueueCapacity\s*=\s*1_024' 'Terminal outcomes must use a high-capacity but memory-bounded delivery lane.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'DroppedTerminal' 'Catastrophic terminal-lane overload must be observable instead of silently losing outcomes.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' '_progressDeliveryQueue' 'Non-terminal desktop progress must use a bounded delivery lane.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'ProgressQueueCapacity\s*=\s*1' 'Desktop progress must use latest-wins coalescing rather than accumulating stale heartbeats.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'BoundedChannelFullMode\.DropOldest' 'Only the non-terminal progress lane may use latest-wins DropOldest coalescing.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'RetireProgressOperation' 'A terminal outcome must retire pending progress for the same generation.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'ShutdownDrainTimeout' 'Desktop progress shutdown must use a bounded graceful drain.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'SemaphoreSlim _deliverySignal = new\(0, 1\)' 'Desktop progress dual queues must share one coalesced wake signal instead of accumulating abandoned WaitToRead tasks.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'Task\.WhenAny\(waits\)' 'Desktop progress queue waits must not leak losing WaitToRead tasks.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressPipeListener.cs' 'NamedPipeServerStreamAcl\.Create' 'The tray progress IPC endpoint must use an explicit named-pipe ACL.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressPipeListener.cs' 'WellKnownSidType\.LocalSystemSid' 'The tray progress IPC ACL must explicitly admit the LocalSystem service.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressPipeListener.cs' 'PipeReadTimeout' 'A stalled desktop progress IPC client must be disconnected after a bounded read budget.'
Assert-NotContains 'src/Talvora.Tray/DesktopProgressPipeListener.cs' 'ReadLineAsync' 'Unbounded line-based desktop progress framing is forbidden.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'TextWrapping\s*=\s*TextWrapping\.Wrap' 'Desktop progress text must wrap instead of being arbitrarily truncated.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'VerticalScrollBarVisibility\s*=\s*ScrollBarVisibility\.Auto' 'Long desktop progress text and diff evidence must remain fully viewable.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'ShowActivated\s*=\s*false' 'Desktop progress must not steal keyboard focus.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'StartedLifetime\s*=\s*\r?\n?\s*TimeSpan\.FromMinutes\(15\)' 'Active worklog cards must remain available through long work.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'RunningLifetime\s*=\s*\r?\n?\s*TimeSpan\.FromMinutes\(15\)' 'Running worklog cards must remain available through long work.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'CompletedLifetime\s*=\s*\r?\n?\s*TimeSpan\.FromMinutes\(2\)' 'Completion cards must remain readable without lingering like permanent overlays.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'FailureLifetime\s*=\s*\r?\n?\s*TimeSpan\.FromMinutes\(5\)' 'Failure and warning cards must remain visible long enough for inspection.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' '!IsTerminal' 'Active worklog cards must not auto-dismiss before the work state changes.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'DoubleAnimation' 'Desktop progress cards must retain polished enter/exit motion.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'DragMove\(\)' 'Desktop progress cards must remain directly draggable by the user.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'PinStateChangeRequested' 'Desktop progress cards must expose an explicit position pin control.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'TALVORA // TRACE' 'Desktop notifications must retain the compact professional trace identity.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'DIFF // GERÇEK DEĞİŞİKLİK' 'Technical evidence must be shown once, in the lower diff surface.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'ResizeMode\s*=\s*ResizeMode\.CanResize' 'Desktop notification cards must be user-resizable without exposing the legacy dotted resize grip.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'WindowChrome\.SetWindowChrome' 'Borderless notification cards must keep native edge resizing.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'Width\s*=\s*500' 'Desktop notification cards must start compact rather than occupying excessive screen width.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'MinWidth\s*=\s*360' 'Desktop notification cards must be shrinkable to a compact width.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'MinHeight\s*=\s*150' 'Desktop notification cards without diff evidence must be able to remain compact.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'AutomationProperties\.SetName' 'Desktop notification controls must expose UI Automation names.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'SystemParameters\.ClientAreaAnimation' 'Desktop notification motion must respect the Windows reduced-motion setting.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'SystemParameters\.HighContrast' 'Desktop notifications must provide a high-contrast fallback.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressVisualTheme.cs' 'Cascadia Mono, Consolas' 'Execution trace and evidence must use a terminal-appropriate Cascadia fallback stack.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressVisualTheme.cs' 'Segoe UI Variable Text, Segoe UI' 'User-facing notification copy must use the modern Windows UI font stack.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs' 'SymbolIcon' 'Notification state and actions must use reviewed WPF-UI iconography.'
Assert-Contains 'src/Talvora.Tray/ControlCenterTheme.xaml' 'Talvora.Notification.Trace' 'Notification colors must come from semantic design tokens rather than ad-hoc per-update brushes.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' '_isPointerOver\s*\|\|\s*_isClosing' 'Hover must keep dismissal paused even when a progress update arrives.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'MaximumVisibleCardsPerLane\s*=\s*1' 'Desktop progress must keep one worklog card and one independent alert lane without uncontrolled stacking.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' '_automaticScreenDeviceName' 'Automatic desktop progress placement must stay anchored to one monitor for the active card generation.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'ApplyHeightConstraint' 'Desktop progress height must be clamped to the active monitor work area.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressPresentationState.cs' 'SuppressOutOfOrder' 'Late progress frames must not overwrite a newer terminal state.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressPresentationState.cs' 'SuppressDismissed' 'Manual dismissal must suppress the same worklog generation.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'JsonFileStore\.ReadBounded' 'Desktop progress placement restore must use a bounded JSON reader.'
Assert-Contains 'src/Talvora.Tray/TrayApplicationContext.cs' 'DesktopProgressLane\.Alert' 'Operational tray warnings and errors must use the independent alert lane.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'ActiveStaleThreshold\s*=\s*\r?\n?\s*TimeSpan\.FromSeconds\(15\)' 'Active work must flag stale data quickly instead of looking frozen.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' '_lastPayloadUpdatedUtc' 'Stale detection must use real IPC payload time instead of user drag/pin interaction time.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' '"Yeni durum bekliyorum"' 'Stale active work must explain that fresh real data is unavailable without inventing a result.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'SetTextIfChanged' 'Repeated evidence updates must avoid unnecessary WPF text/layout churn.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' '_diffPanel\.Visibility = Visibility\.Collapsed' 'The diff surface must disappear entirely when no real code preview exists.'
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
Assert-Contains '.github/workflows/windows-ci.yml' 'Talvora\.Notification\.Regression' 'CI must execute the desktop notification behavior regression.'
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
