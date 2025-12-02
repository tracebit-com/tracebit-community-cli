#!/usr/bin/env bash
set -euo pipefail

# Set SIGN_PACKAGE=true to sign and notarize, otherwise creates unsigned package only
BUILD_USER_ID="MSY6YB87G8"
KEYCHAIN_PATH=$RUNNER_TEMP/app-signing.keychain-db
SIGN_PACKAGE="${SIGN_PACKAGE:-false}"
VERSION="${VERSION:-0.0.0}"

# Detect host architecture, default to current arch for local builds
HOST_ARCH="${HOST_ARCH:-$(uname -m)}"
if [ "$HOST_ARCH" = "x86_64" ]; then
    HOST_ARCH_XML="x86_64"
elif [ "$HOST_ARCH" = "arm64" ]; then
    HOST_ARCH_XML="arm64"
else
    echo "Unknown architecture: $HOST_ARCH"
    exit 1
fi

if [ "$SIGN_PACKAGE" = "true" ]; then
    echo "→ Unlocking keychain..."
    security unlock-keychain -p "$KEYCHAIN_PASSWORD" $KEYCHAIN_PATH
    echo "→ Signing binary..."
    codesign -s "$BUILD_USER_ID" --keychain "$KEYCHAIN_PATH" -f --timestamp -o runtime -i "com.tracebit.cli.community" Build/tracebit
fi

mkdir -p installer/{payload/{.local/bin,Library/LaunchAgents},scripts}
cp Build/tracebit installer/payload/.local/bin/
cp installers/macos/com.tracebit.cli.community.plist installer/payload/Library/LaunchAgents
cp installers/macos/postinstall installer/scripts

echo $VERSION

echo "→ Building component package..."
pkgbuild --root installer/payload \
         --scripts installer/scripts \
         --identifier com.tracebit.cli.community \
         --version $VERSION \
         --install-location / \
         installer/install-tracebit-component.pkg

echo "→ Building product archive with distribution definition..."
sed -e "s/__HOST_ARCH__/$HOST_ARCH_XML/g" -e "s/__VERSION__/$VERSION/g" \
    installers/macos/distribution.xml > installer/distribution.xml
cp -r installers/macos/resources installer/
productbuild --distribution installer/distribution.xml \
             --package-path installer \
             --resources installer/resources \
             install-tracebit.unsigned.pkg

echo "→ Setting package icon..."
sips -i installers/macos/resources/installer-icon.icns
DeRez -only icns installers/macos/resources/installer-icon.icns > $RUNNER_TEMP/pkg-icon.rsrc
Rez -append $RUNNER_TEMP/pkg-icon.rsrc -o install-tracebit.unsigned.pkg
SetFile -a C install-tracebit.unsigned.pkg

if [ "$SIGN_PACKAGE" = "true" ]; then
  echo "→ Signing package..."
  productsign -s "$BUILD_USER_ID" --keychain "$KEYCHAIN_PATH" --timestamp install-tracebit.unsigned.pkg install-tracebit.pkg

  echo "→ Submitting for notarization..."
  { notaryid=$(xcrun notarytool submit "install-tracebit.pkg" --keychain-profile "AppNotarize" --keychain "$KEYCHAIN_PATH" --wait | tee /dev/fd/3 | awk '/id: / { print $2 }' | head -n 1); } 3>&1

  echo "→ Fetching notarization log..."
  xcrun notarytool log $notaryid --keychain-profile "AppNotarize" --keychain $KEYCHAIN_PATH notarylog.json

  echo "✓ Created signed and notarized package: install-tracebit.pkg"
else
  echo "✓ Created unsigned package: install-tracebit.unsigned.pkg"
fi
