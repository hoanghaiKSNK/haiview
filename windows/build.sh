#!/bin/zsh
# Build HaiView for Windows (x64 and ARM64) as single self-contained .exe files and zip them into ../dist.
# Works on macOS, Linux or Windows with the .NET 10 SDK.
set -euo pipefail
cd "$(dirname "$0")"
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' HaiView/HaiView.csproj)
mkdir -p ../dist
for RID in win-x64 win-arm64; do
  OUT="build/$RID"
  rm -rf "$OUT"
  dotnet publish HaiView/HaiView.csproj -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true -o "$OUT" | tail -1
  ARCH=${RID#win-}
  ZIP="../dist/HaiView-${VERSION}-Windows-${ARCH}.zip"
  rm -f "$ZIP"
  (cd "$OUT" && zip -q -9 "../../$ZIP" HaiView.exe)
  echo "Packaged: $ZIP"
done
