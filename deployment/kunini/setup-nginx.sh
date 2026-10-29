#!/usr/bin/env bash
set -euo pipefail

app_dir="/var/apps/jellyfin"
domains=("media.kunini.ru" "media-app.kunini.ru")
staging_dir="/home/dekunin/nginx-sites"
apply_helper="/usr/local/sbin/codex-nginx-apply"
certbot_config="/home/dekunin/letsencrypt"
certbot_work="/home/dekunin/letsencrypt-work"
certbot_logs="/home/dekunin/letsencrypt-logs"
temporary_dir="$(mktemp -d)"
challenge_dir="${app_dir}/acme/.well-known/acme-challenge"
challenge_files=()

cleanup() {
    local challenge_file
    for challenge_file in "${challenge_files[@]}"; do
        rm -f "${challenge_file}"
    done
    rm -rf "${temporary_dir}"
}
trap cleanup EXIT

restore_site() {
    local site_name="$1"
    local staged_config="$2"
    local backup_config="$3"
    local bootstrap_config="$4"

    if [[ -s "${backup_config}" ]]; then
        install -m 0644 "${backup_config}" "${staged_config}"
    else
        install -m 0644 "${bootstrap_config}" "${staged_config}"
    fi
    sudo -n "${apply_helper}" "${site_name}"
}

curl --fail --silent http://127.0.0.1:8096/health >/dev/null
install -d -m 0755 "${staging_dir}" "${app_dir}/acme" "${challenge_dir}"

for domain in "${domains[@]}"; do
    site_name="${domain}.conf"
    staged_config="${staging_dir}/${site_name}"
    bootstrap_config="${app_dir}/deployment/nginx/${domain}.bootstrap.conf"
    tls_config="${app_dir}/deployment/nginx/${site_name}"
    backup_config="${temporary_dir}/${site_name}"
    certificate_dir="${certbot_config}/live/${domain}"

    if [[ -s "${staged_config}" ]]; then
        install -m 0600 "${staged_config}" "${backup_config}"
    fi

    if [[ -s "${certificate_dir}/fullchain.pem" && -s "${certificate_dir}/privkey.pem" ]]; then
        initial_config="${tls_config}"
    else
        initial_config="${bootstrap_config}"
    fi
    install -m 0644 "${initial_config}" "${staged_config}"
    if ! sudo -n "${apply_helper}" "${site_name}"; then
        restore_site "${site_name}" "${staged_config}" "${backup_config}" "${bootstrap_config}"
        echo "Initial Nginx configuration failed for ${domain}; restored the previous site." >&2
        exit 1
    fi

    challenge_name="jellyfin-preflight-${domain//./-}-$$"
    challenge_file="${challenge_dir}/${challenge_name}"
    challenge_files+=("${challenge_file}")
    challenge_value="jellyfin-${RANDOM}-${RANDOM}"
    printf '%s' "${challenge_value}" >"${challenge_file}"

    if ! served_value="$(curl --fail --silent --max-time 15 --resolve "${domain}:80:127.0.0.1" \
        "http://${domain}/.well-known/acme-challenge/${challenge_name}")" \
        || [[ "${served_value}" != "${challenge_value}" ]]; then
        restore_site "${site_name}" "${staged_config}" "${backup_config}" "${bootstrap_config}"
        echo "The ACME challenge path is not served by Nginx for ${domain}." >&2
        exit 1
    fi

    if ! certbot certonly \
        --webroot \
        --webroot-path "${app_dir}/acme" \
        --domain "${domain}" \
        --cert-name "${domain}" \
        --non-interactive \
        --agree-tos \
        --config-dir "${certbot_config}" \
        --work-dir "${certbot_work}" \
        --logs-dir "${certbot_logs}" \
        --deploy-hook "sudo -n ${apply_helper} ${site_name}"; then
        restore_site "${site_name}" "${staged_config}" "${backup_config}" "${bootstrap_config}"
        echo "Certificate issuance failed for ${domain}; restored the previous site." >&2
        exit 1
    fi

    install -m 0644 "${tls_config}" "${staged_config}"
    if ! sudo -n "${apply_helper}" "${site_name}"; then
        restore_site "${site_name}" "${staged_config}" "${backup_config}" "${bootstrap_config}"
        echo "TLS configuration failed for ${domain}; restored the previous site." >&2
        exit 1
    fi

    rm -f "${challenge_file}"
done

"${app_dir}/deployment/renew-certificate.sh" --dry-run

cron_line="19 4 * * * ${app_dir}/deployment/renew-certificate.sh"
cron_file="${temporary_dir}/crontab"
crontab -l >"${cron_file}" 2>/dev/null || true
if ! grep -Fqx "${cron_line}" "${cron_file}"; then
    printf '%s\n' "${cron_line}" >>"${cron_file}"
    crontab "${cron_file}"
fi

browser_domain="media.kunini.ru"
app_domain="media-app.kunini.ru"
browser_http_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
    --resolve "${browser_domain}:80:127.0.0.1" "http://${browser_domain}/")"
browser_login_location="$(curl --silent --head --resolve "${browser_domain}:443:127.0.0.1" \
    "https://${browser_domain}/" | tr -d '\r' | awk 'tolower($1) == "location:" { print $2 }')"
app_http_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
    --resolve "${app_domain}:80:127.0.0.1" "http://${app_domain}/")"
app_public_info="$(curl --fail --silent --resolve "${app_domain}:443:127.0.0.1" \
    "https://${app_domain}/System/Info/Public")"

if [[ "${browser_http_status}" != "301" || "${browser_login_location}" != https://auth.kunini.ru/login* ]]; then
    echo "Browser HTTP/TLS authentication boundary failed verification." >&2
    exit 1
fi
if [[ "${app_http_status}" != "301" ]] \
    || ! jq -e '.ProductName == "Jellyfin Server" and .StartupWizardCompleted == true' <<<"${app_public_info}" >/dev/null; then
    echo "Direct Jellyfin application endpoint failed verification." >&2
    exit 1
fi

printf 'Nginx and TLS are ready: %s uses Kunini Auth; %s uses Jellyfin login.\n' \
    "${browser_domain}" "${app_domain}"
