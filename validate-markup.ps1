$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
$sourcePath = Join-Path $PSScriptRoot 'src/PocketCast.cs'
if (-not (Test-Path -LiteralPath $sourcePath)) { $sourcePath = Join-Path $PSScriptRoot 'pocket-cast/src/PocketCast.cs' }
$source = [System.IO.File]::ReadAllText($sourcePath)
$match = [regex]::Match($source, 'return @"(?<markup><Grid[\s\S]*?)";')
if (-not $match.Success) { throw 'XAML markup was not found.' }
$markup = $match.Groups['markup'].Value.Replace('""', '"')
# Parse the WPF resource/control graph without opening a window or running app code.
$layout = [System.Windows.Markup.XamlReader]::Parse($markup)
$names = [regex]::Matches($source, 'Find<[^>]+>\("(?<name>[^"]+)"\)') | ForEach-Object { $_.Groups['name'].Value } | Sort-Object -Unique
foreach ($name in $names) {
    if ($null -eq $layout.FindName($name)) { throw "Missing named control: $name" }
}
$connection = $layout.FindName('Connection')
if ($connection.Items.Count -ne 2) { throw 'Expected Wi-Fi and USB connection modes.' }
if ($layout.FindName('UsbPanel').Visibility -ne [System.Windows.Visibility]::Collapsed) { throw 'USB controls should be hidden by default.' }
Write-Output "WPF markup parsed; all $($names.Count) referenced controls found. No window opened."
