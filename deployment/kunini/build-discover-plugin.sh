#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_dir="${script_dir}/discover-plugin"
output_dir="${source_dir}/build"
sdk_image="mcr.microsoft.com/dotnet/sdk:10.0.100@sha256:c7445f141c04f1a6b454181bd098dcfa606c61ba0bd213d0a702489e5bd4cd71"

mkdir -p "${output_dir}"
node --test "${source_dir}/Jellyfin.Plugin.KuniniDiscover/Web/kunini-discover.test.js"

run_build() {
    docker run --rm \
        --user "$(id -u):$(id -g)" \
        --env DOTNET_CLI_HOME=/tmp/dotnet \
        --env NUGET_PACKAGES=/tmp/nuget/packages \
        --volume "${source_dir}:/src" \
        --workdir /src \
        "${sdk_image}" \
        bash -euo pipefail -c '
            dotnet restore KuniniDiscover.Tests/KuniniDiscover.Tests.csproj --locked-mode
            dotnet run --project KuniniDiscover.Tests/KuniniDiscover.Tests.csproj --configuration Release --no-restore
            dotnet publish Jellyfin.Plugin.KuniniDiscover/Jellyfin.Plugin.KuniniDiscover.csproj --configuration Release --no-restore --output /src/build
        '
}

if ! run_build; then
    echo "The pinned SDK container failed once; retrying the reproducible build." >&2
    run_build
fi

test -s "${output_dir}/Jellyfin.Plugin.KuniniDiscover.dll"
printf 'Kunini Discover plugin built at %s\n' "${output_dir}"
