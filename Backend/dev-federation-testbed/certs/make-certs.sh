#!/usr/bin/env bash
# Генерирует self-signed TLS-сертификат для каждой ноды стенда (docs/rearch/phase-1/
# step-1.6-nginx.md, Изменение 4) и печатает SPKI sha256-отпечаток — подставь его
# в seed-peers.sql (Federation:TlsSpkiSha256 / TlsSpkiSha256 у пира).
set -euo pipefail

cd "$(dirname "$0")"

for node in node1 node2; do
    echo "== $node =="
    openssl req -x509 -newkey rsa:2048 -nodes \
        -keyout "$node.key.pem" \
        -out "$node.crt.pem" \
        -days 365 \
        -subj "/CN=federation-$node.test"

    fingerprint=$(openssl x509 -in "$node.crt.pem" -pubkey -noout \
        | openssl pkey -pubin -outform DER \
        | openssl dgst -sha256 -binary \
        | base64)

    echo "SPKI sha256 ($node): $fingerprint"
done
