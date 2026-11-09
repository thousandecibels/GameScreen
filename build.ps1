param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'build')
)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs'
$destination = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$output = Join-Path $destination 'GameScreen.exe'
$icon = Join-Path $PSScriptRoot 'assets\GameScreen.ico'

& (Join-Path $framework 'csc.exe') /nologo "/win32manifest:$PSScriptRoot\GameScreen.manifest" /target:winexe /optimize+ /warnaserror+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/win32icon:$icon" "/resource:$icon,GameScreen.ico" "/out:$output" $sources.FullName
if ($LASTEXITCODE -ne 0) {
    throw "The compiler exited with code $LASTEXITCODE."
}
Write-Output "Built GameScreen at $output"
