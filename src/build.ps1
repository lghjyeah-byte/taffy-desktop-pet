$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$wpf = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu "/win32icon:$projectRoot\outputs\taffy.ico" "/out:$projectRoot\outputs\TaffyPet.exe" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$PSScriptRoot\TaffyPet.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$check = Start-Process -FilePath "$projectRoot\outputs\TaffyPet.exe" -ArgumentList @('--smoke',"$projectRoot\tests") -WindowStyle Hidden -PassThru -Wait
if ($check.ExitCode -ne 0) { throw 'Smoke test failed' }


