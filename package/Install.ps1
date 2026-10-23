param(
 [string]$GameDirectory,
 [switch]$Disable,
 [switch]$Silent
)
$ErrorActionPreference='Stop'
try {
 if([string]::IsNullOrWhiteSpace($GameDirectory)){
   if($Silent){throw '请通过 -GameDirectory 指定包含 Autolantis.exe 的目录。'}
   Add-Type -AssemblyName System.Windows.Forms
   $picker=New-Object Windows.Forms.FolderBrowserDialog
   $picker.Description='选择包含 Autolantis.exe 的游戏目录'
   try {
     if($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK){return}
     $GameDirectory=$picker.SelectedPath
   } finally {$picker.Dispose()}
 }
 $target=[IO.Path]::GetFullPath($GameDirectory)
 if(!(Test-Path -LiteralPath (Join-Path $target 'Autolantis.exe'))){$target=Join-Path $target 'Autolantis'}
 if(!(Test-Path -LiteralPath (Join-Path $target 'Autolantis.exe'))){throw '未找到 Autolantis.exe。'}
 if(Get-Process -Name Autolantis -ErrorAction SilentlyContinue){throw '请先完全退出游戏。'}
 $dll=Join-Path $target 'BepInEx\plugins\Autolantis.SkipFix.dll'
 if($Disable){
   if(Test-Path -LiteralPath $dll){Move-Item -LiteralPath $dll -Destination ($dll+'.disabled') -Force}
   $message='性能修复补丁已关闭，下次启动生效。'
 }else{
   if((Get-FileHash -LiteralPath (Join-Path $target 'GameAssembly.dll')).Hash -ne 'E8D4E7E8C484562BFA1B00A59B17AD582CE384CE96255B24AC8C6F90DA1DE4F4'){throw '游戏版本不匹配，未安装。'}
   $core=Join-Path $target 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'
   if(!(Test-Path -LiteralPath $core)){throw '请先按 README 安装 BepInEx Unity.IL2CPP-win-x64 6.0.0-be.790。'}
   $expected=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'loader-sha256.txt') -Raw).Trim()
   if((Get-FileHash -LiteralPath $core).Hash -ne $expected){throw 'BepInEx 版本与测试版本不一致。'}
   New-Item -ItemType Directory -Path (Split-Path $dll) -Force | Out-Null
   Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Autolantis.SkipFix.dll') -Destination $dll -Force
   $message='性能修复补丁已安装。请从 Steam 启动，测试一轮结算直到完成，然后退出游戏。'
 }
 Write-Output $message
 if(!$Silent){Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show($message,'Autolantis Skip 修复') | Out-Null}
}catch{
 if(!$Silent){Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show($_.Exception.Message,'未完成') | Out-Null}
 throw
}