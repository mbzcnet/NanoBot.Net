#!/bin/bash
#
# Build and publish NanoBot.Net for all platforms
#
# Usage:
#   ./publish.sh <version> [options]
#
# Options:
#   --tag                Create and push git tag (triggers GitHub Actions release)
#   --update-formula     Update Homebrew Formula sha256 after release
#   --push-tap           Push updated formula to homebrew-tap repository
#   --nuget              Push package to NuGet.org
#   --all                Run all steps: build, tag, wait for release, update formula, push tap
#
# Environment Variables (for sensitive data):
#   GITHUB_TOKEN         GitHub Personal Access Token (for push-tap)
#   NUGET_API_KEY        NuGet.org API Key (for --nuget)
#   HOMEBREW_TAP_REPO    Homebrew tap repository (default: mbzcnet/homebrew-tap)
#   HOMEBREW_TAP_BRANCH  Homebrew tap branch (default: main)
#
# Examples:
#   ./publish.sh 0.1.0                              # Build only
#   ./publish.sh 0.1.0 --tag                        # Build and push tag
#   ./publish.sh 0.1.0 --update-formula             # Build and update formula (release must exist)
#   ./publish.sh 0.1.0 --push-tap                   # Build, update formula, push to tap
#   ./publish.sh 0.1.0 --nuget                      # Build and push to NuGet
#   ./publish.sh 0.1.0 --all                        # Full release workflow
#   GITHUB_TOKEN=xxx ./publish.sh 0.1.0 --push-tap  # With GitHub token
#   NUGET_API_KEY=xxx ./publish.sh 0.1.0 --nuget    # With NuGet key
#

set -e

REPO="mbzcnet/NanoBot.Net"
PROJECT="src/NanoBot.Cli/NanoBot.Cli.csproj"
OUTPUT_DIR="dist"
FORMULA_FILE="install/homebrew-nanobot/Formula/nbot.rb"
BINARY_NAME="nbot"

HOMEBREW_TAP_REPO="${HOMEBREW_TAP_REPO:-mbzcnet/homebrew-tap}"
HOMEBREW_TAP_BRANCH="${HOMEBREW_TAP_BRANCH:-main}"

PLATFORMS=(
    "osx-x64"
    "osx-arm64"
    "linux-x64"
    "linux-arm64"
    "win-x64"
    "win-arm64"
)

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

info() { echo -e "${GREEN}[INFO]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }
step() { echo -e "${BLUE}==>${NC} $1"; }

usage() {
    echo "Usage: $0 <version> [options]"
    echo ""
    echo "Options:"
    echo "  --tag              Create and push git tag"
    echo "  --update-formula   Update Homebrew Formula sha256"
    echo "  --push-tap         Push formula to homebrew-tap repository"
    echo "  --nuget            Push package to NuGet.org"
    echo "  --all              Full release workflow"
    echo ""
    echo "Environment Variables:"
    echo "  GITHUB_TOKEN         GitHub PAT for push-tap (required for --push-tap)"
    echo "  NUGET_API_KEY        NuGet API key (required for --nuget)"
    echo "  HOMEBREW_TAP_REPO    Tap repository (default: mbzcnet/homebrew-tap)"
    echo "  HOMEBREW_TAP_BRANCH  Tap branch (default: main)"
    echo ""
    echo "Examples:"
    echo "  $0 0.1.0                              # Build only"
    echo "  $0 0.1.0 --tag                        # Build and push tag"
    echo "  $0 0.1.0 --update-formula             # Update formula (release must exist)"
    echo "  $0 0.1.0 --push-tap                   # Update formula and push to tap"
    echo "  $0 0.1.0 --nuget                      # Build and push to NuGet"
    echo "  $0 0.1.0 --all                        # Full release workflow"
    exit 1
}

parse_args() {
    if [[ $# -lt 1 ]]; then
        usage
    fi
    
    VERSION="${1}"
    shift
    
    DO_TAG=false
    DO_UPDATE_FORMULA=false
    DO_PUSH_TAP=false
    DO_NUGET=false
    
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --tag)
                DO_TAG=true
                shift
                ;;
            --update-formula)
                DO_UPDATE_FORMULA=true
                shift
                ;;
            --push-tap)
                DO_UPDATE_FORMULA=true
                DO_PUSH_TAP=true
                shift
                ;;
            --nuget)
                DO_NUGET=true
                shift
                ;;
            --all)
                DO_TAG=true
                DO_UPDATE_FORMULA=true
                DO_PUSH_TAP=true
                shift
                ;;
            *)
                error "Unknown option: $1"
                ;;
        esac
    done
    
    if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$ ]]; then
        error "Invalid version format: $VERSION (expected: X.Y.Z or X.Y.Z-suffix)"
    fi
    
    info "Version: $VERSION"
}

check_prerequisites() {
    step "Checking prerequisites..."
    
    if ! command -v dotnet &> /dev/null; then
        error "dotnet not found. Please install .NET SDK."
    fi
    
    if [[ "$DO_TAG" == true ]]; then
        if ! command -v git &> /dev/null; then
            error "git not found. Required for --tag option."
        fi
        
        if ! git rev-parse --is-inside-work-tree &> /dev/null; then
            error "Not a git repository."
        fi
        
        local current_branch
        current_branch=$(git rev-parse --abbrev-ref HEAD)
        if [[ "$current_branch" != "main" && "$current_branch" != "master" ]]; then
            warn "Not on main/master branch (current: $current_branch)"
        fi
        
        local uncommitted
        uncommitted=$(git status --porcelain)
        if [[ -n "$uncommitted" ]]; then
            error "Uncommitted changes detected. Please commit or stash first."
        fi
    fi
    
    if [[ "$DO_UPDATE_FORMULA" == true ]]; then
        if ! command -v curl &> /dev/null; then
            error "curl not found. Required for --update-formula option."
        fi
        
        if ! command -v shasum &> /dev/null && ! command -v sha256sum &> /dev/null; then
            error "shasum or sha256sum not found. Required for --update-formula option."
        fi
    fi
    
    if [[ "$DO_PUSH_TAP" == true ]]; then
        if [[ -z "$GITHUB_TOKEN" ]]; then
            error "GITHUB_TOKEN is required for --push-tap. Set it as environment variable."
        fi
        info "GITHUB_TOKEN is set"
    fi
    
    if [[ "$DO_NUGET" == true ]]; then
        if [[ -z "$NUGET_API_KEY" ]]; then
            error "NUGET_API_KEY is required for --nuget. Set it as environment variable."
        fi
        info "NUGET_API_KEY is set"
    fi
    
    info "Prerequisites OK"
}

build_all_platforms() {
    step "Building NanoBot.Net v$VERSION..."
    
    rm -rf "$OUTPUT_DIR"
    mkdir -p "$OUTPUT_DIR"
    
    for PLATFORM in "${PLATFORMS[@]}"; do
        echo ""
        info "Building for $PLATFORM..."
        
        dotnet publish "$PROJECT" \
            -c Release \
            -r "$PLATFORM" \
            --self-contained true \
            -p:PublishSingleFile=true \
            -p:PublishTrimmed=true \
            -p:Version="$VERSION" \
            -o "$OUTPUT_DIR/$PLATFORM"
        
        cd "$OUTPUT_DIR"
        if [[ "$PLATFORM" == win-* ]]; then
            rm -f "$BINARY_NAME-$PLATFORM.zip"
            zip -rq "$BINARY_NAME-$PLATFORM.zip" "$PLATFORM"
            info "Created: $BINARY_NAME-$PLATFORM.zip"
        else
            rm -f "$BINARY_NAME-$PLATFORM.tar.gz"
            tar -czvf "$BINARY_NAME-$PLATFORM.tar.gz" "$PLATFORM" 2>/dev/null
            info "Created: $BINARY_NAME-$PLATFORM.tar.gz"
        fi
        cd - > /dev/null
    done
    
    echo ""
    step "Build artifacts:"
    ls -lh "$OUTPUT_DIR"/*.{tar.gz,zip} 2>/dev/null || true
}

create_and_push_tag() {
    step "Creating and pushing git tag v$VERSION..."
    
    local tag="v$VERSION"
    
    if git tag -l "$tag" | grep -q "$tag"; then
        error "Tag $tag already exists. Delete it first with: git tag -d $tag"
    fi
    
    git tag -a "$tag" -m "Release v$VERSION"
    info "Created local tag: $tag"
    
    git push origin "$tag"
    info "Pushed tag to origin: $tag"
    
    echo ""
    info "GitHub Actions will now build and create the release."
    info "Monitor at: https://github.com/$REPO/actions"
}

wait_for_release() {
    step "Waiting for GitHub release to be available..."
    
    local release_url="https://github.com/$REPO/releases/tag/v$VERSION"
    local max_attempts=60
    local attempt=0
    
    info "Release URL: $release_url"
    echo ""
    warn "This may take several minutes for GitHub Actions to complete."
    info "Press Ctrl+C to skip waiting (you can run --update-formula later)"
    echo ""
    
    while [[ $attempt -lt $max_attempts ]]; do
        if curl -sfLo /dev/null "$release_url"; then
            info "Release is now available!"
            return 0
        fi
        
        attempt=$((attempt + 1))
        echo -n "."
        sleep 10
    done
    
    echo ""
    warn "Timeout waiting for release. Please check GitHub Actions status."
    warn "You can run --update-formula manually after the release is ready."
    return 1
}

calculate_sha256() {
    local url="$1"
    local hash
    
    if command -v shasum &> /dev/null; then
        hash=$(curl -sL "$url" | shasum -a 256 | cut -d' ' -f1)
    else
        hash=$(curl -sL "$url" | sha256sum | cut -d' ' -f1)
    fi
    
    echo "$hash"
}

update_homebrew_formula() {
    step "Updating Homebrew Formula sha256..."
    
    local base_url="https://github.com/$REPO/releases/download/v$VERSION"
    local formula_path="$FORMULA_FILE"
    
    if [[ ! -f "$formula_path" ]]; then
        error "Formula file not found: $formula_path"
    fi
    
    info "Downloading artifacts and calculating sha256..."
    echo ""
    
    declare -A sha256_hashes
    
    sha256_hashes["osx-x64"]=$(calculate_sha256 "$base_url/$BINARY_NAME-osx-x64.tar.gz")
    info "  osx-x64:   ${sha256_hashes[osx-x64]}"
    
    sha256_hashes["osx-arm64"]=$(calculate_sha256 "$base_url/$BINARY_NAME-osx-arm64.tar.gz")
    info "  osx-arm64: ${sha256_hashes[osx-arm64]}"
    
    sha256_hashes["linux-x64"]=$(calculate_sha256 "$base_url/$BINARY_NAME-linux-x64.tar.gz")
    info "  linux-x64: ${sha256_hashes[linux-x64]}"
    
    sha256_hashes["linux-arm64"]=$(calculate_sha256 "$base_url/$BINARY_NAME-linux-arm64.tar.gz")
    info "  linux-arm64: ${sha256_hashes[linux-arm64]}"
    
    echo ""
    step "Updating $formula_path..."
    
    sed -i.bak "s/^  version \".*\"/  version \"$VERSION\"/" "$formula_path"
    
    sed -i.bak "s/sha256 \"TODO:.*\"/sha256 \"${sha256_hashes[osx-x64]}\"/" "$formula_path"
    
    local temp_file=$(mktemp)
    local current_section=""
    local current_arch=""
    
    while IFS= read -r line; do
        if [[ "$line" == *"on_macos do" ]]; then
            current_section="macos"
        elif [[ "$line" == *"on_linux do" ]]; then
            current_section="linux"
        elif [[ "$line" == *"on_intel do" ]]; then
            current_arch="intel"
        elif [[ "$line" == *"on_arm do" ]]; then
            current_arch="arm"
        elif [[ "$line" == *"sha256"* ]]; then
            if [[ "$current_section" == "macos" && "$current_arch" == "intel" ]]; then
                line="      sha256 \"${sha256_hashes[osx-x64]}\""
            elif [[ "$current_section" == "macos" && "$current_arch" == "arm" ]]; then
                line="      sha256 \"${sha256_hashes[osx-arm64]}\""
            elif [[ "$current_section" == "linux" && "$current_arch" == "intel" ]]; then
                line="      sha256 \"${sha256_hashes[linux-x64]}\""
            elif [[ "$current_section" == "linux" && "$current_arch" == "arm" ]]; then
                line="      sha256 \"${sha256_hashes[linux-arm64]}\""
            fi
        fi
        echo "$line" >> "$temp_file"
    done < "$formula_path"
    
    mv "$temp_file" "$formula_path"
    rm -f "${formula_path}.bak"
    
    info "Formula updated successfully!"
    
    echo ""
    step "Updated formula content:"
    cat "$formula_path"
}

push_to_homebrew_tap() {
    step "Pushing formula to homebrew-tap repository..."
    
    local tap_owner=$(echo "$HOMEBREW_TAP_REPO" | cut -d'/' -f1)
    local tap_name=$(echo "$HOMEBREW_TAP_REPO" | cut -d'/' -f2)
    local formula_content=$(cat "$FORMULA_FILE")
    local formula_filename="Formula/nbot.rb"
    
    info "Target repository: $HOMEBREW_TAP_REPO"
    info "Target branch: $HOMEBREW_TAP_BRANCH"
    info "Formula file: $formula_filename"
    
    local api_url="https://api.github.com/repos/$HOMEBREW_TAP_REPO/contents/$formula_filename"
    
    local existing_sha=""
    local existing_response=$(curl -s -w "\n%{http_code}" \
        -H "Authorization: token $GITHUB_TOKEN" \
        -H "Accept: application/vnd.github.v3+json" \
        "$api_url?ref=$HOMEBREW_TAP_BRANCH")
    
    local http_code=$(echo "$existing_response" | tail -n1)
    local response_body=$(echo "$existing_response" | sed '$d')
    
    if [[ "$http_code" == "200" ]]; then
        existing_sha=$(echo "$response_body" | grep -o '"sha"[[:space:]]*:[[:space:]]*"[^"]*"' | cut -d'"' -f4)
        info "Existing file found, will update (sha: ${existing_sha:0:8}...)"
    else
        info "File does not exist, will create new file"
    fi
    
    local encoded_content=$(echo -n "$formula_content" | base64)
    
    local commit_message="Update nbot to v$VERSION"
    
    local request_body
    if [[ -n "$existing_sha" ]]; then
        request_body=$(cat <<EOF
{
  "message": "$commit_message",
  "content": "$encoded_content",
  "sha": "$existing_sha",
  "branch": "$HOMEBREW_TAP_BRANCH"
}
EOF
)
    else
        request_body=$(cat <<EOF
{
  "message": "$commit_message",
  "content": "$encoded_content",
  "branch": "$HOMEBREW_TAP_BRANCH"
}
EOF
)
    fi
    
    local put_response=$(curl -s -w "\n%{http_code}" \
        -X PUT \
        -H "Authorization: token $GITHUB_TOKEN" \
        -H "Accept: application/vnd.github.v3+json" \
        -H "Content-Type: application/json" \
        -d "$request_body" \
        "$api_url")
    
    local put_http_code=$(echo "$put_response" | tail -n1)
    local put_response_body=$(echo "$put_response" | sed '$d')
    
    if [[ "$put_http_code" == "200" || "$put_http_code" == "201" ]]; then
        info "Successfully pushed formula to $HOMEBREW_TAP_REPO"
        info "View at: https://github.com/$HOMEBREW_TAP_REPO/blob/$HOMEBREW_TAP_BRANCH/$formula_filename"
    else
        error "Failed to push formula. HTTP $put_http_code\nResponse: $put_response_body"
    fi
}

push_to_nuget() {
    step "Pushing package to NuGet.org..."
    
    local nupkg_dir="nupkg"
    
    mkdir -p "$nupkg_dir"
    
    info "Packing NanoBot.Cli..."
    dotnet pack "$PROJECT" \
        -c Release \
        -p:PackageVersion="$VERSION" \
        -o "$nupkg_dir"
    
    local nupkg_file="$nupkg_dir/NanoBot.Cli.$VERSION.nupkg"
    
    if [[ ! -f "$nupkg_file" ]]; then
        error "Package file not found: $nupkg_file"
    fi
    
    info "Package created: $nupkg_file"
    
    info "Pushing to NuGet.org..."
    dotnet nuget push "$nupkg_file" \
        --source https://api.nuget.org/v3/index.json \
        --api-key "$NUGET_API_KEY"
    
    info "Successfully pushed to NuGet.org"
    info "View at: https://www.nuget.org/packages/NanoBot.Cli/$VERSION"
}

main() {
    echo ""
    echo "=========================================="
    echo "   NanoBot.Net Release Builder"
    echo "=========================================="
    echo ""
    
    parse_args "$@"
    check_prerequisites
    
    build_all_platforms
    
    if [[ "$DO_TAG" == true ]]; then
        echo ""
        create_and_push_tag
        
        if [[ "$DO_UPDATE_FORMULA" == true || "$DO_PUSH_TAP" == true ]]; then
            wait_for_release
        fi
    fi
    
    if [[ "$DO_UPDATE_FORMULA" == true ]]; then
        echo ""
        update_homebrew_formula
    fi
    
    if [[ "$DO_PUSH_TAP" == true ]]; then
        echo ""
        push_to_homebrew_tap
    fi
    
    if [[ "$DO_NUGET" == true ]]; then
        echo ""
        push_to_nuget
    fi
    
    echo ""
    info "Done!"
    
    echo ""
    echo "=========================================="
    echo "   Summary"
    echo "=========================================="
    echo "  Version:        v$VERSION"
    echo "  Release:        https://github.com/$REPO/releases/tag/v$VERSION"
    if [[ "$DO_PUSH_TAP" == true ]]; then
        echo "  Homebrew Tap:   https://github.com/$HOMEBREW_TAP_REPO"
    fi
    if [[ "$DO_NUGET" == true ]]; then
        echo "  NuGet Package:  https://www.nuget.org/packages/NanoBot.Cli/$VERSION"
    fi
}

main "$@"
