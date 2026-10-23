$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$payload=Join-Path $projectRoot 'work\installer-payload-v1.4'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
foreach($name in @('TaffyPet.exe','sprites.png','motions','audio','使用说明.md','语音来源.json')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot ('outputs\'+$name)) -Destination $payload -Recurse -Force
}
$compiler=Join-Path $projectRoot 'work\installer-tools\nsis-3.13\makensis.exe'
if(!(Test-Path -LiteralPath $compiler)) { throw 'NSIS 3.13 compiler missing in project work/installer-tools.' }
& $compiler /V2 (Join-Path $PSScriptRoot 'installer.nsi')
if($LASTEXITCODE -ne 0) { throw 'Installer build failed' }