# Windows에서 배포 묶음 만들기 (.NET 8 SDK 필요)
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist"
Remove-Item -Recurse -Force (Join-Path $out "AbyssBot") -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root "src/AbyssBot.App") -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none -o (Join-Path $out "AbyssBot")
Copy-Item (Join-Path $PSScriptRoot "사용법.txt") (Join-Path $out "AbyssBot")
Compress-Archive -Path (Join-Path $out "AbyssBot") -DestinationPath (Join-Path $out "AbyssBot.zip") -Force
