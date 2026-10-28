#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:8096"
domain="media.kunini.ru"
password_file="/var/apps/jellyfin/secrets/admin-password"
expected_digest="sha256:fea863d0cd9ec445985d9c75fc6ed64e38a1d2766cf0c250a33d553f4fc1b771"
auth_header='Authorization: MediaBrowser Client="Kunini Verification", Device="Server", DeviceId="kunini-verification", Version="1.0.0"'

curl --fail --silent "${base_url}/health" >/dev/null
container_health="$(docker container inspect jellyfin --format '{{.State.Health.Status}}')"
container_digest="$(docker image inspect "$(docker container inspect jellyfin --format '{{.Image}}')" --format '{{index .RepoDigests 0}}' | sed 's/^[^@]*@//')"
published_port="$(docker port jellyfin 8096/tcp)"
media_mode="$(docker container inspect jellyfin --format '{{range .Mounts}}{{if eq .Destination "/media"}}{{.Mode}}{{end}}{{end}}')"
bridge_mode="$(docker container inspect jellyfin --format '{{range .Mounts}}{{if eq .Destination "/config/plugins/KuniniAuth/Jellyfin.Plugin.KuniniAuth.dll"}}{{.Mode}}{{end}}{{end}}')"

if [[ "${container_health}" != "healthy" || "${container_digest}" != "${expected_digest}" ]]; then
    echo "Container health or image digest mismatch." >&2
    exit 1
fi
if [[ "${published_port}" != "127.0.0.1:8096" || "${media_mode}" != "ro" || "${bridge_mode}" != "ro" ]]; then
    echo "Listener or read-only mount mismatch." >&2
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
unbound_public_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
    "${base_url}/Users/${admin_user_id}" \
    --header 'Host: media.kunini.ru' \
    --header "${token_header}")"
if [[ "${unbound_public_status}" != "403" ]]; then
    echo "An authenticated public-host request was not bound to a Kunini identity." >&2
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

http_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "http://${domain}/")"
https_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "https://${domain}/")"
login_location="$(curl --silent --head "https://${domain}/" | tr -d '\r' | awk 'tolower($1) == "location:" { print $2 }')"
if [[ "${http_status}" != "301" || "${https_status}" != "302" || "${login_location}" != https://auth.kunini.ru/login* ]]; then
    echo "External HTTP/TLS/Kunini Auth boundary mismatch." >&2
    exit 1
fi

certificate_expiry="$(openssl s_client -connect "${domain}:443" -servername "${domain}" </dev/null 2>/dev/null | openssl x509 -noout -enddate | cut -d= -f2-)"
scan_state="$(jq -r '.State' <<<"${task}")"
scan_progress="$(jq -r '.CurrentProgressPercentage // 0' <<<"${task}")"

printf 'container=healthy image=%s port=%s media=%s bridge=%s libraries=3 items=%s scan=%s progress=%s%% cert_expiry=%s\n' \
    "${expected_digest}" "${published_port}" "${media_mode}" "${bridge_mode}" "${item_count}" "${scan_state}" "${scan_progress}" "${certificate_expiry}"
