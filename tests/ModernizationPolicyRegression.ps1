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

$sdk = (Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).sdk
if ($sdk.version -cnotmatch '^10\.0\.4\d{2}$' -or $sdk.rollForward -cne 'disable' -or $sdk.allowPrerelease -cne $false) {
    throw 'global.json must pin an exact stable .NET 10.0.4xx SDK with rollForward=disable and allowPrerelease=false so SDK and NuGet lock files stay in lockstep.'
}
Assert-Contains 'Directory.Packages.props' '<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>' 'Central Package Management must remain enabled.'
Assert-Contains 'Directory.Build.props' '<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>' 'NuGet lock files must remain enabled.'
Assert-Contains 'Directory.Build.props' '<NuGetAuditMode>all</NuGetAuditMode>' 'NuGet audit must cover transitive dependencies.'
Assert-Contains 'AGENTS.md' 'Context7 is mandatory' 'Context7 must remain an explicit development quality gate.'
Assert-NotContains 'AGENTS.md' 'Awwwards is the mandatory design delivery reference' 'Awwwards was removed from the delivery gate (audit 29).'
Assert-Contains 'docs/MODERNIZATION-POLICY.md' 'Gitea issue' 'Modernization phases must remain tracked in Gitea.'
Assert-Contains 'AGENTS.md' 'Mandatory Windows desktop progress reporting' 'Desktop progress reporting must remain a durable engineering rule.'
Assert-Contains 'src/Talvora/Program.cs' 'AddCallToolFilter' 'Every MCP tool call must remain wrapped by the desktop progress contract.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'FirstProgressDelay\s*=\s*\r?\n?\s*TimeSpan\.FromSeconds\(2\)' 'Desktop progress must become visibly live within two seconds.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'ProgressInterval\s*=\s*\r?\n?\s*TimeSpan\.FromSeconds\(5\)' 'Long-running work must refresh often enough to feel continuously live.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'attempt <= 3' 'Desktop progress IPC must retry transient tray connection misses instead of silently losing updates.'
Assert-NotContains 'src/Talvora/TalvoraDesktopWorklogState.cs' '_recentWorklogUpdates' 'N1 must remove the duplicate work-history queue.'
Assert-NotContains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'Son yaptıklarım:' 'N1 must not repeat prior steps in user-facing message text.'
Assert-NotContains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'Gerçek kod değişikliği:' 'N1 must not repeat code evidence in the plain worklog message.'
Assert-NotContains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'Amaç:' 'N1 must remove filler purpose text from the running message.'
Assert-NotContains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'İşlem devam ediyor\. Geçen süre:' 'N1 must use the approved concise heartbeat fallback.'
Assert-Contains 'src/Talvora/WorklogCopy.cs' 'Kod değişikliği uygulandı' 'N1 must use the approved past-tense source-edit success title.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'Şimdi sıradaki adıma geçiyorum' 'Desktop worklog must not promise an unverified next step.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'yeniden deneyeceğim|düzelteceğim' 'Desktop worklog failure copy must not promise future work that has not happened.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'CallToolResult' 'Desktop worklog must recognize MCP tool error results, not only thrown exceptions.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'IsError\s+is\s+true' 'MCP tool error results must become a visible failure state.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'StructuredContent' 'Structured tool results must be inspected for non-throwing failures.'
Assert-Contains 'src/Talvora/StructuredResultFailure.cs' 'exitCode' 'Nonzero command and test results must become a visible failure state.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'StructuredResultFailure\.IsFailure' 'Desktop worklog must use the shared structured failure detector.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'WorklogFailureClassifier\.Describe' 'N1 detected failures must use approved plain-language reason classification.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'GetOrCreateSession' 'Meaningful tool calls must coalesce into a project/actor desktop worklog session.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' '_activeWorklog' 'Parallel meaningful operations must be tracked so one call cannot report premature overall completion.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'session\.Failure' 'A failure must survive concurrent work until the session reaches a safe terminal state.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'session\.Cancellation' 'A cancellation must survive concurrent work until the session reaches a safe terminal state.'
Assert-Contains 'src/Talvora/TalvoraDesktopWorklogState.cs' 'SHA256\.HashData' 'Each project/actor worklog must have its deterministic session identity.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'DesktopProgressEvidence' 'Desktop worklog must carry structured real evidence separately from conversational copy.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'Version\s*=\s*4' 'Desktop progress wire format must use the approved v4 protocol.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'MaximumFrameBytes\s*=\s*4 \* 1024 \* 1024' 'Desktop progress IPC must retain a hard frame-size ceiling with room for escaped full code.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'WriteFrameAsync' 'Desktop progress sender must use cancellation-aware framed writes.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'ReadFrameAsync' 'Desktop progress receiver must use bounded framed reads.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'SerializeToBoundedPayload' 'Desktop progress aggregate payload size must be deterministically bounded.'
Assert-Contains 'src/Talvora.Shared/DesktopProgressProtocol.cs' 'RedactAndClip' 'Desktop progress wire fields and evidence must pass canonical sensitive-data redaction before transport.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressEvidence.cs' 'RedactSensitivePreviewLine' 'Code evidence preview must redact likely secret-bearing lines.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressEvidence.cs' 'MaximumPreviewLines' 'Desktop diff evidence must not silently discard changed lines.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressEvidence.cs' 'MaximumPreviewCharacters\s*=\s*DesktopProgressProtocol.MaximumCodeCharacters' 'Code evidence must use the dedicated bounded code budget.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressEvidence.cs' 'BuildStructuredEditEvidence' 'Structured apply-edits operations must expose old/new code evidence, not only apply-patch operations.'
# N5: the approved Phosphor card supersedes the removed legacy notification view.
# Test all eight copied files against Claude's immutable SHA-256 reference manifest.
$phosphorTargets = @{
    'WorklogTheme.xaml' = 'src/Talvora.Tray/Worklog/WorklogTheme.xaml'
    'WorklogCardView.xaml' = 'src/Talvora.Tray/Worklog/WorklogCardView.xaml'
    'WorklogCardView.xaml.cs' = 'src/Talvora.Tray/Worklog/WorklogCardView.xaml.cs'
    'WorklogCardViewModel.cs' = 'src/Talvora.Tray/Worklog/WorklogCardViewModel.cs'
    'UnifiedDiffViewParser.cs' = 'src/Talvora.Tray/Worklog/UnifiedDiffViewParser.cs'
    'WorklogCardMapper.cs' = 'src/Talvora.Tray/Worklog/WorklogCardMapper.cs'
    'WorklogPreviewScenarios.cs' = 'src/Talvora.Tray/Worklog/WorklogPreviewScenarios.cs'
    'DesktopProgressNotificationWindow.cs' = 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs'
}
$manifest = Join-Path $root 'docs/codex/reference/worklog-phosphor/SHA256SUMS.txt'
$referenceLines = @(Get-Content -LiteralPath $manifest | Where-Object { $_ -match '^[a-f0-9]{64}\s+src/' })
if ($referenceLines.Count -ne $phosphorTargets.Count) { throw 'N5 approved reference inventory must contain exactly eight sources.' }
foreach ($line in $referenceLines) {
    if ($line -cnotmatch '^([a-f0-9]{64})\s+src/([^/]+)$') { throw 'N5 malformed reference checksum entry.' }
    $expected = $Matches[1]
    $name = $Matches[2]
    if (-not $phosphorTargets.ContainsKey($name)) { throw ('N5 unknown reference: ' + $name) }
    foreach ($relativePath in @(("docs/codex/reference/worklog-phosphor/src/$name"), ([string]$phosphorTargets[$name]))) {
        $file = Join-Path $root $relativePath
        if (-not (Test-Path -LiteralPath $file)) { throw ('N5 missing approved source: ' + $relativePath) }
        $normalized = [IO.File]::ReadAllText($file).TrimStart([char]0xFEFF).Replace("`r`n", "`n")
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($normalized))).ToLowerInvariant()
        if ($digest -cne $expected) { throw ('N5 Phosphor source differs from approved checksum: ' + $relativePath) }
    }
}
Assert-NotContains 'src/Talvora.Tray/Worklog/WorklogCardView.xaml' 'TERMINAL // REAL EXECUTION|KANIT // GERÇEK VERİ' 'Phosphor worklog must not recreate the prohibited legacy evidence headings.'
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
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'BuildPlainCompletion' 'N1 removes obsolete completion prose and uses approved WorklogCopy fallback.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'TALVORA_UPDATE_DETACHED' 'Detached self-update completion must be detected from the real installer handoff marker.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'WorklogCopy\.Deploy\.Succeeded' 'N1 retains truthful deferred installer handoff using approved copy.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'TryBuildDeferredCompletion' 'Desktop worklog must use truthful deferred completion copy when installation continues outside the tool call.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'Talvora işlemi:' 'Unknown tools must never leak raw technical tool names into the user-facing worklog.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'regresyon|derleme|kaynak kod|Git işlemi|repository işlemi|runtime kanıt|process veya job|tunnel işlemi|servis ve tray' 'User-facing desktop worklog language must not expose developer jargon; canonical technical identity belongs only in the compact metadata line.'
Assert-Contains 'src/Talvora/Program.cs' 'request\.Params\.Arguments' 'Desktop progress may use tool arguments to create a meaningful user-facing work subject.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'BuildNarrative\(toolName, arguments, initialEvidence\)' 'Desktop progress must translate technical tool calls into meaningful work subjects.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'ShouldNotifyToolCall' 'Desktop progress must suppress low-value internal inspection chatter.'
Assert-NotContains 'src/Talvora/TalvoraDesktopProgressNarrative.cs' 'new\(\s*OperationCategory\.Inspection' 'N1 inspection calls must not create worklog narrative cards.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' '"read_source" or' 'N1 suppresses inspection calls from worklog, including failed reads.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'GetDefaultInteractiveUser' 'Desktop progress must target the default interactive Windows session (console preferred, RDP supported).'
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
# N5 Phosphor: check preserved behaviour in the approved WPF resources/window.
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogTheme.xaml' 'Setter Property="TextWrapping" Value="Wrap"' 'User-facing worklog text must wrap.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardView.xaml' 'VerticalScrollBarVisibility="Auto"' 'Code evidence must remain scrollable.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardView.xaml' 'HorizontalScrollBarVisibility="Disabled"' 'Narrow code terminal must not introduce horizontal scrolling.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardView.xaml' 'ItemsSource="{Binding CodeLines}"' 'Phosphor code terminal must display real diff lines.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardView.xaml' 'Visibility="{Binding HasCode' 'Code surface must collapse when no real code exists.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'ShowActivated\s*=\s*false' 'Desktop worklog must never steal keyboard focus.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'SucceededLifetime\s*=\s*TimeSpan\.FromMinutes\(3\)' 'Succeeded cards must remain visible for three minutes.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'FailedLifetime\s*=\s*TimeSpan\.FromMinutes\(8\)' 'Failed cards must remain visible for eight minutes.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' '!IsTerminal' 'Active worklog cards must not auto-dismiss.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'DoubleAnimation' 'Desktop worklog must retain approved enter/exit motion.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'DragMove\(\)' 'Desktop progress must remain user-draggable.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'PinStateChangeRequested' 'Desktop progress must preserve the pin control.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'ResizeMode\s*=\s*ResizeMode\.CanResize' 'Desktop card must remain user-resizable.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'WindowChrome\.SetWindowChrome' 'Borderless card must retain native edge resizing.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'DefaultWidth\s*=\s*460' 'N5 Phosphor card must use its approved compact default width.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'MinWidth\s*=\s*380' 'Phosphor compact minimum width must be preserved.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'MaxWidth\s*=\s*720' 'Phosphor maximum width must be bounded.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'MinHeight\s*=\s*120' 'Code-free worklog must stay compact.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'AutomationProperties\.SetName' 'Worklog card must remain accessible to UI Automation.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'SystemParameters\.ClientAreaAnimation' 'Motion must respect Windows reduced-motion settings.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardView.xaml.cs' 'ApplyContrast\(bool highContrast\)' 'Phosphor must preserve a true Windows high-contrast mode.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogTheme.xaml' 'Wl.Mono.*Cascadia Mono' 'Code and copy must use the approved terminal font fallback.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardView.xaml' 'Segoe Fluent Icons, Segoe MDL2 Assets' 'Phosphor controls must use the approved native icon font.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogTheme.xaml' 'Wl.Green.Color' 'Phosphor semantic colours must come from reviewed design tokens.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'isPointerOver\s*\|\|\s*isClosing' 'Hover must keep auto-dismiss paused.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'MaximumVisibleCardsPerLane\s*=\s*1' 'Desktop progress must keep one worklog card and one independent alert lane without uncontrolled stacking.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' '_automaticScreenDeviceName' 'Automatic desktop progress placement must stay anchored to one monitor for the active card generation.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'ApplyHeightConstraint' 'Desktop progress height must be clamped to the active monitor work area.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressPresentationState.cs' 'SuppressOutOfOrder' 'Late progress frames must not overwrite a newer terminal state.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressPresentationState.cs' 'SuppressDismissed' 'Manual dismissal must suppress the same worklog generation.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'JsonFileStore\.ReadBounded' 'Desktop progress placement restore must use a bounded JSON reader.'
Assert-Contains 'src/Talvora.Tray/TrayApplicationContext.cs' 'DesktopProgressLane\.Alert' 'Operational tray warnings and errors must use the independent alert lane.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'Math\.Max\(10, lastFrame\.ExpectedNextUpdateSeconds\) \* 2' 'Active work must mark stale after twice the producer interval, never a fixed ad hoc timeout.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'lastFrameReceivedUtc = DateTimeOffset\.UtcNow' 'Stale detection must use the real received-frame time rather than dragging or pinning.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardMapper.cs' '"Yanıt bekleniyor"' 'Stale work must retain the approved N2 waiting title without inventing a result.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardViewModel.cs' 'INotifyPropertyChanged' 'Repeated evidence updates must use WPF property notifications instead of rebuilding the window.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardView.xaml' 'Visibility="{Binding HasCode' 'The terminal must disappear entirely when no real code exists.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'OrderByDescending\(static window => window\.IsTerminal\)' 'Completed cards must retire before active work when the visible-card budget is full.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'desktop-progress-placement\.json' 'Desktop progress pin state and position must persist across tray restarts.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'PlacementDocumentVersion\s*=\s*2' 'Desktop progress placement persistence must use the size-aware v2 document.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'HasUserSize' 'Desktop progress placement must remember whether the user chose a custom size.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'WidthDip' 'Desktop progress placement must persist the user-selected width in device-independent units.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'HeightDip' 'Desktop progress placement must persist the user-selected height in device-independent units.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'ApplyPreferredSize' 'New notification generations must restore the saved user size before they are shown.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'WmExitSizeMove' 'Desktop progress must distinguish completed interactive resizing from programmatic layout changes.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'UserResizeCompleted' 'Completed user resizing must be persisted without treating automatic diff expansion as a user preference.'
Assert-NotContains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'AutomaticDiffWidthDip|EnsureDiffWidth|AutomaticSizePersistenceRequested|_autoDiffExpanded' 'Code evidence must not automatically widen a window or save a computed size.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'view\.CodeMaxHeight\s*=\s*Math\.Clamp' 'Terminal height must expand through the approved WPF control.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'Math\.Clamp\(heightDip - 240, 160, 900\)' 'Code viewport must retain approved bounded user-selected height.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs' 'SizeToContent\s*=\s*SizeToContent\.Height' 'Code and code-free worklog cards must size naturally without widening.'
Assert-NotContains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'CaptureAutomaticPreferredSize|MarkAutomaticSizePersistenceHandled' 'Only direct user resizing may update persisted dimensions.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'if \(_hasPreferredSize\)' 'The presenter must apply only explicitly saved user dimensions.'
Assert-Contains 'src/Talvora.Tray/Worklog/WorklogCardView.xaml.cs' 'set => CodeScroll\.MaxHeight = value' 'User-selected height must control the real terminal scroll viewport.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'GetWindowRect' 'Desktop progress placement must capture native Windows pixel coordinates.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'SetWindowPos' 'Desktop progress placement must use native no-activate pixel positioning across mixed-DPI monitors.'
Assert-Contains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'Desktop progress notification publish failed' 'Desktop progress UI failures must be isolated and logged instead of terminating the tray application.'
Assert-NotContains 'src/Talvora.Tray/DesktopProgressNotificationPresenter.cs' 'workArea\.Bottom\s*/\s*dpi\.DpiScaleY' 'Mixed-DPI desktop progress placement must not divide global screen coordinates by one window DPI scale.'
Assert-Contains 'src/Talvora.Tray/TrayApplicationContext.cs' '_desktopProgressPresenter\.Publish' 'Legacy tray status notifications must use the full-text progress-card surface.'
Assert-NotContains 'src/Talvora.Tray/TrayApplicationContext.cs' 'BalloonTipText\s*=\s*text\.Length\s*<=\s*240' 'Arbitrary 240-character tray notification truncation is forbidden.'
Assert-NotContains 'src/Talvora.Tray/TrayApplicationContext.cs' 'ShowBalloon|showBalloon' 'Legacy balloon terminology must not survive after routing notifications through the modern progress-card surface.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'IsTruthy\(Environment\.GetEnvironmentVariable\("CI"\)\)' 'Desktop progress must stay noninteractive in CI.'
Assert-Contains '.github/workflows/windows-ci.yml' 'Invoke-QualityGate\.ps1\s+-Scope\s+Full' 'CI must execute the same full quality gate used locally.'
Assert-Contains 'scripts/Invoke-QualityGate.ps1' '--locked-mode' 'The shared quality gate must perform locked restores.'
Assert-Contains 'scripts/Invoke-QualityGate.ps1' 'ModernizationPolicyRegression\.ps1' 'The shared quality gate must include the modernization policy regression.'
Assert-Contains 'scripts/Invoke-QualityGate.ps1' 'Talvora\.Notification\.Regression' 'The shared quality gate must include desktop notification regression.'
Assert-Contains 'scripts/Invoke-QualityGate.ps1' 'Validate-Context7Gate\.ps1' 'The shared quality gate must enforce fresh Context7 evidence.'
Assert-Contains 'scripts/Invoke-QualityGate.ps1' 'Validate-ProjectDeliveryGate\.ps1' 'The shared quality gate must enforce the Context7 project delivery gate.'
Assert-Contains 'scripts/Invoke-QualityGate.ps1' 'Talvora\.SourceEdit\.Regression' 'The shared quality gate must execute the previously omitted source edit behavioural suite.'
Assert-Contains '.github/workflows/windows-ci.yml' 'actions/checkout@[0-9a-f]{40}\s+#\s+v\d+\.\d+\.\d+' 'actions/checkout must be pinned to a full release SHA.'
Assert-Contains '.github/workflows/windows-ci.yml' 'actions/setup-dotnet@[0-9a-f]{40}\s+#\s+v\d+\.\d+\.\d+' 'actions/setup-dotnet must be pinned to a full release SHA.'
Assert-Contains '.github/dependabot.yml' 'package-ecosystem:\s*"dotnet-sdk"' 'Dependabot must keep global.json current.'
Assert-Contains '.github/dependabot.yml' 'dependency-name:\s*"Microsoft\.Build\.Framework"' 'Dependabot must retain the net10-compatible Microsoft.Build.Framework guard.'
Assert-Contains '.github/dependabot.yml' 'versions:\s*\r?\n\s*-\s*">=18\.0\.0"' 'Dependabot must ignore the incompatible Microsoft.Build.Framework 18.x line until the .NET 11 migration.'
Assert-Contains 'scripts/Invoke-QualityGate.ps1' 'Get-ChildItem.*-LiteralPath.*tests.*-Filter.*\*\.ps1' 'The shared full gate must discover every PowerShell regression, including tunnel-client integration.'
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

# Execute the workflow's actual SDK gate with event fixtures and a bounded Git stub.
# No repository history, refs, SDK environment overrides or service state is mutated.
$workflow = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/windows-ci.yml'))
$gateMatch = [regex]::Match($workflow, '(?ms)^      - name: Require lock files with SDK updates\r?\n        shell: pwsh\r?\n        run: \|\r?\n(?<script>.*?)(?=^      - )')
if (-not $gateMatch.Success) { throw 'CI must require lock files with SDK updates.' }
$sdkGate = [scriptblock]::Create(($gateMatch.Groups['script'].Value -replace '(?m)^          ', ''))
$eventPath = [IO.Path]::GetTempFileName()
$savedEventPath = $env:GITHUB_EVENT_PATH
$savedEventName = $env:GITHUB_EVENT_NAME
try {
    $env:GITHUB_EVENT_PATH = $eventPath
    foreach ($case in @(
        @{ Name = 'PR SDK only'; Event = 'pull_request'; Changed = @('global.json'); Updated = @('global.json'); Error = 'global.json changed without' },
        @{ Name = 'PR SDK with locks'; Event = 'pull_request'; Changed = @('global.json', 'src/Talvora.Tray/packages.lock.json'); Updated = @('global.json', 'src/Talvora.Tray/packages.lock.json') },
        @{ Name = 'push SDK only'; Event = 'push'; Changed = @('global.json'); Updated = @('global.json'); Error = 'global.json changed without' },
        @{ Name = 'push SDK with installer lock'; Event = 'push'; Changed = @('global.json', 'src/Talvora.Installer/packages.lock.json'); Updated = @('global.json', 'src/Talvora.Installer/packages.lock.json') },
        @{ Name = 'new branch'; Event = 'push'; NewBranch = $true; Changed = @('global.json', 'tests/Talvora.Smoke/packages.lock.json'); Updated = @('global.json', 'tests/Talvora.Smoke/packages.lock.json') },
        @{ Name = 'unrelated change'; Event = 'push'; Changed = @('README.md'); Updated = @('README.md') },
        @{ Name = 'deleted lock is insufficient'; Event = 'push'; Changed = @('global.json', 'src/Talvora.Tray/packages.lock.json'); Updated = @('global.json'); Error = 'global.json changed without' },
        @{ Name = 'missing push revision'; Event = 'push'; Changed = @(); Updated = @(); GitFailure = $true; Error = 'could not read the event change set' },
        @{ Name = 'missing PR merge base'; Event = 'pull_request'; Changed = @(); Updated = @(); GitFailure = $true; Error = 'could not determine the PR merge base' }
    )) {
        $env:GITHUB_EVENT_NAME = $case.Event
        $baseSha = '1' * 40
        $headSha = '2' * 40
        @{
            before = if ($case.NewBranch) { '0' * 40 } else { $baseSha }
            after = $headSha
            pull_request = @{ base = @{ sha = $baseSha }; head = @{ sha = $headSha } }
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $eventPath -Encoding utf8
        $failure = $null
        try {
            & {
                function git {
                    $global:LASTEXITCODE = 0
                    switch ($args[0]) {
                        'merge-base' {
                            if ($args[1] -ne $baseSha -or $args[2] -ne $headSha) { throw 'Wrong PR revision range.' }
                            if ($case.GitFailure) { $global:LASTEXITCODE = 128; return }
                            'merge-base-sha'
                        }
                        'hash-object' { 'empty-tree-sha' }
                        'diff' {
                            $expectedBase = if ($case.Event -eq 'pull_request') { 'merge-base-sha' } elseif ($case.NewBranch) { 'empty-tree-sha' } else { $baseSha }
                            if ($args[-2] -ne $expectedBase -or $args[-1] -ne $headSha) { throw 'Wrong event diff range.' }
                            if ($case.GitFailure) { $global:LASTEXITCODE = 128; return }
                            if ($args -contains '--diff-filter=AM') { $case.Updated } else { $case.Changed }
                        }
                        default { throw 'Unexpected Git operation in SDK lock gate.' }
                    }
                }
                & $sdkGate
            }
        }
        catch { $failure = $_.Exception.Message }
        if ($case.Error) {
            if (-not $failure -or -not $failure.Contains($case.Error)) { throw "SDK gate case '$($case.Name)' did not produce the expected diagnostic: $failure" }
        }
        elseif ($failure) { throw "SDK gate case '$($case.Name)' failed: $failure" }
    }
}
finally {
    $env:GITHUB_EVENT_PATH = $savedEventPath
    $env:GITHUB_EVENT_NAME = $savedEventName
    Remove-Item -LiteralPath $eventPath -Force
}

Write-Host 'MODERNIZATION_POLICY_GREEN'
