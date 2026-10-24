$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$themeRoot = Join-Path $repoRoot 'src/Talvora.Tray/Themes'
$xamlNamespace = 'http://schemas.microsoft.com/winfx/2006/xaml'

function Read-Theme {
    param([string] $Name)
    $file = Join-Path $themeRoot ("Tokens.$Name.xaml")
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Missing $Name token dictionary."
    }
    [xml] $xml = Get-Content -LiteralPath $file -Raw
    $colors = @{}
    foreach ($color in $xml.SelectNodes("//*[local-name()='Color']")) {
        $key = $color.GetAttribute('Key', $xamlNamespace)
        if ($key) { $colors[$key] = $color.InnerText.Trim() }
    }
    if ($colors.Count -eq 0) { throw "$Name has no Color tokens." }
    $brushes = @{}
    foreach ($brush in $xml.SelectNodes("//*[local-name()='SolidColorBrush']")) {
        $key = $brush.GetAttribute('Key', $xamlNamespace)
        if ($key) { $brushes[$key] = $brush }
    }
    [pscustomobject]@{ Colors = $colors; Brushes = $brushes }
}

function Luminance {
    param([string] $Hex)
    if ($Hex -notmatch '^#[0-9a-fA-F]{6}$') { throw "Invalid colour $Hex." }
    $linear = @(1, 3, 5 | ForEach-Object {
        $v = [Convert]::ToInt32($Hex.Substring($_, 2), 16) / 255.0
        if ($v -le 0.04045) { $v / 12.92 } else { [Math]::Pow(($v + 0.055) / 1.055, 2.4) }
    })
    0.2126 * $linear[0] + 0.7152 * $linear[1] + 0.0722 * $linear[2]
}

function Assert-Contrast {
    param([hashtable] $Colors, [string] $Foreground, [string] $Background, [string] $Theme)
    $a = Luminance $Colors["$Foreground.Color"]
    $b = Luminance $Colors["$Background.Color"]
    $ratio = ([Math]::Max($a, $b) + 0.05) / ([Math]::Min($a, $b) + 0.05)
    if ($ratio -lt 4.5) { throw "$Theme $Foreground / $Background contrast $ratio is below 4.5:1." }
}

$gece = Read-Theme 'Gece'
$gun = Read-Theme 'Gun'
$geceKeys = @($gece.Colors.Keys | Sort-Object)
$gunKeys = @($gun.Colors.Keys | Sort-Object)
if (($geceKeys -join ',') -cne ($gunKeys -join ',')) { throw 'Theme colour keys differ.' }
foreach ($theme in @(@('Gece', $gece), @('Gun', $gun))) {
    $name = $theme[0]
    $palette = $theme[1]
    foreach ($background in @('Tv.Bg', 'Tv.Surface', 'Tv.SurfaceRaised', 'Tv.SurfaceSunken')) {
        foreach ($foreground in @('Tv.Text', 'Tv.TextMuted', 'Tv.TextFaint')) {
            Assert-Contrast $palette.Colors $foreground $background $name
        }
    }
    Assert-Contrast $palette.Colors 'Tv.AccentInk' 'Tv.Accent' $name
    foreach ($state in @('Ready', 'Attention', 'Offline', 'Neutral', 'Repairing')) {
        $key = "Tv.$state.Soft"
        if (-not $palette.Brushes.ContainsKey($key) -or $palette.Brushes[$key].Opacity -ne '0.14') {
            throw "$name missing 14%-opacity $key brush."
        }
    }
}
Write-Output 'CONTROL_CENTER_THEME_CONTRAST_GREEN'
