#!/usr/bin/env bash
set -euo pipefail

app_dir="${APP_DIR:-/var/apps/jellyfin}"
source_env="${TRAILER_PROXY_SOURCE_ENV:-/var/apps/tele-you-video/.env}"
target="${app_dir}/secrets/socks-proxy-url"

if [[ -s "${target}" ]]; then
    proxy_url="$(<"${target}")"
else
    if [[ ! -r "${source_env}" ]]; then
        echo "Trailer proxy source environment is not readable: ${source_env}" >&2
        exit 1
    fi
    proxy_url="$(awk -F= '
        $1 == "SOCKS_PROXY_URL" || $1 == "SOCKS5_PROXY" || $1 == "PROXY_SOCKS" {
            sub(/^[^=]*=/, ""); print; exit
        }
    ' "${source_env}")"
fi

proxy_url="${proxy_url%$'\r'}"
proxy_url="${proxy_url#\"}"
proxy_url="${proxy_url%\"}"
proxy_url="${proxy_url#\'}"
proxy_url="${proxy_url%\'}"
case "${proxy_url}" in
    socks5://*|socks5h://*) ;;
    *)
        echo "Trailer proxy must be a socks5:// or socks5h:// URL." >&2
        exit 1
        ;;
esac
if [[ "${proxy_url}" == *$'\n'* || "${#proxy_url}" -gt 2048 ]]; then
    echo "Trailer proxy URL is malformed." >&2
    exit 1
fi

umask 077
temporary="$(mktemp "${target}.tmp.XXXXXX")"
trap 'rm -f "${temporary}"' EXIT
printf '%s\n' "${proxy_url}" >"${temporary}"
chmod 0600 "${temporary}"
mv "${temporary}" "${target}"
trap - EXIT
printf 'Trailer SOCKS proxy secret is ready at %s.\n' "${target}"
