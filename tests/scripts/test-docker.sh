#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/../.." || exit 1

IMAGE_NAME="nanobot-net-test"

echo "=== Building Docker image ==="
docker build -t "$IMAGE_NAME" . 2>&1 || {
    echo "ERROR: Docker build failed"
    exit 1
}

echo ""
echo "=== Running 'nanobot onboard' ==="
docker run --name nanobot-test-run "$IMAGE_NAME" onboard 2>&1 || {
    echo "WARNING: onboard command may have failed (expected if config exists)"
}

echo ""
echo "=== Running 'nanobot status' ==="
STATUS_OUTPUT=$(docker commit nanobot-test-run nanobot-net-onboarded > /dev/null 2>&1 && \
    docker run --rm nanobot-net-onboarded status 2>&1) || true

echo "$STATUS_OUTPUT"

echo ""
echo "=== Validating output ==="
PASS=true

check() {
    if echo "$STATUS_OUTPUT" | grep -qi "$1"; then
        echo "  PASS: found '$1'"
    else
        echo "  FAIL: missing '$1'"
        PASS=false
    fi
}

check "NanoBot Status"
check "Config:"
check "Workspace:"
check "Model:"
check "Provider:"

echo ""
if $PASS; then
    echo "=== All checks passed ==="
else
    echo "=== Some checks FAILED ==="
    exit 1
fi

echo ""
echo "=== Cleanup ==="
docker rm -f nanobot-test-run 2>/dev/null || true
docker rmi -f nanobot-net-onboarded 2>/dev/null || true
docker rmi -f "$IMAGE_NAME" 2>/dev/null || true
echo "Done."
