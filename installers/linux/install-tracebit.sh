#!/usr/bin/env bash

# install-tracebit.sh - Download and install the Tracebit CLI for the current platform
# Usage: ./install-tracebit.sh [OPTIONS]
#        curl -sSL <url>/install-tracebit.sh | bash
#        curl -sSL <url>/install-tracebit.sh | bash -s -- [OPTIONS]

set -euo pipefail
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly PURPLE='\033[1;35m'
readonly GRAY='\033[1;90m'
readonly RESET='\033[0m'

readonly GITHUB_REPO="tracebit-com/tracebit-community-cli"
readonly BINARY_NAME="tracebit"

INSTALL_PATH="${INSTALL_PATH:-$HOME/.local/bin}"
SHOW_HELP=false
VERBOSE=false
DRY_RUN=false
VERSION=""
LOCAL_BINARY=""
UNINSTALL=false

show_help() {
    cat << 'EOF'
Tracebit CLI Installer

DESCRIPTION:
    Downloads and installs the Tracebit CLI for the current platform.

USAGE:
    ./install-tracebit.sh [OPTIONS]

OPTIONS:
    -i, --install-path PATH     Directory to install the CLI (default: $HOME/.local/bin)
    -v, --version VERSION       Version to install (default: latest)
    -b, --binary PATH           Install from a local binary instead of downloading
    -u, --uninstall             Uninstall Tracebit CLI
    --verbose                   Enable verbose output
    --dry-run                   Show what would be done without making changes
    -h, --help                  Show this help message

EXAMPLES:
    ./install-tracebit.sh
    ./install-tracebit.sh --install-path /usr/local/bin
    ./install-tracebit.sh --version v1.0.0
    ./install-tracebit.sh --binary ./tracebit
    ./install-tracebit.sh --uninstall

EOF
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case $1 in
            -i|--install-path)
                if [[ $# -lt 2 || -z "$2" ]]; then
                    say_error "Option '$1' requires a non-empty value"
                    say_info "Use --help for usage information."
                    exit 1
                fi
                INSTALL_PATH="$2"
                shift 2
                ;;
            -v|--version)
                if [[ $# -lt 2 || -z "$2" ]]; then
                    say_error "Option '$1' requires a non-empty value"
                    say_info "Use --help for usage information."
                    exit 1
                fi
                VERSION="$2"
                shift 2
                ;;
            -b|--binary)
                if [[ $# -lt 2 || -z "$2" ]]; then
                    say_error "Option '$1' requires a non-empty value"
                    say_info "Use --help for usage information."
                    exit 1
                fi
                LOCAL_BINARY="$2"
                shift 2
                ;;
            -u|--uninstall)
                UNINSTALL=true
                shift
                ;;
            --dry-run)
                DRY_RUN=true
                shift
                ;;
            --verbose)
                VERBOSE=true
                shift
                ;;
            -h|--help)
                SHOW_HELP=true
                shift
                ;;
            *)
                say_error "Unknown option '$1'"
                say_info "Use --help for usage information."
                exit 1
                ;;
        esac
    done
}

say_verbose() {
    if [[ "$VERBOSE" == true ]]; then
        echo -e "${GRAY}$1${RESET}" >&2
    fi
}

say_error() {
    echo -e "${RED}Error: $1${RESET}" >&2
}

say_warn() {
    echo -e "${YELLOW}Warning: $1${RESET}" >&2
}

say_info() {
    echo -e "$1" >&2
}

say_success() {
    echo -e "${GREEN}$1${RESET}" >&2
}

detect_platform() {
    local os arch

    os=$(uname -s | tr '[:upper:]' '[:lower:]')
    arch=$(uname -m)

    if [[ "$os" != "linux" ]]; then
        say_error "This installer only supports Linux. OS detected: $os"
        exit 1
    fi

    case "$arch" in
        x86_64|amd64)
            echo "linux-x64"
            ;;
        arm64|aarch64)
            echo "linux-arm"
            ;;
        *)
            say_error "Unsupported architecture: $arch"
            exit 1
            ;;
    esac
}

get_download_url() {
    local version="$1"
    local platform="$2"
    local api_url release_url

    if [[ "$version" == "latest" ]]; then
        api_url="https://api.github.com/repos/$GITHUB_REPO/releases/latest"
        say_verbose "Fetching latest release info from GitHub API"

        if ! release_url=$(curl -fsSL "$api_url" | grep -o "\"browser_download_url\": \"[^\"]*$BINARY_NAME-$platform\"" | cut -d'"' -f4 | head -n1); then
            say_error "Failed to fetch release information"
            exit 1
        fi
    else
        say_verbose "Using specific version: $version"
        release_url="https://github.com/$GITHUB_REPO/releases/download/$version/$BINARY_NAME-$platform"
    fi

    if [[ -z "$release_url" ]]; then
        say_error "Could not find release for platform: $platform"
        exit 1
    fi

    echo "$release_url"
}

install_local_binary() {
    local source_path="$1"

    if [[ ! -f "$source_path" ]]; then
        say_error "Binary not found: $source_path"
        exit 1
    fi

    if [[ "$DRY_RUN" == true ]]; then
        say_info "Would install from: $source_path"
        say_info "Would install to: $INSTALL_PATH/$BINARY_NAME"
        return
    fi

    say_info "Installing from local binary: $source_path"

    say_verbose "Creating directory $INSTALL_PATH"
    mkdir -p "$INSTALL_PATH"
    say_verbose "Copying binary to $INSTALL_PATH/$BINARY_NAME"
    cp "$source_path" "$INSTALL_PATH/$BINARY_NAME"
    chmod +x "$INSTALL_PATH/$BINARY_NAME"

    say_info "Installed $BINARY_NAME to $INSTALL_PATH/$BINARY_NAME"
}

download_and_install() {
    local platform download_url temp_file

    platform=$(detect_platform)
    say_verbose "Detected platform: $platform"

    download_url=$(get_download_url "$VERSION" "$platform")
    say_info "Downloading $BINARY_NAME from $download_url"

    if [[ "$DRY_RUN" == true ]]; then
        say_info "Would download from: $download_url"
        say_info "Would install to: $INSTALL_PATH/$BINARY_NAME"
        return
    fi

    temp_file=$(mktemp)
    trap 'rm -f "$temp_file"' EXIT

    if ! curl -fsSL -o "$temp_file" "$download_url"; then
        say_error "Failed to download binary"
        exit 1
    fi

    say_verbose "Download complete"
    say_verbose "Creating directory $INSTALL_PATH"
    mkdir -p "$INSTALL_PATH"
    say_verbose "Setting executable permissions"
    chmod +x "$temp_file"
    say_verbose "Moving binary to $INSTALL_PATH/$BINARY_NAME"
    mv "$temp_file" "$INSTALL_PATH/$BINARY_NAME"

    # Clear the trap since we've successfully moved the file
    trap - EXIT

    say_info "Installed $BINARY_NAME to $INSTALL_PATH/$BINARY_NAME"
}

install_systemd_units() {
    local install_path="$1"
    local systemd_user_dir="$2"

    if [[ "$DRY_RUN" == true ]]; then
        say_info "Would install systemd units to $systemd_user_dir"
        return
    fi

    say_info "Installing systemd units to $systemd_user_dir"

    say_verbose "Creating directory $systemd_user_dir"
    mkdir -p "$systemd_user_dir"
    say_verbose "Writing tracebit.service"
    cat > "$systemd_user_dir/tracebit.service" <<EOF
[Unit]
Description=Tracebit credentials refresh

[Service]
Type=oneshot
ExecStart=$install_path/tracebit refresh
TimeoutSec=60
EOF

    say_verbose "Writing tracebit.timer"
    cat > "$systemd_user_dir/tracebit.timer" <<EOF
[Unit]
Description=Tracebit credentials refresh timer

[Timer]
OnCalendar=hourly
RandomizedDelaySec=60
Persistent=true

[Install]
WantedBy=timers.target
EOF

    # Enable and start the timer
    if command -v systemctl &> /dev/null; then
        say_verbose "Reloading systemd daemon"
        systemctl --user daemon-reload 2>/dev/null || true
        say_verbose "Enabling and starting tracebit.timer"
        if systemctl --user enable --now tracebit.timer 2>/dev/null; then
            say_verbose "Timer enabled successfully"
        else
            say_warn "Failed to enable systemd timer - you may need to enable it manually"
            say_info "To enable manually, run: systemctl --user enable --now tracebit.timer"
        fi
    else
        say_warn "systemctl not found - timer created but not enabled"
        say_info "To enable manually, run: systemctl --user enable --now tracebit.timer"
        say_info "Alternatively, add this to your crontab (crontab -e):"
        say_info "  0 * * * * $install_path/tracebit refresh"
    fi
}

verify_path() {
    if [[ ":$PATH:" == *":$INSTALL_PATH:"* ]]; then
        say_verbose "Found install path in \$PATH"
    else
        say_warn 'Install path is not in your $PATH'
        say_info "To make the tracebit command available in your shell, add the following to your shell profile (e.g. ~/.bashrc):"
        say_info "  export PATH=\"\$PATH:$INSTALL_PATH\""
    fi
}

uninstall() {
    local install_path="$1"
    local systemd_user_dir="$2"
    local config_dir="$3"
    local binary_path="$install_path/$BINARY_NAME"
    local found_anything=false

    say_info "Uninstalling Tracebit CLI from $install_path"

    if [[ "$DRY_RUN" == true ]]; then
        say_info "Would remove binary: $binary_path"
        say_info "Would remove systemd units from: $systemd_user_dir"
        say_info "Would remove config directory: $config_dir"
        return
    fi

    # Stop and disable systemd timer
    if command -v systemctl &> /dev/null; then
        if systemctl --user is-enabled tracebit.timer &> /dev/null; then
            say_verbose "Stopping and disabling systemd timer"
            systemctl --user disable --now tracebit.timer 2>/dev/null || true
            found_anything=true
        fi
    fi

    # Remove systemd units
    if [[ -f "$systemd_user_dir/tracebit.service" ]]; then
        say_verbose "Removing $systemd_user_dir/tracebit.service"
        rm -f "$systemd_user_dir/tracebit.service"
        found_anything=true
    fi

    if [[ -f "$systemd_user_dir/tracebit.timer" ]]; then
        say_verbose "Removing $systemd_user_dir/tracebit.timer"
        rm -f "$systemd_user_dir/tracebit.timer"
        found_anything=true
    fi

    # Reload systemd if we removed units
    if command -v systemctl &> /dev/null && [[ "$found_anything" == true ]]; then
        say_verbose "Reloading systemd daemon"
        systemctl --user daemon-reload 2>/dev/null || true
    fi

    # Remove binary
    if [[ -f "$binary_path" ]]; then
        say_verbose "Removing $binary_path"
        rm -f "$binary_path"
        found_anything=true
    fi

    # Remove config directory
    if [[ -d "$config_dir" ]]; then
        say_verbose "Removing config directory $config_dir"
        rm -rf "$config_dir"
        found_anything=true
    fi

    if [[ "$found_anything" == true ]]; then
        say_success "Uninstallation complete!"
    else
        say_warn "No Tracebit CLI installation found"
    fi
}

main() {
    parse_args "$@"

    if [[ "$SHOW_HELP" == true ]]; then
        show_help
        exit 0
    fi

    if [[ "$UNINSTALL" == true ]]; then
        if [[ -n "$LOCAL_BINARY" || "$VERSION" != "" ]]; then
            say_error "Cannot specify --binary or --version with --uninstall"
            exit 1
        fi
        uninstall "$INSTALL_PATH" "$HOME/.config/systemd/user" "$HOME/.local/share/tracebit"
        exit 0
    fi

    if [[ -n "$LOCAL_BINARY" && "$VERSION" != "" ]]; then
        say_error "Cannot specify both --binary and --version"
        exit 1
    fi

    : ${VERSION:=latest}
    if [[ -n "$LOCAL_BINARY" ]]; then
        say_info "Installing Tracebit CLI from local binary to $INSTALL_PATH"
        install_local_binary "$LOCAL_BINARY"
    else
        say_info "Installing Tracebit CLI ($VERSION) to $INSTALL_PATH"
        download_and_install
    fi

    install_systemd_units "$INSTALL_PATH" "$HOME/.config/systemd/user"
    verify_path

    say_success "Installation complete!"
    say_info "Run ${PURPLE}tracebit auth${RESET} to get started"
}

main "$@"
