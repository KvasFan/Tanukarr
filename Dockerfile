# syntax=docker/dockerfile:1

# Tanukarr
#
# Three stages: the React UI, the .NET backend, then a runtime image holding only
# the published output. Building the UI separately means a backend-only change does
# not reinstall node_modules, and vice versa.

# ---------------------------------------------------------------------------
# UI
# ---------------------------------------------------------------------------
FROM node:24-bookworm AS ui

WORKDIR /src

# Copy the manifests alone first so the dependency layer is reused whenever
# application source changes but dependencies do not.
COPY package.json yarn.lock .yarnrc ./
RUN yarn install --frozen-lockfile --network-timeout 600000

COPY index.html vite.config.ts tsconfig.json ./
COPY frontend/ ./frontend/

# Fail the image build on frontend regressions before packaging Vite's production
# bundle. The checks use the same toolchain as CI.
RUN yarn test:queue-details && yarn lint && yarn stylelint && yarn typecheck && yarn build

# ---------------------------------------------------------------------------
# Backend
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS backend

ARG TARGETARCH
WORKDIR /src

COPY . .

# Map Docker's architecture names onto the .NET runtime identifiers, then build the
# way the upstream CI does. SelfContained keeps the runtime with the app so the final
# image needs no SDK.
#
# Platform is Posix for every Linux target. It names the OS family, not the CPU: the
# solution only declares "Any CPU", "Posix" and "Windows", so passing an architecture
# here fails with MSB4126 before anything compiles. The architecture is carried by
# RuntimeIdentifiers instead.
RUN set -eux; \
    case "${TARGETARCH}" in \
      amd64) RID=linux-x64   ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "Unsupported architecture: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    dotnet msbuild -restore src/Sonarr.sln \
      -p:SelfContained=true \
      -p:Configuration=Release \
      -p:Platform=Posix \
      -p:RuntimeIdentifiers="${RID}" \
      -t:PublishAllRids; \
    mkdir -p /app; \
    cp -r "_output/net10.0/${RID}/publish/." /app/

# The native amd64 build verifies the same published backend. The initial
# Tanukarr release publishes amd64 only; other targets require their own runtime
# verification before they can be advertised as supported release images.
# Integration tests require configured external services and run separately.
# NuGet distributes ffprobe with mode 644. Disk-access tests execute the binary
# from their own output directory before /app is made executable.
RUN set -eux; \
    if [ "${TARGETARCH}" = "amd64" ]; then \
      chmod +x _tests/net10.0/linux-x64/ffprobe; \
      for suite in Core Api Http; do \
        dotnet test "_tests/net10.0/linux-x64/Sonarr.${suite}.Test.dll" \
          --filter "TestCategory!=IntegrationTest" --logger "console;verbosity=minimal"; \
      done; \
      dotnet test _tests/net10.0/linux-x64/Sonarr.Common.Test.dll \
        --filter "FullyQualifiedName~SonarrCloudRequestPolicyFixture|FullyQualifiedName~CleanseLogMessageFixture|FullyQualifiedName~BuildInfoFixture|FullyQualifiedName~HttpResponseLoggingFixture|FullyQualifiedName~HttpClientSuppressedLoggingFixture" --logger "console;verbosity=minimal"; \
    fi

# The UI is built separately and is not produced by the .NET build.
COPY --from=ui /src/_output/UI /app/UI

# The publish output is platform-agnostic, so it carries assemblies this image can
# never use. Upstream's packaging drops the same ones for its Linux builds: the
# Windows platform assembly, and the Windows service installers. Sonarr.Update goes
# too, because updating happens through Docker here.
RUN set -eux; \
    rm -rf /app/Sonarr.Update; \
    rm -f /app/Sonarr.Windows.*; \
    rm -f /app/ServiceInstall.* /app/ServiceUninstall.*; \
    chmod +x /app/Sonarr; \
    find /app -name ffprobe -exec chmod +x {} \;

# ---------------------------------------------------------------------------
# Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS runtime

ARG SONARRWEEB_VERSION=1.0.0
ARG SONARRWEEB_SOURCE=https://github.com/KvasExert/Tanukarr
ARG SONARRWEEB_REVISION
ARG SONARRWEEB_CREATED
# Legacy build arguments remain valid; Tanukarr arguments take precedence.
ARG TANUKARR_VERSION=${SONARRWEEB_VERSION}
ARG TANUKARR_SOURCE=${SONARRWEEB_SOURCE}
ARG TANUKARR_REVISION=${SONARRWEEB_REVISION}
ARG TANUKARR_CREATED=${SONARRWEEB_CREATED}

# runtime-deps already carries what a self-contained .NET app needs to start —
# ICU for globalization, ca-certificates for outbound HTTPS, tzdata — so only the
# extras go here. gosu drops privileges in the entrypoint; libsqlite3-0 backs the
# database.
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends \
      gosu \
      libsqlite3-0; \
    rm -rf /var/lib/apt/lists/*

COPY --from=backend /app /app
COPY --chmod=0755 docker/entrypoint.sh /entrypoint.sh

# This image is a binary distribution of GPLv3 software, so it carries its licence
# and a pointer to the corresponding source.
COPY --chmod=0644 LICENSE.md /app/LICENSE.md

# Static assets copied from a Windows workspace can retain DrvFs write bits.
# Keep all shipped application files immutable to the unprivileged app user.
RUN chmod -R go-w /app

# /config holds the database, config.xml and logs. Everything else is media.
VOLUME ["/config"]
EXPOSE 8989

ENV XDG_CONFIG_HOME=/config \
    PUID=1000 \
    PGID=1000 \
    TZ=Etc/UTC \
    COMPlus_EnableDiagnostics=0

# Docker restarts the container on failure, so the app does not need to fork or
# manage its own lifetime.
ENTRYPOINT ["/entrypoint.sh"]
CMD ["/app/Sonarr", "-nobrowser", "-data=/config"]

LABEL org.opencontainers.image.title="Tanukarr" \
      org.opencontainers.image.description="TV series and anime management with per-series TheTVDB, TMDB, AniList and MyAnimeList (Jikan) metadata, episode ordering options, multi-season packs and library integrations." \
      org.opencontainers.image.source="${TANUKARR_SOURCE}" \
      org.opencontainers.image.version="${TANUKARR_VERSION}" \
      org.opencontainers.image.revision="${TANUKARR_REVISION}" \
      org.opencontainers.image.created="${TANUKARR_CREATED}" \
      org.opencontainers.image.licenses="GPL-3.0-only"
