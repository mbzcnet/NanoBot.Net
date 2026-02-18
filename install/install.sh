#!/bin/bash
#
# NanoBot.Net Installation Script
# Supports: macOS, Linux, WSL
#
# Usage:
#   curl -fsSL https://get.nbot.ai | bash
#   or
#   curl -fsSL https://raw.githubusercontent.com/NanoBot/NanoBot.Net/main/scripts/install.sh | bash
#

set -e

REPO="NanoBot/NanoBot.Net"
BINARY_NAME="nbot"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/bin}"
VERSION="${VERSION:-latest}"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1"
    exit 1
}

detect_platform() {
    OS="$(uname -s)"
    ARCH="$(uname -m)"

    case "$OS" in
        Darwin*)
            OS="osx"
            ;;
        Linux*)
            OS="linux"
            ;;
        MINGW*|MSYS*|CYGWIN*)
            error "Please use install.ps1 for Windows. For WSL, run this script in WSL environment."
            ;;
        *)
            error "Unsupported OS: $OS"
            ;;
    esac

    case "$ARCH" in
        x86_64|amd64)
            ARCH="x64"
            ;;
        aarch64|arm64)
            ARCH="arm64"
            ;;
        *)
            error "Unsupported architecture: $ARCH"
            ;;
    esac

    PLATFORM="${OS}-${ARCH}"
    info "Detected platform: $PLATFORM"
}

get_latest_version() {
    if [ "$VERSION" = "latest" ]; then
        VERSION=$(curl -s "https://api.github.com/repos/$REPO/releases/latest" | grep '"tag_name":' | sed -E 's/.*"v([^"]+)".*/\1/')
        if [ -z "$VERSION" ]; then
            error "Failed to get latest version"
        fi
    fi
    info "Installing version: $VERSION"
}

install_nbot() {
    DOWNLOAD_URL="https://github.com/$REPO/releases/download/v$VERSION/${BINARY_NAME}-${PLATFORM}.tar.gz"
    TEMP_DIR=$(mktemp -d)
    ARCHIVE_FILE="$TEMP_DIR/${BINARY_NAME}.tar.gz"

    info "Downloading from $DOWNLOAD_URL..."

    if ! curl -fsSL "$DOWNLOAD_URL" -o "$ARCHIVE_FILE"; then
        error "Failed to download nbot"
    fi

    info "Extracting..."
    tar -xzf "$ARCHIVE_FILE" -C "$TEMP_DIR"

    mkdir -p "$INSTALL_DIR"

    mv "$TEMP_DIR/$BINARY_NAME" "$INSTALL_DIR/$BINARY_NAME"
    chmod +x "$INSTALL_DIR/$BINARY_NAME"

    rm -rf "$TEMP_DIR"

    info "Installed to $INSTALL_DIR/$BINARY_NAME"
}

add_to_path() {
    if ! echo "$PATH" | grep -q "$INSTALL_DIR"; then
        warn "$INSTALL_DIR is not in PATH"
        echo ""
        echo "Add the following to your shell profile (~/.bashrc, ~/.zshrc, etc.):"
        echo ""
        echo "    export PATH=\"\$PATH:$INSTALL_DIR\""
        echo ""
        echo "Then run: source ~/.bashrc  (or ~/.zshrc)"
    fi
}

verify_installation() {
    if [ -x "$INSTALL_DIR/$BINARY_NAME" ]; then
        info "Installation successful!"
        "$INSTALL_DIR/$BINARY_NAME" --version
    else
        error "Installation failed"
    fi
}

main() {
    echo ""
    echo "=========================================="
    echo "     NanoBot.Net Installer"
    echo "=========================================="
    echo ""

    detect_platform
    get_latest_version
    install_nbot
    add_to_path
    verify_installation

    echo ""
    echo "Run 'nbot onboard' to get started!"
}

main "$@"
