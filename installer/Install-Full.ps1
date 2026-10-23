param([string]$GameDirectory,[switch]$Disable,[switch]$Silent)
$ErrorActionPreference='Stop'
$tempRoot=$null
try {
 if([string]::IsNullOrWhiteSpace($GameDirectory)){
  if($Silent){throw '请指定 -GameDirectory。'}
  Add-Type -AssemblyName System.Windows.Forms
  $picker=New-Object Windows.Forms.FolderBrowserDialog
  $picker.Description='选择包含 Autolantis.exe 的目录'
  try{if($picker.ShowDialog() -ne 'OK'){return};$GameDirectory=$picker.SelectedPath}finally{$picker.Dispose()}
 }
 $target=[IO.Path]::GetFullPath($GameDirectory)
 if(!(Test-Path -LiteralPath (Join-Path $target 'Autolantis.exe'))){$target=Join-Path $target 'Autolantis'}
 if(!(Test-Path -LiteralPath (Join-Path $target 'Autolantis.exe'))){throw '未找到 Autolantis.exe。'}
 if(Get-Process -Name Autolantis -ErrorAction SilentlyContinue){throw '请先退出游戏。'}
 $plugin=Join-Path $target 'BepInEx/plugins/Autolantis.SkipFix.dll'
 if($Disable){
  if(Test-Path -LiteralPath $plugin){Move-Item -LiteralPath $plugin -Destination ($plugin+'.disabled') -Force}
  $message='性能补丁已关闭。共享 BepInEx 加载器和其他插件保留。'
 }else{
  if((Get-FileHash -LiteralPath (Join-Path $target 'GameAssembly.dll')).Hash -ne 'E8D4E7E8C484562BFA1B00A59B17AD582CE384CE96255B24AC8C6F90DA1DE4F4'){throw '游戏版本不匹配，未安装。'}
  $payload=Join-Path $PSScriptRoot 'payload'
  $archive=Join-Path $payload 'BepInEx-IL2CPP-win-x64-be790.zip'
  if((Get-FileHash -LiteralPath $archive).Hash -ne '7E2C83E38DB7744D6457839BC869B311AA29A47517938A3A7643BE24D00C72EC'){throw '加载器压缩包校验失败，请重新下载。'}
  $pluginSource=Join-Path $payload 'Autolantis.SkipFix.dll'
  $expected=(Get-Content -LiteralPath (Join-Path $payload 'plugin-sha256.txt') -Raw).Trim()
  if((Get-FileHash -LiteralPath $pluginSource).Hash -ne $expected){throw '插件校验失败，请重新下载。'}
  $core=Join-Path $target 'BepInEx/core/BepInEx.Unity.IL2CPP.dll'
  if((Test-Path -LiteralPath $core) -and (Get-FileHash -LiteralPath $core).Hash -ne 'AFC9CC7E3C407CF1324C1CB669ADDD6FD162354FFEC9BBB83B78C83CA6941305'){throw '已有不同版本的 BepInEx，未混合覆盖。请先单独处理旧加载器。'}
  $tempRoot=Join-Path ([IO.Path]::GetTempPath()) ('AutolantisSetup-'+[Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $tempRoot | Out-Null
  Expand-Archive -LiteralPath $archive -DestinationPath $tempRoot
  $copyPlan=New-Object Collections.Generic.List[object]
  foreach($file in Get-ChildItem -LiteralPath $tempRoot -Recurse -File){
   $relative=$file.FullName.Substring($tempRoot.Length+1)
   $destination=Join-Path $target $relative
   if(Test-Path -LiteralPath $destination){
    if($relative -eq 'changelog.txt'){continue}
    if($relative -eq 'doorstop_config.ini'){
     $ini=Get-Content -LiteralPath $destination -Raw
     if($ini -notmatch '(?m)^enabled\s*=\s*true\s*$' -or $ini -notmatch '(?m)^target_assembly\s*=\s*BepInEx[\\/]core[\\/]BepInEx.Unity.IL2CPP.dll\s*$'){
      throw '现有 doorstop_config.ini 未启用正确的 IL2CPP 加载器，未覆盖。'
     }
     continue
    }
    if((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw "发现已有冲突文件：$relative。未安装，避免影响现有加载器。"}
   }else{$copyPlan.Add([pscustomobject]@{Source=$file.FullName;Destination=$destination})}
  }
  # All conflicts have been checked before modifying the game directory.
  foreach($item in $copyPlan){
   New-Item -ItemType Directory -Path (Split-Path $item.Destination) -Force | Out-Null
   Copy-Item -LiteralPath $item.Source -Destination $item.Destination
  }
  New-Item -ItemType Directory -Path (Split-Path $plugin) -Force | Out-Null
  if((Test-Path -LiteralPath $plugin) -and !(Test-Path -LiteralPath ($plugin+'.backup'))){Copy-Item -LiteralPath $plugin -Destination ($plugin+'.backup')}
  Copy-Item -LiteralPath $pluginSource -Destination $plugin -Force
  $message='完整性能补丁安装成功。请从 Steam 启动；首次启动可能生成加载器文件并下载 Unity 依赖，请耐心等待。'
 }
 Write-Output $message
 if(!$Silent){Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show($message,'Autolantis 性能补丁') | Out-Null}
}catch{
 if(!$Silent){Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show($_.Exception.Message,'安装未完成') | Out-Null}
 throw
}finally{
 if($tempRoot -and (Test-Path -LiteralPath $tempRoot)){
  $resolved=[IO.Path]::GetFullPath($tempRoot)
  $base=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
  if($resolved.StartsWith($base,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved).StartsWith('AutolantisSetup-')){
   Remove-Item -LiteralPath $resolved -Recurse -Force
  }
 }
}