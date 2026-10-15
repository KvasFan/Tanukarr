# Install Tanukarr 0.1.0 with Docker

Tanukarr manages TV series, anime and movie collections. The 0.1.0 reset derives
from 1.1.0 and keeps its database schema and APIs. It adds graphical account
creation, collection/quality fixes and configurable metadata catalogues including
TVmaze. Read the [release notes](releases/0.1.0.md) and
[verification ledger](releases/0.1.0-verification.md) for actual publication
status, exact source/image identities and remaining security findings.

The intended release image is `ghcr.io/kvasexpert/tanukarr:0.1.0`. The GitHub
repository contains source, while GHCR stores Docker images. Published support
is Linux amd64; a configured build target is not a tested or published platform.
Previous [1.1.0 publication evidence](releases/1.1.0-publication.md) describes
that historical image, not this new one.

## Requirements

Use [Docker Engine](https://docs.docker.com/engine/install/) with the
[Compose plugin](https://docs.docker.com/compose/install/), or Docker Desktop
configured for Linux containers. Check `docker version` and `docker compose
version`. The published image needs no local Node or .NET SDK.

The default host address is **http://localhost:6970**, forwarding to container
port **8989**. Keep configuration, ports and download-client categories separate
from any other Sonarr instance. Use a non-root numeric host UID and GID that own
the media directories; root or malformed PUID/PGID values are rejected.

## Complete Compose configuration

Create a separate installation directory and save this as `compose.yaml`:

```yaml
name: tanukarr

services:
  tanukarr:
    image: ghcr.io/kvasexpert/tanukarr:0.1.0
    container_name: tanukarr
    restart: unless-stopped
    security_opt:
      - no-new-privileges:true
    ports:
      - "127.0.0.1:6970:8989"
    environment:
      PUID: "${TANUKARR_PUID:-1000}"
      PGID: "${TANUKARR_PGID:-1000}"
      UMASK: "${TANUKARR_UMASK:-002}"
      TZ: "${TANUKARR_TZ:-Etc/UTC}"
    volumes:
      - tanukarr-config:/config
      - /srv/tanukarr/media:/media

volumes:
  tanukarr-config:
    name: tanukarr-config
```

On Linux, run `id -u` and `id -g` for the account managing your media. Store the
numbers and preferred timezone in `.env` beside the Compose file:

```dotenv
TANUKARR_PUID=1000
TANUKARR_PGID=1000
TANUKARR_TZ=Europe/Rome
```

Replace the media path with your actual library/download parent. For a new,
empty directory owned by UID/GID 1000:

```sh
sudo install -d -m 2775 -o 1000 -g 1000 /srv/tanukarr/media
sudo install -d -m 2775 -o 1000 -g 1000 /srv/tanukarr/media/downloads
sudo install -d -m 2775 -o 1000 -g 1000 /srv/tanukarr/media/tv
docker compose config
docker compose pull
docker compose up -d
docker compose ps
```

The entrypoint secures `/config` with mode 0750 and the selected owner/group;
it does not recursively change media ownership. Do not commit `.env`, real
configuration or credentials. To use bind-mounted configuration, replace
`tanukarr-config:/config` with `/srv/tanukarr/config:/config` and prepare that
directory for the selected UID/GID. Keep the existing mount on an upgrade.
`docker compose down -v` removes named volumes and their data.

The release's `compose.yaml` asset offers `./config` and `./media` bind defaults
and `TANUKARR_*` overrides for bind address, port and paths. Inspect the rendered
configuration before starting it. Legacy `SONARRWEEB_*` and `SONARR_PRO_*`
deployment variable aliases remain accepted.

## First-run account creation

Open **http://localhost:6970**. Choose a username and a password of 12 to 128
characters in **Create your account**, confirm it and submit. No default password
is generated or printed in logs. The form is protected by an antiforgery token;
after one account exists it cannot replace that account. Subsequent visits show
the regular login form. Existing users, legacy configuration migration and
explicit `SONARR__AUTH__INITIALUSERNAME` / `SONARR__AUTH__INITIALPASSWORD`
provisioning remain supported. Keep provisioned secrets outside version control
and avoid displaying resolved secret-containing configuration.

The packaged default uses Forms authentication. Authentication bypass for local
addresses applies only if explicitly configured. Keep the default loopback port
binding, or place remote access behind a correctly configured HTTPS reverse
proxy. URL Base and proxy paths must agree. Do not expose an unclaimed first-run
form publicly before creating its account.

## Configure the library

Add `/media/tv` as a root folder and use the same container paths in download
clients; a common parent allows hardlinks and moves. Configure clients and
indexers separately from metadata. A search cannot produce indexer releases
without a reachable, enabled indexer supporting that search type.

Settings → Metadata Source enables and orders TheTVDB, TMDB, AniList,
MyAnimeList/Jikan and TVmaze. TMDB needs a user-supplied v3 API key; other listed
public catalogue paths do not need a key here. Add New Series can search all
enabled catalogues or one selected source. Existing series keep their owning
catalogue and native episode numbering. Consult the [provider guide](metadata-providers.md)
for coverage, credentials and ordering limits.

Quality choices, groups, sizes and codec factors are managed under
Settings → Quality. Settings → Profiles retains profile policy and custom
formats. See the [quality guide](quality-and-codec-size.md),
[collection guide](anime-season-groups.md), [list guide](library-tools.md),
[Jellyfin refresh](jellyfin.md) and [watched-state guide](jellyfin-watched.md).
Compression uses the optional separate [host worker](../docker/compression/README.md).

## Upgrade and pin an image

**0.1.0 is a version reset from 1.1.0, not an older database format.** Stop the
existing application and back up `/config` consistently before upgrading. Keep
the exact configuration/media mounts, Compose project identity, port and
authentication. Changing a display/service name must not select a new empty
configuration volume. Preserve the old image and matching backup for rollback.

Change the image in the existing Compose file, then run:

```sh
docker compose pull
docker compose up -d
docker compose ps
docker compose logs --tail=100 tanukarr
```

`docker compose restart` keeps the old image. Database migrations can prevent
rollback; restore a compatible backup into a separate location when necessary.
The reset keeps migrations 251/252 from 1.1.0 and v3/v5 APIs. The inherited
internal Core version differs from Tanukarr's product version.

The release workflow assigns `latest` to the tested release, `edge` to main,
and `sha-<full-commit>` to a build. A digest pins exact image bytes; use the
published platform manifest from the new verification ledger in the form
`ghcr.io/kvasexpert/tanukarr@sha256:<published-manifest>`. Do not reuse the
historical 1.1.0 digest for the new release. Only use assets whose publication
and checks are actually recorded in the ledger.

Anonymous pulls should work for public packages. If registry policy requires
authentication, use `docker login ghcr.io -u <github-user>` and enter an
appropriately scoped token at its password prompt. Do not put it on a command
line or in committed files.

## Offline image and source builds

Download the image archive and `SHA256SUMS` from the actual GitHub release.
Verify its filename and checksum before loading:

```sh
sha256sum --ignore-missing --check SHA256SUMS
docker load --input Tanukarr-0.1.0-linux-amd64-docker.tar.gz
docker compose up -d --pull never
```

On Windows use `Get-FileHash -Algorithm SHA256`. A checksum verifies downloaded
bytes against the checksum file; it is not a separate publisher signature.
Use the exact image tag printed by `docker load`. GitHub source archives cannot
be loaded as Docker images.

To build the released source:

```sh
git clone --branch v0.1.0 --depth 1 https://github.com/KvasExpert/Tanukarr.git
cd Tanukarr
docker build --progress=plain --build-arg TANUKARR_VERSION=0.1.0 \
  --build-arg TANUKARR_REVISION="$(git rev-parse HEAD)" -t tanukarr:0.1.0 .
```

Use `image: tanukarr:0.1.0` in Compose. The repository's own Compose file remains
a local-build deployment using `tanukarr:local`; it does not pull the release.
Build requirements and actual test scope are in [CONTRIBUTING.md](../CONTRIBUTING.md)
and the [container test guide](../docker/tests/README.md). Build/test configuration
alone does not establish that a release was published or verified.

The release assets include the image, corresponding source, SBOM, verification
reports, manifest, Compose configuration and checksums. Preserve the GPL license
and contributor notices. Superseded GitHub release assets are backed up before
the requested replacement; dated reports retain their original evidence. The
former 0.1.0 source is distinguished by its archive tag and
[legacy report](releases/legacy-0.1.0-verification.md).

## Troubleshooting

Check `docker compose ps`, `/ping`, login and sanitized logs. Permission errors
usually require matching UID/GID and mount ownership. For search problems, verify
provider credentials, enabled sources, native numbering and indexer/client tests.
Share the Tanukarr version, source/image digest, reproduction and sanitized logs
in [this project's tracker](https://github.com/KvasExpert/Tanukarr/issues).
Remove API keys, cookies, passwords and private paths before sharing diagnostics.
