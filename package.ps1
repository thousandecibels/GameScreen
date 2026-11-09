param(
    [Parameter(Mandatory)]
    [string]$WixBin,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$candle = Join-Path $WixBin 'candle.exe'
$light = Join-Path $WixBin 'light.exe'
if (-not (Test-Path -LiteralPath $candle -PathType Leaf) -or -not (Test-Path -LiteralPath $light -PathType Leaf)) {
    throw 'Pass the folder containing the WiX 3 candle.exe and light.exe tools to -WixBin.'
}

$icon = Join-Path $PSScriptRoot 'assets\GameScreen.ico'
$installer = Join-Path $PSScriptRoot 'installer\GameScreen.wxs'
$destination = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$intermediate = Join-Path ([IO.Path]::GetTempPath()) ('GameScreen-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $intermediate | Out-Null
$appPath = Join-Path $intermediate 'GameScreen.exe'
$object = Join-Path $intermediate 'GameScreen.wixobj'
$msi = Join-Path $intermediate 'Setup.msi'
$zip = Join-Path $intermediate 'Portable.zip'
$marker = Join-Path $intermediate 'portable.flag'

try {
    & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $intermediate
    $app = Get-Item -LiteralPath $appPath
    $version = '{0}.{1}.{2}' -f $app.VersionInfo.FileMajorPart, $app.VersionInfo.FileMinorPart, $app.VersionInfo.FileBuildPart
    # Rebuilding a release keeps its product code. A new version gets a major upgrade.
    $productCode = 'F1E08BE3-86DF-41A7-B856-{0:X4}{1:X4}{2:X4}' -f $app.VersionInfo.FileMajorPart, $app.VersionInfo.FileMinorPart, $app.VersionInfo.FileBuildPart

    & $candle -nologo -wx -arch x64 "-dAppExe=$($app.FullName)" "-dAppIcon=$icon" "-dProductVersion=$version" "-dProductCode=$productCode" -out $object $installer
    if ($LASTEXITCODE -ne 0) { throw "WiX compilation failed with exit code $LASTEXITCODE." }
    & $light -nologo -wx -spdb -out $msi $object
    if ($LASTEXITCODE -ne 0) { throw "WiX linking failed with exit code $LASTEXITCODE." }

    Set-Content -LiteralPath $marker -Value 'GameScreen portable mode' -Encoding ascii
    $readme = Join-Path $PSScriptRoot 'installer\Portable.txt'
    Compress-Archive -LiteralPath $app.FullName, $marker, $readme -DestinationPath $zip

    Copy-Item -LiteralPath $msi -Destination (Join-Path $destination "GameScreen-$version-Setup-x64.msi")
    Copy-Item -LiteralPath $zip -Destination (Join-Path $destination "GameScreen-$version-Portable.zip")
    Write-Output "Built GameScreen $version packages in $destination"
} finally {
    foreach ($file in @($appPath, $object, $msi, $zip, $marker)) {
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            Remove-Item -LiteralPath $file
        }
    }
    if (@(Get-ChildItem -LiteralPath $intermediate -Force).Count -eq 0) {
        [IO.Directory]::Delete($intermediate, $false)
    }
}
