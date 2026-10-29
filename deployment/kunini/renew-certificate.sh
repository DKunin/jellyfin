#!/usr/bin/env bash
set -euo pipefail

renew_domain() {
    local domain="$1"
    shift

    certbot renew "$@" \
        --cert-name "${domain}" \
        --no-random-sleep-on-renew \
        --config-dir /home/dekunin/letsencrypt \
        --work-dir /home/dekunin/letsencrypt-work \
        --logs-dir /home/dekunin/letsencrypt-logs \
        --deploy-hook "sudo -n /usr/local/sbin/codex-nginx-apply ${domain}.conf" \
        --quiet
}

renew_domain media.kunini.ru "$@"
renew_domain media-app.kunini.ru "$@"
