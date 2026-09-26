#!/bin/sh
set -eu

target="${1:-}"
snapshot="${2:-latest}"
if [ -z "$target" ]; then echo "Usage: restore-slate.sh /srv/slate/data [snapshot]" >&2; exit 2; fi
case "$target" in /|/data|/srv) echo "Refusing unsafe restore target: $target" >&2; exit 2;; esac
if [ -e "$target" ] && [ "$(find "$target" -mindepth 1 -maxdepth 1 -print -quit)" ]; then echo "Restore target must be empty: $target" >&2; exit 2; fi
: "${RESTIC_REPOSITORY:?Set RESTIC_REPOSITORY}"
if [ -z "${RESTIC_PASSWORD:-}" ] && [ -z "${RESTIC_PASSWORD_FILE:-}" ]; then echo "Set RESTIC_PASSWORD or RESTIC_PASSWORD_FILE" >&2; exit 2; fi

stage="$(mktemp -d)"
cleanup() { rm -rf "$stage"; }
trap cleanup EXIT INT TERM
restic restore "$snapshot" --target "$stage"
restored="$stage/data"
test -f "$restored/library/.slate/library.json"
test -d "$restored/assets/objects"
test -d "$restored/assets/metadata"
test -f "$restored/auth/devices.json"
mkdir -p "$target"
cp -a "$restored/." "$target/"
git -C "$target/library" fsck --no-dangling
echo "Restored Slate durable data to $target. Derived indexes are intentionally absent and rebuild on API startup."
