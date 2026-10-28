#!/usr/bin/env bash
set -euo pipefail

certbot renew "$@" \
    --cert-name media.kunini.ru \
    --no-random-sleep-on-renew \
    --config-dir /home/dekunin/letsencrypt \
    --work-dir /home/dekunin/letsencrypt-work \
    --logs-dir /home/dekunin/letsencrypt-logs \
    --deploy-hook 'sudo -n /usr/local/sbin/codex-nginx-apply media.kunini.ru.conf' \
    --quiet
