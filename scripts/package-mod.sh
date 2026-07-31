#!/usr/bin/env bash
# Build + stage + zip the mod into a drop-in RimWorld mod folder.
# Output: dist/RimWorldOtelExporter-v<ver>.zip  (+ .sha256)
# The archive root is the mod folder, so it extracts straight into RimWorld's Mods/.
set -euo pipefail
cd "$(dirname "$0")/.."   # repo root

MOD=RimWorldOtelExporter
VER="$(sed -n 's:.*<modVersion>\(.*\)</modVersion>.*:\1:p' About/About.xml | head -1)"
: "${VER:?could not read <modVersion> from About/About.xml}"
echo "Packaging $MOD v$VER"

dotnet build "Source/$MOD" -c Release -v q -nologo >/dev/null

STAGE="dist/staging/$MOD"
ZIP="dist/$MOD-v$VER.zip"
rm -rf dist/staging "$ZIP" "$ZIP.sha256"
mkdir -p "$STAGE/About" "$STAGE/Assemblies"

cp About/About.xml "$STAGE/About/"
[ -f About/Preview.png ] && cp About/Preview.png "$STAGE/About/" || true

# Runtime DLLs the mod needs (main + Protobuf + Mono polyfills), plus pdb for readable stack traces.
for f in \
  RimWorldOtelExporter.dll \
  Google.Protobuf.dll \
  System.Memory.dll \
  System.Buffers.dll \
  System.Runtime.CompilerServices.Unsafe.dll \
  RimWorldOtelExporter.pdb
do
  cp "Assemblies/$f" "$STAGE/Assemblies/"
done

cp CHANGELOG.md "$STAGE/" 2>/dev/null || true
[ -f LICENSE ] && cp LICENSE "$STAGE/" || true

( cd dist/staging && zip -rq "../$(basename "$ZIP")" "$MOD" )
( cd dist && shasum -a 256 "$(basename "$ZIP")" > "$(basename "$ZIP").sha256" )

echo "Created $ZIP"
ls -la "$ZIP"
cat "$ZIP.sha256"
