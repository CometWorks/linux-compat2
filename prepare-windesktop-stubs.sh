#!/usr/bin/env bash
# Stages the Windows Desktop implementation assemblies the plugin ships as the
# WinDesktopStubs asset (see LinuxCompat.xml). Nothing on the Linux code path executes
# them, but game assemblies and Pulsar patches reference WinForms types, and the runtime
# must be able to resolve those tokens. Reference assemblies cannot be loaded for
# execution, so the implementation assemblies from the runtime pack are used.
set -euo pipefail

VERSION="${WINDESKTOP_VERSION:-9.0.14}"
DEST="${1:-$(dirname "$0")/../windesktop-stubs}"
PACK="$HOME/.nuget/packages/microsoft.windowsdesktop.app.runtime.win-x64/$VERSION/runtimes/win-x64/lib/net9.0"

if [ ! -d "$PACK" ]; then
    echo "Restoring Microsoft.WindowsDesktop.App runtime pack $VERSION..."
    STUB="$(mktemp -d)"
    trap 'rm -rf "$STUB"' EXIT
    cat > "$STUB/stub.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <SelfContained>true</SelfContained>
    <RuntimeFrameworkVersion>$VERSION</RuntimeFrameworkVersion>
  </PropertyGroup>
</Project>
EOF
    dotnet restore "$STUB/stub.csproj"
fi

mkdir -p "$DEST"
for name in \
    Accessibility \
    Microsoft.Win32.Registry.AccessControl \
    Microsoft.Win32.SystemEvents \
    System.Configuration.ConfigurationManager \
    System.Design \
    System.Diagnostics.EventLog \
    System.Diagnostics.PerformanceCounter \
    System.Drawing \
    System.Drawing.Common \
    System.Drawing.Design \
    System.Private.Windows.Core \
    System.Resources.Extensions \
    System.Security.Cryptography.Xml \
    System.Security.Permissions \
    System.Threading.AccessControl \
    System.Windows.Extensions \
    System.Windows.Forms \
    System.Windows.Forms.Design \
    System.Windows.Forms.Design.Editors \
    System.Windows.Forms.Primitives
do
    cp "$PACK/$name.dll" "$DEST/"
done

echo "Staged $(ls "$DEST" | wc -l) assemblies in $DEST"
