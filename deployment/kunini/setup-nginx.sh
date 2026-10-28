#!/usr/bin/env bash
set -euo pipefail

app_dir="/var/apps/jellyfin"
domain="media.kunini.ru"
site_name="${domain}.conf"
staging_dir="/home/dekunin/nginx-sites"
staged_config="${staging_dir}/${site_name}"
bootstrap_config="${app_dir}/deployment/nginx/${domain}.bootstrap.conf"
tls_config="${app_dir}/deployment/nginx/${site_name}"
apply_helper="/usr/local/sbin/codex-nginx-apply"
certbot_config="/home/dekunin/letsencrypt"
certbot_work="/home/dekunin/letsencrypt-work"
certbot_logs="/home/dekunin/letsencrypt-logs"

curl --fail --silent http://127.0.0.1:8096/health >/dev/null
install -d -m 0755 "${staging_dir}" "${app_dir}/acme"
install -m 0644 "${bootstrap_config}" "${staged_config}"
sudo -n "${apply_helper}" "${site_name}"

challenge_dir="${app_dir}/acme/.well-known/acme-challenge"
install -d -m 0755 "${challenge_dir}"
challenge_name="jellyfin-preflight-$$"
challenge_value="jellyfin-${RANDOM}-${RANDOM}"
printf '%s' "${challenge_value}" >"${challenge_dir}/${challenge_name}"
trap 'rm -f "${challenge_dir}/${challenge_name}"' EXIT

served_value="$(curl --fail --silent --max-time 15 --resolve "${domain}:80:127.0.0.1" "http://${domain}/.well-known/acme-challenge/${challenge_name}")"
if [[ "${served_value}" != "${challenge_value}" ]]; then
    echo "The ACME challenge path is not served by Nginx." >&2
    exit 1
fi

certbot certonly \
    --webroot \
    --webroot-path "${app_dir}/acme" \
    --domain "${domain}" \
    --cert-name "${domain}" \
    --non-interactive \
    --agree-tos \
    --config-dir "${certbot_config}" \
    --work-dir "${certbot_work}" \
    --logs-dir "${certbot_logs}" \
    --deploy-hook "sudo -n ${apply_helper} ${site_name}"

install -m 0644 "${tls_config}" "${staged_config}"
if ! sudo -n "${apply_helper}" "${site_name}"; then
    install -m 0644 "${bootstrap_config}" "${staged_config}"
    sudo -n "${apply_helper}" "${site_name}"
    echo "TLS configuration failed; restored the closed bootstrap site." >&2
    exit 1
fi

"${app_dir}/deployment/renew-certificate.sh" --dry-run

cron_line="19 4 * * * ${app_dir}/deployment/renew-certificate.sh"
cron_file="$(mktemp)"
trap 'rm -f "${challenge_dir}/${challenge_name}" "${cron_file}"' EXIT
crontab -l >"${cron_file}" 2>/dev/null || true
if ! grep -Fqx "${cron_line}" "${cron_file}"; then
    printf '%s\n' "${cron_line}" >>"${cron_file}"
    crontab "${cron_file}"
fi

http_status="$(curl --silent --output /dev/null --write-out '%{http_code}' --resolve "${domain}:80:127.0.0.1" "http://${domain}/")"
login_location="$(curl --silent --head --resolve "${domain}:443:127.0.0.1" "https://${domain}/" | tr -d '\r' | awk 'tolower($1) == "location:" { print $2 }')"
if [[ "${http_status}" != "301" || "${login_location}" != https://auth.kunini.ru/login* ]]; then
    echo "Public HTTP/TLS authentication boundary failed verification." >&2
    exit 1
fi

printf 'Nginx, TLS, and Kunini Auth are ready for %s.\n' "${domain}"
