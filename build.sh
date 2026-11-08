#!/bin/bash
# malak-teszt_es-bench.exe build – Linux/macOS (es GitHub Actions), mono mcs kell hozza:
#   apt install mono-mcs   |   brew install mono
# A .NET Framework 4.8 referencia-szerelvenyei ellen fordit (a ref/ mappaba magatol letolti a NuGet-rol),
# igy a fordito csak olyan metodust valaszthat, ami Windowson is letezik (lasd v3.4.1).
set -euo pipefail
cd "$(dirname "$0")"
REFPKG="microsoft.netframework.referenceassemblies.net48"
REFVER="1.0.3"
if [ ! -f ref/mscorlib.dll ]; then
  echo "referencia-szerelvenyek letoltese ($REFPKG $REFVER)..."
  mkdir -p ref .tmp
  curl -fsSL "https://api.nuget.org/v3-flatcontainer/$REFPKG/$REFVER/$REFPKG.$REFVER.nupkg" -o .tmp/ref.zip
  unzip -q -o .tmp/ref.zip "build/.NETFramework/v4.8/mscorlib.dll" "build/.NETFramework/v4.8/System.dll" "build/.NETFramework/v4.8/System.Core.dll" -d .tmp/ref
  cp .tmp/ref/build/.NETFramework/v4.8/{mscorlib,System,System.Core}.dll ref/
  rm -rf .tmp
fi
mkdir -p out
mcs -nostdlib -noconfig -optimize+ -target:exe -out:out/malak-teszt_es-bench.exe \
  -r:ref/mscorlib.dll -r:ref/System.dll -r:ref/System.Core.dll \
  -resource:collect.ps1,collect.ps1 \
  Program.cs MB.cs GL.cs Ext.cs Properties/AssemblyInfo.cs
echo "kesz: out/malak-teszt_es-bench.exe ($(wc -c < out/malak-teszt_es-bench.exe) bajt)"
( sha256sum out/malak-teszt_es-bench.exe 2>/dev/null || shasum -a 256 out/malak-teszt_es-bench.exe ) | cut -d' ' -f1 > out/SHA256.txt
echo "SHA-256: $(cat out/SHA256.txt)"
