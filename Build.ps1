param([Parameter(Mandatory=$true)][string]$GameDirectory)
$ErrorActionPreference='Stop'
$game=(Resolve-Path -LiteralPath $GameDirectory).Path
dotnet build (Join-Path $PSScriptRoot 'src/Autolantis.SkipFix.csproj') -c Release --nologo "-p:GameDirectory=$game" -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/bin/Release/net6.0/Autolantis.SkipFix.dll') -Destination (Join-Path $PSScriptRoot 'package/Autolantis.SkipFix.dll') -Force
Write-Host 'Built package/Autolantis.SkipFix.dll. Game installation was not modified.'
$hash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'package/Autolantis.SkipFix.dll')).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'package/SHA256SUMS.txt'),$hash+'  Autolantis.SkipFix.dll'+[Environment]::NewLine)