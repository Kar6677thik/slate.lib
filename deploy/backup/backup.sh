#!/bin/sh
set -eu

namespace="${SLATE_NAMESPACE:-slate}"
deployment="${SLATE_DEPLOYMENT:-slate}"
status_file="/data/state/backup-status.json"
temporary_status="${status_file}.tmp"
replicas="$(kubectl -n "$namespace" get deployment "$deployment" -o jsonpath='{.spec.replicas}')"

finish() {
  result=$?
  if [ "$result" -eq 0 ]; then state="Succeeded"; detail="Encrypted backup and integrity check completed."; else state="Failed"; detail="Backup command failed with exit code $result."; fi
  mkdir -p /data/state
  printf '{"state":"%s","completedAt":"%s","snapshot":"%s","detail":"%s"}\n' "$state" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "${snapshot:-unknown}" "$detail" > "$temporary_status"
  mv "$temporary_status" "$status_file"
  kubectl -n "$namespace" scale deployment "$deployment" --replicas="${replicas:-1}" >/dev/null || true
  exit "$result"
}
trap finish EXIT INT TERM

kubectl -n "$namespace" scale deployment "$deployment" --replicas=0 >/dev/null
kubectl -n "$namespace" wait --for=delete pod -l app.kubernetes.io/name=slate,app.kubernetes.io/component=api --timeout=180s >/dev/null

output="$(restic backup --tag slate /data/library /data/assets /data/auth /data/state --exclude /data/state/backup-status.json --json)"
snapshot="$(printf '%s\n' "$output" | sed -n 's/.*"snapshot_id":"\([^"]*\)".*/\1/p' | tail -1)"
restic forget --tag slate --keep-daily 7 --keep-weekly 4 --keep-monthly 6 --prune
restic check
