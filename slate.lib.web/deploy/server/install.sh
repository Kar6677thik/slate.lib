#!/bin/sh
set -eu

SOURCE=/home/kar_thik/slate-ci/web-bootstrap-20261003

if [ "$(id -u)" -ne 0 ]; then
  echo "STOP: run this installer with sudo." >&2
  exit 1
fi

for required in slate-deploy-web web.yaml slate-web-ci.sudoers; do
  if [ ! -f "$SOURCE/$required" ]; then
    echo "STOP: missing bootstrap file: $required" >&2
    exit 1
  fi
done

if [ -e /usr/local/sbin/slate-deploy-web ] || \
   [ -e /usr/local/lib/slate-web/web.yaml ] || \
   [ -e /etc/sudoers.d/slate-web-ci ]; then
  echo "STOP: a Slate web deployment bootstrap target already exists." >&2
  exit 1
fi

visudo -cf "$SOURCE/slate-web-ci.sudoers"
k3s kubectl apply --dry-run=server -f "$SOURCE/web.yaml" >/dev/null
install -d -o root -g root -m 0755 /usr/local/lib/slate-web
install -o root -g root -m 0755 "$SOURCE/slate-deploy-web" /usr/local/sbin/slate-deploy-web
install -o root -g root -m 0644 "$SOURCE/web.yaml" /usr/local/lib/slate-web/web.yaml
install -o root -g root -m 0440 "$SOURCE/slate-web-ci.sudoers" /etc/sudoers.d/slate-web-ci

if ! k3s kubectl get namespace slate-web >/dev/null 2>&1; then
  k3s kubectl create namespace slate-web
fi

source_secret=$(k3s kubectl -n slate get secret slate-registry -o jsonpath='{.data.\.dockerconfigjson}')
if k3s kubectl -n slate-web get secret slate-registry >/dev/null 2>&1; then
  target_secret=$(k3s kubectl -n slate-web get secret slate-registry -o jsonpath='{.data.\.dockerconfigjson}')
  if [ "$source_secret" != "$target_secret" ]; then
    echo "STOP: slate-web/slate-registry exists with different credentials." >&2
    exit 1
  fi
  echo "The Slate web registry pull secret already matches the backend secret."
else
  printf '%s\n' \
    'apiVersion: v1' \
    'kind: Secret' \
    'metadata:' \
    '  name: slate-registry' \
    '  namespace: slate-web' \
    'type: kubernetes.io/dockerconfigjson' \
    'data:' \
    "  .dockerconfigjson: $source_secret" | k3s kubectl create -f -
fi

echo "Slate web CI deployment bootstrap installed."

