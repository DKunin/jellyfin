#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:8096"
browser_domain="media.kunini.ru"
app_domain="media-app.kunini.ru"
password_file="/var/apps/jellyfin/secrets/admin-password"
expected_digest="sha256:fea863d0cd9ec445985d9c75fc6ed64e38a1d2766cf0c250a33d553f4fc1b771"
auth_header='Authorization: MediaBrowser Client="Kunini Verification", Device="Server", DeviceId="kunini-verification", Version="1.0.0"'

curl --fail --silent "${base_url}/health" >/dev/null
container_health="$(docker container inspect jellyfin --format '{{.State.Health.Status}}')"
container_digest="$(docker image inspect "$(docker container inspect jellyfin --format '{{.Image}}')" --format '{{index .RepoDigests 0}}' | sed 's/^[^@]*@//')"
published_port="$(docker port jellyfin 8096/tcp)"
media_mode="$(docker container inspect jellyfin --format '{{range .Mounts}}{{if eq .Destination "/media"}}{{.Mode}}{{end}}{{end}}')"
bridge_mode="$(docker container inspect jellyfin --format '{{range .Mounts}}{{if eq .Destination "/config/plugins/KuniniAuth/Jellyfin.Plugin.KuniniAuth.dll"}}{{.Mode}}{{end}}{{end}}')"
discover_mode="$(docker container inspect jellyfin --format '{{range .Mounts}}{{if eq .Destination "/config/plugins/KuniniDiscover/Jellyfin.Plugin.KuniniDiscover.dll"}}{{.Mode}}{{end}}{{end}}')"
configured_dns="$(docker container inspect jellyfin --format '{{json .HostConfig.Dns}}')"
container_environment="$(docker container inspect jellyfin --format '{{range .Config.Env}}{{println .}}{{end}}')"

if [[ "${container_health}" != "healthy" || "${container_digest}" != "${expected_digest}" ]]; then
    echo "Container health or image digest mismatch." >&2
    exit 1
fi
if [[ "${published_port}" != "127.0.0.1:8096" || "${media_mode}" != "ro" \
    || "${bridge_mode}" != "ro" || "${discover_mode}" != "ro" ]]; then
    echo "Listener or read-only mount mismatch." >&2
    exit 1
fi
if [[ "${configured_dns}" != '["9.9.9.9","149.112.112.112"]' ]]; then
    echo "Container DNS configuration mismatch." >&2
    exit 1
fi
if ! grep -Fqx 'KUNINI_AUTH_DIRECT_ACCESS_ADDRESSES=172.30.0.1' <<<"${container_environment}" \
    || ! grep -Fqx "KUNINI_AUTH_DIRECT_ACCESS_HOSTS=${app_domain}" <<<"${container_environment}"; then
    echo "Direct Jellyfin access trust boundary mismatch." >&2
    exit 1
fi

tmdb_api_ip="$(docker exec jellyfin getent ahostsv4 api.themoviedb.org | awk 'NR == 1 { print $1 }')"
tmdb_image_ip="$(docker exec jellyfin getent ahostsv4 image.tmdb.org | awk 'NR == 1 { print $1 }')"
if [[ -z "${tmdb_api_ip}" || -z "${tmdb_image_ip}" || "${tmdb_api_ip}" == 127.* || "${tmdb_image_ip}" == 127.* ]]; then
    echo "TMDb DNS resolution is missing or points to loopback." >&2
    exit 1
fi
tmdb_api_status="$(docker exec jellyfin curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
    --connect-timeout 10 --max-time 20 --retry 2 --retry-all-errors --retry-delay 1 \
    --resolve "api.themoviedb.org:443:${tmdb_api_ip}" \
    https://api.themoviedb.org/3/configuration)"
tmdb_image_status="$(docker exec jellyfin curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
    --connect-timeout 10 --max-time 20 --retry 2 --retry-all-errors --retry-delay 1 \
    --resolve "image.tmdb.org:443:${tmdb_image_ip}" \
    https://image.tmdb.org/)"
if [[ ! "${tmdb_api_status}" =~ ^[1-5][0-9]{2}$ || ! "${tmdb_image_status}" =~ ^[1-5][0-9]{2}$ ]]; then
    echo "TMDb API or image endpoint is not reachable over HTTPS." >&2
    exit 1
fi

bridge_status="$(curl --fail --silent "${base_url}/KuniniAuth/status" \
    --header 'X-Kunini-Auth-User: verification-check' \
    --header 'X-Kunini-Auth-User-Id: 39ff34d1-e848-43ed-b515-4aedf46ef68c')"
jq -e '.enabled == true and .loginUsername == "KuniniAuth" and (.jellyfinUsername | startswith("kunini-verification-check-"))' \
    <<<"${bridge_status}" >/dev/null
missing_header_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "${base_url}/KuniniAuth/status")"
if [[ "${missing_header_status}" != "401" ]]; then
    echo "Kunini bridge did not fail closed without trusted headers." >&2
    exit 1
fi

admin_password="$(<"${password_file}")"
auth_result="$(jq -n --arg username dkunin --arg password "${admin_password}" '{Username:$username,Pw:$password}' \
    | curl --fail --silent --request POST "${base_url}/Users/AuthenticateByName" \
        --header "${auth_header}" --header 'Content-Type: application/json' --data-binary @-)"
access_token="$(jq -r '.AccessToken' <<<"${auth_result}")"
admin_user_id="$(jq -r '.User.Id' <<<"${auth_result}")"
token_header="Authorization: MediaBrowser Token=\"${access_token}\""
providers="$(curl --fail --silent "${base_url}/Auth/Providers" --header "${token_header}")"
jq -e '.[] | select(.Name == "Kunini Auth")' <<<"${providers}" >/dev/null
curl --fail --silent "${base_url}/KuniniDiscover/script.js" | grep -F 'Discover beyond your library' >/dev/null
curl --fail --silent "${base_url}/KuniniDiscover/styles.css" | grep -F '.kunini-discover-section' >/dev/null
discover_source_id="$(curl --fail --silent \
    "${base_url}/Items?Recursive=true&IncludeItemTypes=Movie,Series&HasTmdbId=true&Limit=1&Fields=ProviderIds" \
    --header "${token_header}" | jq -r '.Items[0].Id // empty')"
if [[ -z "${discover_source_id}" ]]; then
    echo "No TMDb-backed movie or series is available for Discover verification." >&2
    exit 1
fi
discover_response="$(curl --fail --silent \
    "${base_url}/KuniniDiscover/recommendations?itemId=${discover_source_id}&limit=5" \
    --header "${token_header}")"
jq -e '.source == "TMDb" and (.items | type == "array") and all(.items[]; (.title | length) > 0 and .year > 1800)' \
    <<<"${discover_response}" >/dev/null
discover_poster_url="$(jq -r '[.items[].posterUrl | select(. != null)][0] // empty' <<<"${discover_response}")"
if [[ -n "${discover_poster_url}" ]]; then
    discover_poster_type="$(curl --fail --silent --output /dev/null --write-out '%{content_type}' \
        "${base_url}${discover_poster_url}")"
    [[ "${discover_poster_type}" == "image/jpeg" ]]
fi
network_config="$(curl --fail --silent "${base_url}/System/Configuration/network" --header "${token_header}")"
if ! jq -e '.EnablePublishedServerUriByRequest == true' <<<"${network_config}" >/dev/null; then
    echo "Request-derived published server URLs are not enabled." >&2
    exit 1
fi
unbound_public_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
    "${base_url}/Users/${admin_user_id}" \
    --header "Host: ${browser_domain}" \
    --header "${token_header}")"
if [[ "${unbound_public_status}" != "403" ]]; then
    echo "An authenticated public-host request was not bound to a Kunini identity." >&2
    exit 1
fi
direct_host_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
    "${base_url}/Users/${admin_user_id}" \
    --header "Host: ${app_domain}" \
    --header "${token_header}")"
spoofed_direct_host_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
    "${base_url}/Users/${admin_user_id}" \
    --header "Host: ${app_domain}" \
    --header 'X-Kunini-Auth-User: attacker' \
    --header "${token_header}")"
if [[ "${direct_host_status}" != "200" || "${spoofed_direct_host_status}" != "403" ]]; then
    echo "Direct Jellyfin host authorization boundary failed verification." >&2
    exit 1
fi

folders="$(curl --fail --silent "${base_url}/Library/VirtualFolders" --header "${token_header}")"
jq -e '
    (map(select(.Name == "Movies" and (.Locations | index("/media/movies")))) | length) == 1 and
    (map(select(.Name == "Series" and (.Locations | index("/media/series")))) | length) == 1 and
    (map(select(.Name == "Videos" and (.Locations | index("/media/videos")))) | length) == 1
' <<<"${folders}" >/dev/null

task="$(curl --fail --silent "${base_url}/ScheduledTasks" --header "${token_header}" | jq -c '.[] | select(.Key == "RefreshLibrary")')"
item_count="$(curl --fail --silent --get "${base_url}/Items" --header "${token_header}" \
    --data-urlencode 'Recursive=true' --data-urlencode 'IncludeItemTypes=Movie,Episode,Video' --data-urlencode 'Limit=0' | jq -r '.TotalRecordCount')"
curl --fail --silent --request POST "${base_url}/Sessions/Logout" --header "${token_header}" >/dev/null || true

browser_http_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "http://${browser_domain}/")"
browser_https_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "https://${browser_domain}/")"
browser_login_location="$(curl --silent --head "https://${browser_domain}/" | tr -d '\r' | awk 'tolower($1) == "location:" { print $2 }')"
if [[ "${browser_http_status}" != "301" || "${browser_https_status}" != "302" \
    || "${browser_login_location}" != https://auth.kunini.ru/login* ]]; then
    echo "External HTTP/TLS/Kunini Auth boundary mismatch." >&2
    exit 1
fi

app_http_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "http://${app_domain}/")"
app_public_info="$(curl --fail --silent "https://${app_domain}/System/Info/Public")"
if [[ "${app_http_status}" != "301" ]] \
    || ! jq -e '.ProductName == "Jellyfin Server" and .StartupWizardCompleted == true' <<<"${app_public_info}" >/dev/null; then
    echo "External direct Jellyfin endpoint failed public discovery verification." >&2
    exit 1
fi

app_auth_header='Authorization: MediaBrowser Client="Kunini App Verification", Device="Server", DeviceId="kunini-app-verification", Version="1.0.0"'
app_auth_result="$(jq -n --arg username dkunin --arg password "${admin_password}" '{Username:$username,Pw:$password}' \
    | curl --fail --silent --request POST "https://${app_domain}/Users/AuthenticateByName" \
        --header "${app_auth_header}" --header 'Content-Type: application/json' --data-binary @-)"
app_access_token="$(jq -r '.AccessToken' <<<"${app_auth_result}")"
app_token_header="Authorization: MediaBrowser Token=\"${app_access_token}\""
app_system_info="$(curl --fail --silent "https://${app_domain}/System/Info" --header "${app_token_header}")"
if ! jq -e --arg address "https://${app_domain}" '.LocalAddress == $address' <<<"${app_system_info}" >/dev/null; then
    echo "The direct Jellyfin endpoint did not publish its own address." >&2
    exit 1
fi
curl --fail --silent --request POST "https://${app_domain}/Sessions/Logout" --header "${app_token_header}" >/dev/null || true

browser_certificate_expiry="$(openssl s_client -connect "${browser_domain}:443" -servername "${browser_domain}" </dev/null 2>/dev/null \
    | openssl x509 -noout -enddate | cut -d= -f2-)"
app_certificate="$(openssl s_client -connect "${app_domain}:443" -servername "${app_domain}" </dev/null 2>/dev/null | openssl x509)"
if ! openssl x509 -checkhost "${app_domain}" -noout <<<"${app_certificate}" >/dev/null; then
    echo "The direct Jellyfin endpoint certificate does not match its hostname." >&2
    exit 1
fi
app_certificate_expiry="$(openssl x509 -noout -enddate <<<"${app_certificate}" | cut -d= -f2-)"
scan_state="$(jq -r '.State' <<<"${task}")"
scan_progress="$(jq -r '.CurrentProgressPercentage // 0' <<<"${task}")"

printf 'container=healthy image=%s port=%s media=%s bridge=%s dns=%s tmdb=%s/%s libraries=3 items=%s scan=%s progress=%s%% browser_cert=%s app_cert=%s\n' \
    "${expected_digest}" "${published_port}" "${media_mode}" "${bridge_mode}" "${configured_dns}" \
    "${tmdb_api_status}" "${tmdb_image_status}" "${item_count}" "${scan_state}" "${scan_progress}" \
    "${browser_certificate_expiry}" "${app_certificate_expiry}"
