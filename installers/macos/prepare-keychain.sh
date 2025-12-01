#!/usr/bin/env bash
set -euo pipefail

# Requires
#APPLICATION_P12_PASSWORD
#APPLICATION_KEY_CERTIFICATE_BASE64
#INSTALLER_P12_PASSWORD
#INSTALLER_KEY_CERTIFICATE_BASE64
#NOTARIZATION_PASSWORD
#NOTARIZATION_APPLEID
#KEYCHAIN_PASSWORD
#RUNNER_TEMP

BUILD_USER_ID="MSY6YB87G8"
INTERMEDIATE_CA="DeveloperIDG2CA.cer"

curl "https://www.apple.com/certificateauthority/$INTERMEDIATE_CA" -Lo $INTERMEDIATE_CA

KEYCHAIN_PATH=$RUNNER_TEMP/app-signing.keychain-db

# create temporary keychain
security delete-keychain $KEYCHAIN_PATH || true
security create-keychain -p "$KEYCHAIN_PASSWORD" $KEYCHAIN_PATH
security set-keychain-settings -lut 21600 $KEYCHAIN_PATH
security unlock-keychain -p "$KEYCHAIN_PASSWORD" $KEYCHAIN_PATH

# import certificates to keychain
security import <(echo -n "$APPLICATION_KEY_CERTIFICATE_BASE64" | base64 --decode) -P "$APPLICATION_P12_PASSWORD" -A -f pkcs12 -k $KEYCHAIN_PATH
security import <(echo -n "$INSTALLER_KEY_CERTIFICATE_BASE64" | base64 --decode) -P "$INSTALLER_P12_PASSWORD" -A -f pkcs12 -k $KEYCHAIN_PATH \
security import $INTERMEDIATE_CA -k $KEYCHAIN_PATH || true

# set keychain as default for codesign
security list-keychains -d user -s $KEYCHAIN_PATH `security list-keychains -d user | xargs`
security set-key-partition-list -S apple-tool:,apple: -k "$KEYCHAIN_PASSWORD" $KEYCHAIN_PATH

xcrun notarytool store-credentials "AppNotarize" --keychain "$KEYCHAIN_PATH" --apple-id "$NOTARIZATION_APPLEID" --team-id "$BUILD_USER_ID" --password "$NOTARIZATION_PASSWORD"
