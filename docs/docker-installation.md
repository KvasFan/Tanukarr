# Install Tanukarr 1.0.0 with Docker

Tanukarr is an independent Sonarr fork. Its source and release notes are on
[GitHub](https://github.com/KvasExert/Tanukarr). The **1.0.0**
[release notes](releases/1.0.0.md) describe the Tanukarr rename, supplied Tanuki
logos, selected-library Jellyfin connection, AniList inclusion/exclusion and
request-limit recovery, and Nyaa query encoding. Original
Sonarr and fork contributors, including recorded AI tool/model attribution, are
listed in [CONTRIBUTORS.md](../CONTRIBUTORS.md).

GitHub Container Registry (GHCR) is GitHub's Docker image registry. The installation
below uses `ghcr.io/kvasexert/tanukarr:1.0.0`; the GitHub repository itself is source
code, not a Docker image. Check the release notes and
[package page](https://github.com/users/KvasExert/packages/container/package/tanukarr) for
the published architectures and image digest. Use only a tag that has finished
publishing. A configured workflow does not establish that an image is available.

## Prerequisites

Install [Docker Engine](https://docs.docker.com/engine/install/) and the
[Docker Compose plugin](https://docs.docker.com/compose/install/), or Docker
Desktop configured for Linux containers. Check `docker version` and
`docker compose version`. These examples use Compose v2-style commands and do not
require Node, .NET or a local source build.

The default host port is **6970** and the container port is **8989**. The examples
publish only to `127.0.0.1`; upstream Sonarr can continue using host port 8989.
Keep Tanukarr's configuration and download-client category separate from other
Sonarr instances.

## Docker Compose: named configuration volume

Create an installation directory and save the following as `compose.yaml`:

```yaml
name: tanukarr

services:
  tanukarr:
    image: ghcr.io/kvasexert/tanukarr:1.0.0
    container_name: tanukarr
    restart: unless-stopped
    ports:
      - "127.0.0.1:6970:8989"
    environment:
      PUID: "${TANUKARR_PUID:-1000}"
      PGID: "${TANUKARR_PGID:-1000}"
      TZ: "${TANUKARR_TZ:-Etc/UTC}"
      UMASK: "${TANUKARR_UMASK:-002}"
    volumes:
      - tanukarr-config:/config
      - /srv/tanukarr/media:/media

volumes:
  tanukarr-config:
    name: tanukarr-config
```

On Linux, run `id -u` and `id -g` for the host account that manages media. Save
their numbers in a `.env` file beside `compose.yaml`, for example:

```dotenv
TANUKARR_PUID=1000
TANUKARR_PGID=1000
TANUKARR_TZ=Europe/Rome
```

Do not commit a production `.env` or configuration directory to Git. Change the
media path to your actual library/download parent before starting. Create and
grant access to that directory for the chosen UID/GID; the entrypoint fixes
`/config` ownership but does not recursively change your media library. For a
new, empty location with UID/GID 1000:

```sh
sudo install -d -m 2775 -o 1000 -g 1000 /srv/tanukarr/media
sudo install -d -m 2775 -o 1000 -g 1000 /srv/tanukarr/media/downloads
sudo install -d -m 2775 -o 1000 -g 1000 /srv/tanukarr/media/tv
docker compose config
docker compose pull
docker compose up -d
docker compose ps
```

Open **http://localhost:6970**. The named volume holds the database, `config.xml`,
settings and logs. Recreating the container retains this volume. Avoid
`docker compose down -v`, which deletes it.

Once the [1.0.0 release](https://github.com/KvasExert/Tanukarr/releases/tag/v1.0.0)
has finished publishing, its ready-to-use
[`compose.yaml`](https://github.com/KvasExert/Tanukarr/releases/download/v1.0.0/compose.yaml)
with bind-mounted `./config` and `./media` defaults is an alternative to the
named-volume example. Download that file into a new
installation directory, create those two directories with the correct permissions,
and run the same Compose commands above. Its `TANUKARR_*` variables also allow
custom bind address, host port, config path and media path; inspect
`docker compose config` before starting. That release YAML pulls the published
image and does not require cloning the source repository.

## Bind-mounted configuration

If you prefer configuration files in the installation directory, replace the
configuration volume line with a bind mount:

```yaml
    volumes:
      - ./config:/config
      - /srv/tanukarr/media:/media
```

Remove the top-level `volumes:` declaration from that example. Create `config`
before startup and make it writable by the selected PUID/PGID. Back up this
directory separately from your media. On Docker Desktop, use a host directory
shared with Docker; on WSL, paths must be visible to the Docker daemon's Linux
distribution. A Windows path visible to PowerShell is not automatically a Linux
bind-mount path.

## Storage, downloads and hardlinks

Mount downloads and the library through one shared parent such as `/media`, and
configure root folders and the download client using matching paths:

```text
/media/downloads/tv    completed downloads
/media/tv              Tanukarr library root
```

Use a separate download category, for example `tanukarr`. If the download client
runs in another container, mount that same host parent at `/media` there too.
If its reported path differs, configure a Remote Path Mapping in Tanukarr.
Remote Path Mapping translates paths; it does not mount storage or fix ownership.

Hardlinks require downloads and the library to be on the **same underlying
filesystem**, with permissions that allow the import. One parent mount helps
preserve that relationship; it does not make separate disks or network shares
support hardlinks. Enable Sonarr's hardlink option where supported. Torrent files
can then remain available for seeding without duplicating file content. Imports
that cannot hardlink may copy files instead, consuming additional space.

## First-run configuration and authentication

Configure authentication in **Settings -> General**. Select Forms authentication,
set a unique username and password, and require authentication for every client
address when the instance is accessible beyond your local workstation. The API
uses its own API key; treat that key and configuration backups as secrets.

The default **Instance Name** is `Tanukarr`. When saving General settings, an
instance name must start or end with **`Tanukarr`** or legacy **`Sonarr`**, for
example `Tanukarr Home` or `Anime Sonarr`. This retains the existing anchored
prefix/suffix policy while accepting the renamed product. Reading an existing
custom instance name does not rename it automatically; a newly saved name must
pass the current validation.

The Compose port binds to loopback. For LAN access, deliberately replace
`127.0.0.1` with the host's LAN address and configure the allowed hostnames under
**Settings -> General** (`AllowedHosts` in `config.xml`). Use the actual hostname
or address clients send in the Host header. If using a reverse proxy, configure
its host and URL base consistently, enable HTTPS, and require authentication.
Do not expose an unauthenticated instance directly to the Internet.

Add root folders, indexers and download clients before starting searches. For
TMDB metadata or episode groups, configure your own key under
**Settings -> Metadata Source**. AniList, TheTVDB and MyAnimeList/Jikan do not use
that TMDB key. AniList account-list status, anime release status and custom-list
membership are separate filters; see [Library tools](library-tools.md).
Version 1.0.0 lets you include several AniList custom lists and veto entries by
custom-list membership or list state. For Jellyfin, configure the dedicated
connection under **Settings -> Connect** and select one initialized library;
see [Jellyfin refresh](jellyfin.md) for the three refresh modes and networking.

An existing upstream configuration must be **copied** to a separate Tanukarr
configuration directory or volume while the source instance is stopped for a
consistent backup. Migrations can make that copy incompatible with upstream.
Never point both running instances at the same `/config`, and retain the original
backup. Reusing a configuration does not validate every existing client or library
setting for the new instance.

## Pulling from GitHub Container Registry

A **public** GHCR package supports anonymous pulls:

```sh
docker pull ghcr.io/kvasexert/tanukarr:1.0.0
```

If GitHub reports `denied` or `unauthorized`, confirm that the tag exists and that
the package visibility is public. A public source repository does not by itself
make its container package public. Authorized users of a private package can
authenticate using their GitHub username and a token with `read:packages`:

```sh
docker login ghcr.io --username YOUR_GITHUB_USERNAME
```

Enter the token at the password prompt. Organization policies may also require
SSO authorization. Do not paste tokens into Compose YAML, documentation or a
shell command that saves them in command history.

## Version pins and updates

`1.0.0` is the Tanukarr release version; the application's inherited internal
Sonarr version may use a different numbering scheme. Prefer the exact release
tag above. For a reproducible deployment, replace the image line with the digest
recorded for that release:

```yaml
    image: ghcr.io/kvasexert/tanukarr@sha256:REPLACE_WITH_RELEASE_DIGEST
```

Release workflows also produce `latest` for release builds, `edge` for untagged
`main` development builds and `sha-<full-commit>` for a particular build. Tags can
be reassigned; a digest fixes the exact image. Architecture support and execution
results belong to the published release's validation evidence.

Before updating, back up `/config` consistently: stop the container before taking
a filesystem copy or volume backup. Read the new release notes, change the image
tag or digest in `compose.yaml`, then run:

```sh
docker compose pull
docker compose up -d
docker compose ps
docker compose logs --tail=100 tanukarr
```

Recreating the container uses the new image with the existing configuration.
`docker compose restart` keeps the container's old image. A database migration
may prevent downgrading; restore the pre-update backup into a separate configuration
location when a rollback requires it. Never overwrite your only backup.

## Release archive fallback

If a release includes a Docker image archive, download that archive and its
checksum from the [GitHub release](https://github.com/KvasExert/Tanukarr/releases/tag/v1.0.0).
Choose an architecture compatible with your Docker host. Verify the filename
listed in the release's checksum file before loading it:

```sh
sha256sum --ignore-missing --check SHA256SUMS
docker load --input Tanukarr-1.0.0-linux-amd64-docker.tar.gz
```

On Windows, use `Get-FileHash -Algorithm SHA256` and compare its output with
`SHA256SUMS`. A matching hash verifies the downloaded bytes against that file;
it is not a separate signature or publisher identity check. `docker load` prints
the loaded image tag. Set `image:` in Compose to that exact tag and start without
pulling:

```sh
docker compose up -d --pull never
```

Use the actual asset name shown for the release; the command above illustrates
the 1.0.0 Linux amd64 archive naming convention. This is an offline image import,
not a source build. Asset availability, architectures, SBOMs, manifests and test
receipts must be checked on the corresponding release. GitHub source archives
are source code and cannot be loaded with `docker load`. Source and image assets
must preserve the project's GPL license and contributor notices.

The initial SonarrWeeb 0.0.1 release provides an image archive for **Linux amd64** and
its matching source archive, SBOM, manifest and verification receipts. Its
immutable assets and dated reports remain historical checkpoints; they do not
establish that an equivalent 1.0.0 asset has been published or tested.

## Building from the GitHub source

To build the tagged source instead of pulling the published image:

```sh
git clone --branch v1.0.0 --depth 1 https://github.com/KvasExert/Tanukarr.git
cd Tanukarr
docker build --progress=plain -t tanukarr:1.0.0 .
```

Use `image: tanukarr:1.0.0` in the standalone Compose example above and run
`docker compose up -d --pull never`. The build downloads dependencies and runs
the checks encoded in the Dockerfile. Further test commands are in
[CONTRIBUTING.md](../CONTRIBUTING.md).

The `v1.0.0` checkout command requires that the release tag has been pushed.
Before that happens, clone `main` and check the repository's `VERSION` file.
An untagged main checkout can include changes added after a release, so record
the full Git commit when reproducing a build.

The repository's own `docker-compose.yml` remains a local-build deployment with
`tanukarr:local` and compatibility environment variables named
`SONARR_PRO_*`. In that checkout, `docker compose up -d --build` rebuilds from
source; it does not pull the GHCR release. Existing local users should preserve
their configuration mount and Compose project identity when changing images.

## Upgrading an existing SonarrWeeb installation

The repository and product are now named **Tanukarr**. New installations use
`ghcr.io/kvasexert/tanukarr:1.0.0`; old SonarrWeeb releases retain their original
`ghcr.io/kvasexert/sonarrweeb` image paths and asset filenames. A GitHub repository
rename does not rename an existing GHCR package or move the host's directories.

Stop the existing container and take a consistent backup first. Edit its image
to the new Tanukarr tag, while retaining the exact original configuration volume
or bind mount, media mounts, Compose project identity, port and authentication.
For example, an existing `sonarrweeb-config:/config` volume should remain mounted
under that name. Replacing it with `tanukarr-config` creates a new empty volume.
Container/service display names can change separately, but do not let Compose
silently select a different configuration volume.

The inherited Sonarr assembly and configuration names remain intentional:
`Sonarr`, `config.xml`, `sonarr.db` and container port 8989 do not change.
Existing database migrations, API keys and provider identities are preserved.
API clients can continue using `/api/v3` and `/api/v5`; `/api/v4` is not supported.
Both checked-in local Compose and the standalone release YAML resolve each
setting in this order: **`TANUKARR_*` → `SONARRWEEB_*` → `SONARR_PRO_*` → default**.
An unset or empty value falls through to the next name. This includes both
configuration and media paths; an old `.env` need not be renamed to preserve
its mounts. New installations should use `TANUKARR_*` names.

| Suffix after the prefix | Default | Purpose |
| --- | --- | --- |
| `BIND_ADDRESS` | `127.0.0.1` | Host address used for the published port |
| `PORT` | `6970` | Host port; container port remains 8989 |
| `PUID` | `1000` | Application UID |
| `PGID` | `1000` | Application GID |
| `UMASK` | `002` | File creation mask |
| `TZ` | `Etc/UTC` | Application timezone |
| `CONFIG_PATH` | `./config` | Configuration bind mount |
| `MEDIA_PATH` | `./media` | Shared library/download bind mount |

Existing `SonarrWeebBuild` WSL distributions and `SonarrWeebTools` host directories
can keep those names; renaming them is unnecessary to run Tanukarr. The launcher
accepts `TANUKARR_WSL_DISTRIBUTION` when a different configured distribution is
needed; it defaults to the existing `SonarrWeebBuild` distribution.

See [compatibility and backups](compatibility-and-backups.md) for ZIP restore,
the outstanding raw-XML restore limitation and PostgreSQL backup requirements.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Registry denies the pull | Correct owner/image/tag, finished publishing run, public package visibility or authorized GHCR login |
| `no matching manifest` | Published architecture versus the host's `docker info`; use a supported release/image archive |
| Port 6970 already in use | Change the host side only, for example `127.0.0.1:6971:8989`, and use that port in the browser |
| Media permission errors | Host directory ownership, PUID/PGID, group permissions, and the download client's UID/GID |
| Downloads cannot be imported | Shared mounts, reported paths, Remote Path Mapping and the common filesystem required for hardlinks |
| Invalid or rejected Host header | AllowedHosts and proxy hostname/URL-base settings |
| Configuration appears empty | Correct named volume or bind path and unchanged Compose project/volume name |

Use `docker compose logs` and the application logs for diagnosis, removing keys,
passwords, private account data and sensitive paths before sharing. Report this
release's problems in the [Tanukarr issue tracker](https://github.com/KvasExert/Tanukarr/issues).
For vulnerabilities, follow [SECURITY.md](../SECURITY.md). The
[container test guide](../docker/tests/README.md) describes tests with disposable
data, and the [documentation index](README.md) links the dated validation reports.
