#!/usr/bin/env bash
# Tests the source generator and puts its Release build where Unity loads it from: Runtime/Generator. Unity runs it
# for Rubickanov.DevConsole.Runtime and every assembly that references it. Needs the .NET SDK.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

dotnet test "$here/Tests" --nologo -v quiet
dotnet build "$here/Generator" -c Release --nologo -v quiet
cp "$here/Generator/bin/Release/netstandard2.0/Rubickanov.DevConsole.Generator.dll" "$here/../Runtime/Generator/"
echo "Runtime/Generator/Rubickanov.DevConsole.Generator.dll updated"
