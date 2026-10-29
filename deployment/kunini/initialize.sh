#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:8096"
app_dir="/var/apps/jellyfin"
password_file="${app_dir}/secrets/admin-password"
auth_header='Authorization: MediaBrowser Client="Kunini Bootstrap", Device="Server", DeviceId="kunini-bootstrap", Version="1.0.0"'

for attempt in $(seq 1 60); do
    system_info="$(curl --fail --silent "${base_url}/System/Info/Public" 2>/dev/null || true)"
    if [[ -n "${system_info}" ]]; then
        break
    fi
    if [[ "${attempt}" -eq 60 ]]; then
        echo "Jellyfin did not become ready." >&2
        exit 1
    fi
    sleep 2
done

startup_complete="$(jq -r '.StartupWizardCompleted' <<<"${system_info}")"
if [[ "${startup_complete}" != "true" ]]; then
    install -d -m 0700 "${app_dir}/secrets"
    if [[ ! -s "${password_file}" ]]; then
        umask 077
        openssl rand -hex 24 >"${password_file}"
    fi

    admin_password="$(<"${password_file}")"
    curl --fail --silent --request POST "${base_url}/Startup/Configuration" \
        --header 'Content-Type: application/json' \
        --data '{"ServerName":"Kunini Media","UICulture":"ru","MetadataCountryCode":"RU","PreferredMetadataLanguage":"ru"}' >/dev/null
    jq -n --arg name dkunin --arg password "${admin_password}" '{Name:$name,Password:$password}' \
        | curl --fail --silent --request POST "${base_url}/Startup/User" --header 'Content-Type: application/json' --data-binary @- >/dev/null
    curl --fail --silent --request POST "${base_url}/Startup/RemoteAccess" \
        --header 'Content-Type: application/json' --data '{"EnableRemoteAccess":true}' >/dev/null
    curl --fail --silent --request POST "${base_url}/Startup/Complete" >/dev/null
elif [[ ! -s "${password_file}" ]]; then
    echo "Jellyfin is already initialized, but ${password_file} is missing." >&2
    exit 1
fi

admin_password="$(<"${password_file}")"
auth_result="$(jq -n --arg username dkunin --arg password "${admin_password}" '{Username:$username,Pw:$password}' \
    | curl --fail --silent --request POST "${base_url}/Users/AuthenticateByName" \
        --header "${auth_header}" --header 'Content-Type: application/json' --data-binary @-)"
access_token="$(jq -r '.AccessToken' <<<"${auth_result}")"
token_header="Authorization: MediaBrowser Token=\"${access_token}\""

network_config="$(curl --fail --silent "${base_url}/System/Configuration/network" --header "${token_header}")"
jq '.KnownProxies = ((.KnownProxies // []) + ["172.30.0.1"] | unique)
    | .EnableRemoteAccess = true
    | .AutoDiscovery = false
    | .EnableUPnP = false
    | .EnablePublishedServerUriByRequest = true
    | .PublicHttpPort = 80
    | .PublicHttpsPort = 443' <<<"${network_config}" \
    | curl --fail --silent --request POST "${base_url}/System/Configuration/network" \
        --header "${token_header}" --header 'Content-Type: application/json' --data-binary @- >/dev/null

encoding_config="$(curl --fail --silent "${base_url}/System/Configuration/encoding" --header "${token_header}")"
jq '.HardwareAccelerationType = "vaapi"
    | .VaapiDevice = "/dev/dri/renderD128"
    | .EnableHardwareEncoding = true' <<<"${encoding_config}" \
    | curl --fail --silent --request POST "${base_url}/System/Configuration/encoding" \
        --header "${token_header}" --header 'Content-Type: application/json' --data-binary @- >/dev/null

virtual_folders="$(curl --fail --silent "${base_url}/Library/VirtualFolders" --header "${token_header}")"
add_library() {
    local name="$1"
    local type="$2"
    local path="$3"
    if jq -e --arg name "${name}" '.[] | select(.Name == $name)' <<<"${virtual_folders}" >/dev/null; then
        return
    fi
    curl --fail --silent --request POST --get "${base_url}/Library/VirtualFolders" \
        --header "${token_header}" \
        --data-urlencode "name=${name}" \
        --data-urlencode "collectionType=${type}" \
        --data-urlencode "paths=${path}" \
        --data-urlencode 'refreshLibrary=false' >/dev/null
}

add_library Movies movies /media/movies
add_library Series tvshows /media/series
add_library Videos homevideos /media/videos

tasks="$(curl --fail --silent "${base_url}/ScheduledTasks" --header "${token_header}")"
scan_task_id="$(jq -r '.[] | select(.Key == "RefreshLibrary") | .Id' <<<"${tasks}")"
if [[ -z "${scan_task_id}" ]]; then
    echo "RefreshLibrary task was not found." >&2
    exit 1
fi
curl --fail --silent --request POST "${base_url}/ScheduledTasks/Running/${scan_task_id}" --header "${token_header}" >/dev/null
curl --fail --silent --request POST "${base_url}/Sessions/Logout" --header "${token_header}" >/dev/null || true

printf 'Jellyfin initialized; library scan started. Admin password: %s\n' "${password_file}"
