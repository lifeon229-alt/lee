#!/usr/bin/env bash
# Windows용 배포 묶음(AbyssBot.zip) 만들기: .NET 설치 없이 실행되는 단일 exe + 설정 + 사진 + 사용법
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-$root/dist}"
rm -rf "$out/AbyssBot" "$out/AbyssBot.zip"
dotnet publish "$root/src/AbyssBot.App" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o "$out/AbyssBot"
cp "$root/build/사용법.txt" "$out/AbyssBot/"
(cd "$out" && python3 -c "import shutil; shutil.make_archive('AbyssBot', 'zip', '.', 'AbyssBot')")
ls -la "$out/AbyssBot.zip"
