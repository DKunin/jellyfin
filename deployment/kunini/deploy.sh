#!/usr/bin/env bash
set -euo pipefail

REMOTE_HOST="${REMOTE_HOST:-dekunin@192.168.28.3}"
REMOTE_DIR="/var/apps/jellyfin"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"${SCRIPT_DIR}/build-auth-bridge.sh"
"${SCRIPT_DIR}/build-discover-plugin.sh"

ssh "${REMOTE_HOST}" "test -d /mnt/data/plex/movies && test -d /mnt/data/plex/series && test -d /mnt/data/plex/videos"
ssh "${REMOTE_HOST}" "docker run --rm -v /mnt/data:/host-data alpine:3.22 install -d -o 1000 -g 1000 -m 0750 /host-data/.jellyfin-cache"
# shellcheck disable=SC2029 # The fixed remote installation path is intentionally expanded locally.
ssh "${REMOTE_HOST}" "install -d -m 0755 '${REMOTE_DIR}/deployment' '${REMOTE_DIR}/acme' && install -d -m 0750 '${REMOTE_DIR}/config' '${REMOTE_DIR}/secrets'"

rsync -a \
    --exclude '.DS_Store' \
    --exclude 'auth-bridge/**/bin' \
    --exclude 'auth-bridge/**/obj' \
    --exclude 'discover-plugin/**/bin' \
    --exclude 'discover-plugin/**/obj' \
    --exclude 'discover-plugin/preview' \
    "${SCRIPT_DIR}/" "${REMOTE_HOST}:${REMOTE_DIR}/deployment/"

ssh "${REMOTE_HOST}" "bash -s" <<'REMOTE'
set -euo pipefail
app_dir=/var/apps/jellyfin
compose_file="${app_dir}/deployment/compose.yaml"

chmod 0755 "${app_dir}"/deployment/*.sh
chmod 0700 "${app_dir}/secrets"
"${app_dir}/deployment/setup-trailer-proxy-secret.sh"
test -s "${app_dir}/deployment/auth-bridge/build/Jellyfin.Plugin.KuniniAuth.dll"
test -s "${app_dir}/deployment/discover-plugin/build/Jellyfin.Plugin.KuniniDiscover.dll"
docker run --rm --volume "${app_dir}/config:/config" alpine:3.22 \
    install -d -o 1000 -g 1000 -m 0750 /config/plugins/KuniniAuth
docker run --rm --volume "${app_dir}/config:/config" alpine:3.22 \
    install -d -o 1000 -g 1000 -m 0750 /config/plugins/KuniniDiscover

rollback_stamp="$(date -u +%Y%m%dT%H%M%SZ)"
if docker container inspect jellyfin >/dev/null 2>&1; then
    previous_image="$(docker container inspect jellyfin --format '{{.Image}}')"
    rollback_tag="jellyfin/rollback:${rollback_stamp}"
    docker image tag "${previous_image}" "${rollback_tag}"
    printf '%s\n' "${rollback_tag}" >"${app_dir}/last-rollback-image"
fi
if docker image inspect jellyfin/kunini-trailer-proxy:local >/dev/null 2>&1; then
    trailer_rollback_tag="jellyfin/kunini-trailer-proxy:rollback-${rollback_stamp}"
    docker image tag jellyfin/kunini-trailer-proxy:local "${trailer_rollback_tag}"
    printf '%s\n' "${trailer_rollback_tag}" >"${app_dir}/last-trailer-proxy-rollback-image"
fi

docker compose -p jellyfin -f "${compose_file}" config >/dev/null
docker compose -p jellyfin -f "${compose_file}" pull jellyfin
docker compose -p jellyfin -f "${compose_file}" build --pull trailer-proxy
docker compose -p jellyfin -f "${compose_file}" up -d --force-recreate --remove-orphans

for attempt in $(seq 1 60); do
    health="$(docker container inspect jellyfin --format '{{if .State.Health}}{{.State.Health.Status}}{{end}}' 2>/dev/null || true)"
    if [[ "${health}" == "healthy" ]] && curl --fail --silent http://127.0.0.1:8096/health >/dev/null; then
        break
    fi
    if [[ "${attempt}" -eq 60 ]]; then
        docker logs --tail 100 jellyfin >&2 || true
        exit 1
    fi
    sleep 2
done

status_payload="$(curl --fail --silent http://127.0.0.1:8096/KuniniAuth/status \
    --header 'X-Kunini-Auth-User: deployment-check' \
    --header 'X-Kunini-Auth-User-Id: 902d495e-59a4-4b2d-92b3-d36a1f006c28')"
jq -e '.enabled == true and .loginUsername == "KuniniAuth" and (.jellyfinUsername | startswith("kunini-deployment-check-"))' \
    <<<"${status_payload}" >/dev/null
curl --fail --silent http://127.0.0.1:8096/KuniniDiscover/script.js | grep -F 'Discover beyond your library' >/dev/null
curl --fail --silent http://127.0.0.1:8096/KuniniDiscover/script.js | grep -F 'response.heading' >/dev/null
curl --fail --silent http://127.0.0.1:8096/KuniniDiscover/script.js | grep -F 'response.hasMore' >/dev/null
curl --fail --silent http://127.0.0.1:8096/KuniniDiscover/script.js | grep -F 'trailer.proxyUrl' >/dev/null
curl --fail --silent http://127.0.0.1:8096/KuniniDiscover/styles.css | grep -F '.kunini-discover-section' >/dev/null
docker exec jellyfin curl --fail --silent http://trailer-proxy:8080/health | grep -F 'ok' >/dev/null

staging_dir=/home/dekunin/nginx-sites
certbot_config=/home/dekunin/letsencrypt
domains=(media.kunini.ru media-app.kunini.ru)
install -d -m 0755 "${staging_dir}"
for domain in "${domains[@]}"; do
    site_name="${domain}.conf"
    if [[ -s "${certbot_config}/live/${domain}/fullchain.pem" && -s "${certbot_config}/live/${domain}/privkey.pem" ]]; then
        source_config="${app_dir}/deployment/nginx/${site_name}"
    else
        source_config="${app_dir}/deployment/nginx/${domain}.bootstrap.conf"
    fi
    install -m 0644 "${source_config}" "${staging_dir}/${site_name}"
    sudo -n /usr/local/sbin/codex-nginx-apply "${site_name}"
done

docker container inspect jellyfin --format 'container={{.State.Status}} health={{.State.Health.Status}} image={{.Image}}'
docker container inspect jellyfin-trailer-proxy --format 'trailer_proxy={{.State.Status}} health={{.State.Health.Status}} image={{.Image}}'
REMOTE
