#!/usr/bin/env bash
#
# nbot install script — one-line installer for NanoBot.Net
#
# Usage:
#   curl -sSL https://raw.githubusercontent.com/mbzcnet/NanoBot.Net/main/install/install.sh | bash
#   curl -sSL https://raw.githubusercontent.com/mbzcnet/NanoBot.Net/main/install/install.sh | bash -s -- --version 0.1.4
#   ./install.sh [--version X.Y.Z] [--dir ~/.local] [--no-verify]
#

set -euo pipefail

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
REPO="mbzcnet/NanoBot.Net"
BINARY_NAME="nbot"
DEFAULT_VERSION="0.1.4"
INSTALL_DIR="${NBOT_INSTALL_DIR:-$HOME/.local/bin}"
TEMP_DIR="${TMPDIR:-/tmp}"
NO_VERIFY="${NBOT_NO_VERIFY:-false}"
SKIP_CONFIG="${NBOT_SKIP_CONFIG:-false}"

# ---------------------------------------------------------------------------
# Colours
# ---------------------------------------------------------------------------
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

info()   { echo -e "${GREEN}[INFO]${NC}  $*"; }
warn()   { echo -e "${YELLOW}[WARN]${NC}  $*" >&2; }
error()  { echo -e "${RED}[ERROR]${NC} $*" >&2; exit 1; }
step()   { echo -e "${BLUE}==>${NC} $*" >&2; }
success(){ echo -e "${GREEN}[OK]${NC}   $*" >&2; }

# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------
parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --version)  VERSION="${2}"; shift 2 ;;
            --dir)      INSTALL_DIR="${2}"; shift 2 ;;
            --no-verify) NO_VERIFY="true"; shift ;;
            --skip-config) SKIP_CONFIG="true"; shift ;;
            -h|--help)  usage; exit 0 ;;
            *)          error "Unknown option: $1" ;;
        esac
    done
}

usage() {
    cat <<'EOF'
Usage: install.sh [options]

Options:
  --version X.Y.Z    Install a specific version (default: latest tag)
  --dir PATH         Installation directory (default: ~/.local/bin)
  --no-verify        Skip SHA256 verification
  --skip-config      Skip post-install configuration step
  -h, --help         Show this help message

Examples:
  curl -sSL https://raw.githubusercontent.com/mbzcnet/NanoBot.Net/main/install/install.sh | bash
  ./install.sh --version 0.1.4 --dir /usr/local/bin
EOF
}

# ---------------------------------------------------------------------------
# Platform detection
# ---------------------------------------------------------------------------
detect_platform() {
    local os arch

    case "$(uname -s)" in
        Linux*)     OS="linux" ;;
        Darwin*)     OS="macos" ;;
        *)           error "Unsupported OS: $(uname -s)" ;;
    esac

    case "$(uname -m)" in
        x86_64)  ARCH="x64" ;;
        aarch64|arm64) ARCH="arm64" ;;
        armv7l)  ARCH="arm" ;;
        *)       error "Unsupported architecture: $(uname -m)" ;;
    esac

    # WSL detection — treat as linux
    if [[ -f /proc/version ]] && grep -qiE "wsl|microsoft"; then
        OS="linux"
    fi

    RID="${OS}-${ARCH}"
}

# ---------------------------------------------------------------------------
# Prerequisites
# ---------------------------------------------------------------------------
check_prerequisites() {
    step "Checking prerequisites..."

    local missing=()
    command -v curl  >/dev/null 2>&1 || missing+=("curl")
    command -v tar   >/dev/null 2>&1 || missing+=("tar")
    command -v gzip  >/dev/null 2>&1 || missing+=("gzip")

    if [[ ${#missing[@]} -gt 0 ]]; then
        error "Missing required tools: ${missing[*]}. Please install them first."
    fi

    success "Prerequisites OK"
}

# ---------------------------------------------------------------------------
# Detect latest version
# ---------------------------------------------------------------------------
get_latest_version() {
    local version
    version=$(curl -sSL "https://api.github.com/repos/${REPO}/releases/latest" | \
        grep '"tag_name"' | sed 's/.*"tag_name"[[:space:]]*:[[:space:]]*"v\?\([^"]*\)".*/\1/')

    if [[ -z "$version" ]]; then
        error "Failed to detect latest version. Is the repository accessible?"
    fi

    echo "$version"
}

# ---------------------------------------------------------------------------
# Download & verify
# ---------------------------------------------------------------------------
download_artifact() {
    step "Downloading nbot ${VERSION} for ${RID}..."

    local url="https://github.com/${REPO}/releases/download/v${VERSION}/${BINARY_NAME}-${RID}.tar.gz"
    local archive="${TEMP_DIR}/${BINARY_NAME}-${RID}.tar.gz"

    rm -f "$archive"

    if ! curl -fLSs --progress-bar \
        -o "$archive" \
        -H "Accept: application/octet-stream" \
        "$url"; then
        error "Download failed: $url"
    fi

    success "Downloaded: $archive"

    if [[ "$NO_VERIFY" != "true" ]]; then
        verify_checksum "$archive"
    fi
}

verify_checksum() {
    local archive="$1"
    step "Verifying SHA256 checksum..."

    local sha_url="https://github.com/${REPO}/releases/download/v${VERSION}/${BINARY_NAME}-${RID}.tar.gz.sha256"

    local expected_sha
    expected_sha=$(curl -sSL "$sha_url" | cut -d' ' -f1 || true)

    if [[ -z "$expected_sha" ]]; then
        warn "Checksum file not found at $sha_url — skipping verification."
        return
    fi

    local actual_sha
    if command -v shasum >/dev/null 2>&1; then
        actual_sha=$(shasum -a 256 "$archive" | cut -d' ' -f1)
    else
        actual_sha=$(sha256sum "$archive" | cut -d' ' -f1)
    fi

    if [[ "$actual_sha" != "$expected_sha" ]]; then
        error "Checksum mismatch!\n  Expected: $expected_sha\n  Actual:   $actual_sha"
    fi

    success "Checksum verified"
}

# ---------------------------------------------------------------------------
# Install
# ---------------------------------------------------------------------------
install_binary() {
    step "Installing to ${INSTALL_DIR}..."

    mkdir -p "$INSTALL_DIR"

    if ! tar -xzf "${TEMP_DIR}/${BINARY_NAME}-${RID}.tar.gz" -C "$TEMP_DIR"; then
        error "Failed to extract archive."
    fi

    local extracted_dir="${TEMP_DIR}/${RID}"
    if [[ ! -d "$extracted_dir" ]]; then
        error "Expected extracted directory not found: $extracted_dir"
    fi

    # Find the binary (single-file publish may have it at root)
    local binary_path
    binary_path=$(find "$extracted_dir" -type f -name "$BINARY_NAME" -o -type f -name "${BINARY_NAME}.exe" 2>/dev/null | head -n1)

    if [[ -z "$binary_path" ]]; then
        error "Binary '$BINARY_NAME' not found in archive."
    fi

    cp "$binary_path" "${INSTALL_DIR}/${BINARY_NAME}"
    chmod +x "${INSTALL_DIR}/${BINARY_NAME}"

    # Copy webui assets if present
    if [[ -d "$extracted_dir/webui" ]]; then
        local webui_dest="${INSTALL_DIR}/webui"
        rm -rf "$webui_dest"
        cp -r "$extracted_dir/webui" "$webui_dest"
        info "WebUI assets installed to $webui_dest"
    fi

    # Cleanup
    rm -rf "$extracted_dir" "${TEMP_DIR}/${BINARY_NAME}-${RID}.tar.gz"

    success "Installed: ${INSTALL_DIR}/${BINARY_NAME}"
}

# ---------------------------------------------------------------------------
# PATH setup
# ---------------------------------------------------------------------------
setup_path() {
    local profile_file=""
    local shell_name="${SHELL:-bash}"
    shell_name="${shell_name##*/}"   # strip path

    case "$shell_name" in
        bash)  profile_file="$HOME/.bashrc" ;;
        zsh)   profile_file="$HOME/.zshrc" ;;
        fish)  profile_file="$HOME/.config/fish/config.fish" ;;
        *)     profile_file="$HOME/.profile" ;;
    esac

    # Already in PATH?
    if [[ ":$PATH:" == *":${INSTALL_DIR}:"* ]]; then
        info "Installation directory is already in PATH."
        return
    fi

    step "Adding ${INSTALL_DIR} to PATH..."

    local export_line
    case "$shell_name" in
        fish)
            export_line="fish_add_path ${INSTALL_DIR}"
            ;;
        *)
            export_line="export PATH=\"\${HOME}/.local/bin:\${PATH}\""
            ;;
    esac

    # Avoid duplicate entries
    if [[ -f "$profile_file" ]] && grep -qF "${INSTALL_DIR}" "$profile_file" 2>/dev/null; then
        info "PATH entry already exists in $profile_file"
    else
        {
            echo ""
            echo "# NanoBot.Net — nbot CLI"
            echo "$export_line"
        } >> "$profile_file"
        info "Added PATH entry to $profile_file"
        info "Restart your shell or run: source $profile_file"
    fi
}

# ---------------------------------------------------------------------------
# Post-install config
# ---------------------------------------------------------------------------
post_install() {
    step "Running post-install configuration..."

    local nbot_path="${INSTALL_DIR}/${BINARY_NAME}"

    if ! "$nbot_path" --version >/dev/null 2>&1; then
        warn "Failed to run 'nbot --version'. You may need to restart your shell."
        return
    fi

    if [[ "$SKIP_CONFIG" != "true" ]]; then
        info "Launching configuration wizard..."
        echo ""
        "$nbot_path" configure
    else
        info "Skipped — run 'nbot configure' manually to set up your API keys and preferences."
    fi
}

# ---------------------------------------------------------------------------
# Print summary
# ---------------------------------------------------------------------------
print_summary() {
    echo ""
    echo "========================================"
    echo -e "${GREEN}  NanoBot.Net ${VERSION} installed!${NC}"
    echo "========================================"
    echo ""
    echo "  Binary:  ${INSTALL_DIR}/${BINARY_NAME}"
    echo "  Platform: ${RID}"
    echo ""
    echo "  Get started:"
    echo "    nbot configure   # First-time setup"
    echo "    nbot agent       # Start the agent"
    echo "    nbot --help      # Full command list"
    echo ""
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
main() {
    echo ""
    echo "========================================"
    echo "   NanoBot.Net Installer"
    echo "========================================"
    echo ""

    parse_args "$@"

    VERSION="${VERSION:-$(get_latest_version)}"

    echo "  Version:     $VERSION"
    echo "  Repository: https://github.com/$REPO"
    echo "  Install to: $INSTALL_DIR"
    echo ""

    check_prerequisites
    detect_platform
    download_artifact
    install_binary
    setup_path
    post_install
    print_summary
}

main "$@"
