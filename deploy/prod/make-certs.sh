#!/usr/bin/env bash
# Spec 019 (Q3, R-2): a local certificate authority and a server certificate for the production
# profile, made on this machine and never committed (deploy/prod/certs is in .gitignore).
# The certificate is valid for localhost (the browser) and keycloak / web (inside compose).
# A real deployment uses a publicly trusted certificate instead.
#
#   bash deploy/prod/make-certs.sh          makes them if missing
#   bash deploy/prod/make-certs.sh --force  makes new ones
set -euo pipefail
export MSYS_NO_PATHCONV=1 # Git Bash on Windows would rewrite "/CN=..." as a path

dir="$(cd "$(dirname "$0")" && pwd)/certs"
if [[ -f "$dir/tls.crt" && "${1:-}" != "--force" ]]; then
  echo "Certificates already in $dir (use --force for new ones)."
  exit 0
fi

mkdir -p "$dir"
cd "$dir"
openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 30 -subj "/CN=OrderFlow local CA" -keyout ca.key -out ca.crt 2>/dev/null
openssl req -newkey rsa:2048 -nodes -sha256 -subj "/CN=localhost" -keyout tls.key -out tls.csr 2>/dev/null
cat > ext.cnf <<'EOF'
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:localhost,DNS:keycloak,DNS:web,IP:127.0.0.1
EOF
openssl x509 -req -sha256 -days 30 -in tls.csr -CA ca.crt -CAkey ca.key -CAcreateserial -extfile ext.cnf -out tls.crt 2>/dev/null
rm -f tls.csr ext.cnf ca.srl

# Keycloak and nginx run as other users inside their containers: let them read the files (local only).
chmod 644 ca.crt tls.crt tls.key
echo "Made a local CA and a certificate for localhost, keycloak and web in $dir."
