param([string]$Destination = (Join-Path $PSScriptRoot 'build'), [switch]$SkipSelfTest)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Destination | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$assemblies = Join-Path $env:WINDIR 'Microsoft.NET/assembly'
$references = @('PresentationFramework', 'PresentationCore', 'WindowsBase', 'System.Xaml') | ForEach-Object {
    $assemblyName = $_
    $match = Get-ChildItem -Path $assemblies -Filter "$assemblyName.dll" -Recurse | Select-Object -First 1
    if (-not $match) { throw "Missing WPF assembly: $assemblyName" }
    '/reference:' + $match.FullName
}
$sourceFile = Join-Path $PSScriptRoot 'src\PocketCast.cs'
$outputFile = Join-Path $Destination 'PocketCast.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Xml.Linq.dll @references "/out:$outputFile" $sourceFile
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath "$PSScriptRoot/src/PocketCast.exe.config" -Destination $Destination
if ($SkipSelfTest) { Write-Output 'Built PocketCast.exe; execution self-test explicitly skipped.' }
else {
    $test = Start-Process -FilePath "$Destination/PocketCast.exe" -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw "Options self-test failed: $($test.ExitCode)" }
    Write-Output "Built PocketCast.exe; options self-test passed."
}
