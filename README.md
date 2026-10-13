# Tanukarr

<img src="frontend/src/Content/Images/logo.svg" alt="Tanukarr Tanuki logo" width="160" />

**Tanukarr 1.0.0** manages **TV series and anime** in one Sonarr-derived application.
It is the renamed continuation of **SonarrWeeb** and includes
selected-library Jellyfin refresh, AniList custom-list inclusion/exclusion,
shared AniList request pacing and bounded HTTP 429 recovery, and correct Nyaa
search encoding for titles containing reserved characters. It retains Sonarr
`develop` through `57816c149` (earlier integration checkpoints used `698bb1dbb`
and `59c22e859`), Kakarotto features, compatible
gaboy and ikatun features, AniList identity/list fixes and security hardening.
These are pinned Git snapshots, not a promise to track the latest upstream head.
Get the source from [KvasExert/Tanukarr](https://github.com/KvasExert/Tanukarr)
and read the [release notes for 1.0.0](docs/releases/1.0.0.md). Published versions
and assets are listed on the [GitHub releases page](https://github.com/KvasExert/Tanukarr/releases).
The [1.0.0 verification report](docs/releases/1.0.0-verification.md) records the
published image, checks, remaining findings and local deployment.
The [Docker installation guide](docs/docker-installation.md) explains GHCR pulls,
complete Compose YAML, configuration, permissions, authentication and updates.
See the [documentation index](docs/README.md),
[integration report](docs/integration-2026-10-12.md)
and the [ikatun feature and validation report](docs/ikatun-integration-2026-10-12.md).
The [security and functional report](docs/security-functional-validation-2026-10-12.md)
records the earlier verified Linux amd64 image, test results, remaining findings
and local Windows/WSL deployment. Dated reports retain their tested source and
image identities; they do not claim that later release packaging reran every
suite. The [initial Docker report](docs/docker-validation-2026-10-12.md) preserves
the first packaging checkpoint.

A fork of [Sonarr](https://github.com/Sonarr/Sonarr) for TV series and anime,
with a metadata provider selected per title, flexible episode orderings and
shared library, search, download and import tools in a single Docker image.

> **Unofficial.** Tanukarr is not affiliated with, endorsed by, or supported by the Sonarr project. Report Tanukarr problems in [this repository](https://github.com/KvasExert/Tanukarr/issues), rather than upstream Sonarr's issue tracker, forums or Discord.

## Contributors and AI attribution

This project builds on the Sonarr contributors' work and features from
[KakarottoCake](https://github.com/KakarottoCake/Sonarr-Pro),
[gaboy498-byte](https://github.com/gaboy498-byte/Sonarr-Pro) and
[ikatun](https://github.com/ikatun/Sonarr-Pro). The source commits credit
**Private Contributor**, **KakarottoCake**, **Matt Flowers**, **Ivo Katunaric** and
**Codex**, alongside upstream developers, translators, reviewers and automation.
Fork maintainers and individual implementation authors are credited separately.

The local integration, security fixes, verification and this documentation update
used **OpenAI Codex based on GPT-6**. Inherited Codex commits do not record an
exact model. The original Kakarotto README declares **Claude Opus 5**, while its
preserved trailers record **Claude Opus 4.8** and **Claude**; ikatun trailers
record **Claude Opus 5.5**. These historical labels are retained as source
attribution, rather than independently verified runtime model identities.

[CONTRIBUTORS.md](CONTRIBUTORS.md) maps the feature groups to original commits,
preserves the complete recorded Git author/co-author name list and explains model
provenance. [COPYRIGHT.md](COPYRIGHT.md) and [LICENSE.md](LICENSE.md) retain the
upstream legal notices. This fork accepts AI contributions under its own
[contribution policy](CONTRIBUTING.md).

---

## TV series and anime in one library

Tanukarr keeps Sonarr's shared library, monitoring, indexer, download-client and
import workflows for episodic TV and anime. Choose the catalogue that identifies
each title and check its numbering against the releases you intend to import.
TheTVDB and TMDB cover TV series, including anime listed in those catalogues;
AniList and MyAnimeList/Jikan provide anime-specific catalogues.

The metadata provider is selected for each series when adding it. Episode
ordering is a separate choice: TMDB-owned series can use available TMDB episode
groups. Use **Default** for TheTVDB, AniList and MyAnimeList owners; selecting a
TMDB group for those owners is not applied. The inherited IMDb choices are also
not applied by the current backend. IMDb is not a fifth metadata source. See the
[metadata provider guide](docs/metadata-providers.md) for selection, identity,
configuration and catalogue limits.

## What's added

**Four metadata sources, picked per series** — TheTVDB and TMDB for their TV
catalogues, plus AniList and MyAnimeList/Jikan for anime. Choose a source from the
dropdown above the Add New Series search box and confirm the matching entry.
The selected provider owns that series' metadata; sources are not automatically
merged. Provider setup and credential requirements are described in the
[metadata provider guide](docs/metadata-providers.md).

**TMDB episode groups** — for a TMDB-owned series, choose an available group
when adding it. TMDB's *Absolute* group produces continuous numbering in season
1; DVD, broadcast or other groups depend on what TMDB lists for that title.
This does not change the metadata owner. Non-TMDB owners should use **Default**;
IMDb options are present in inherited UI/data but are not functional ordering
choices in the current backend. See the [ordering limits](docs/metadata-providers.md#episode-ordering-is-a-separate-choice).

**Provider-specific catalogue entries** — a title or separate edition missing
from one catalogue can be added from another configured provider when that
provider lists it. Choose the entry that represents your TV series, anime,
special or recut; coverage and episode detail vary between catalogues.

**Absolute numbering that still matches season releases** — a series on ordinary season ordering accepts absolute-numbered releases too. `Tensei Shitara Slime Datta Ken - 88` resolves to S04E16.

**Multi-season packs** — `Series.S01-S05` is accepted and mapped to every season it contains, rather than refused. On by default.

**Complete-series search** — a dedicated interactive search in the series toolbar,
plus automatic pack-first searches for missing monitored episodes. Existing
complete seasons are skipped by Search Monitored and automatic RSS upgrades.
Scene/library numbering is checked before acquiring or importing ambiguous files;
manual episode choices remain available. Queue coverage follows each episode's
actual season. See the [ikatun integration report](docs/ikatun-integration-2026-10-12.md)
for scope, dry-run usage and validation.

**Fake release filtering** — the `.torrent` file is inspected before it reaches your download client, rejecting `Episode.mkv.exe`, bare executables and installers, and password-protected archives containing no video. Split RAR sets pass, because those are normal and legitimate. On by default, and every rejection is logged with a specific reason.

**Season pack trimming** — skip episodes you deliberately left unmonitored instead of importing the whole pack. Off by default.

**Alternative titles and season matching** — lookup handles season labels such
as `2nd Season`, `S2`, `Part 2` and `II`, and ranks titles using normalized names
and Jaro-Winkler scoring. Release matching can use alternative titles supplied
by the owning provider, including English and romaji names for anime; matching
still follows the configured series and episode numbering.

**AniList custom lists** — target your own custom lists alongside Planning and
Watching. Tanukarr 1.0.0 supports multiple included lists, excluded lists and
explicit exclusions for all six list-membership states. Matching any included
list is sufficient; exclusions take precedence even when an anime is Watching.
([Sonarr#6772](https://github.com/Sonarr/Sonarr/issues/6772))

AniList account-list membership (Watching/Planning/Completed/etc.), anime release
status (Releasing/Finished/etc.) and custom-list membership are separate filters.
All configured filters apply together. A native AniList item retains its own
provider ID throughout preview, synchronization, exclusions and cleanup; it is
not converted into a parent TVDB series. See [Library tools](docs/library-tools.md)
and the [integration report](docs/integration-2026-10-12.md).

**Library tools and retained versions** — combined path/library filters,
automation pause, import-list previews, search diagnostics, guarded restore,
scoped companion keys and per-show retained versions are described in
[Library tools](docs/library-tools.md). The optional host
[compression worker](docker/compression/README.md) has separate dependencies and
filesystem requirements; it is not installed inside the main Sonarr image.

**Jellyfin library refresh** — a dedicated Connect integration
selects one library and refreshes it after imports, at configurable intervals
(minimum 1 minute, default 60), or both. Idle intervals make no requests; pending
imports survive restarts and failed requests are retried. See the
[Jellyfin guide](docs/jellyfin.md). New libraries must first complete an initial
scan in Jellyfin; pending imports are retained until every configured folder is
indexed. This connection was introduced in SonarrWeeb 0.1.0 and is retained in
Tanukarr 1.0.0.

Full detail, including the reasoning and the trade-offs behind each, is in [FORK.md](FORK.md).

## What's removed

Tanukarr doesn't contact Sonarr's servers for updates, crash reports, or announcements. Those endpoints describe upstream Sonarr builds, so an update offered there would replace this program with a different one — and the Sonarr team shouldn't receive crash reports or install metrics for a fork they don't maintain. Updates happen through Docker instead.

## A word on Usenet

NZBGet, SABnzbd and NZBVortex remain available. Complete-series searches target
torrent indexers, and fake file filtering requires a `.torrent`. Shared queue,
numbering and automatic-search rules also apply to Usenet releases. Pack imports
have only been exercised with torrent fixtures; live Usenet downloads have not
been validated for this integration.

---

## Getting started

You need [Docker](https://docs.docker.com/get-docker/) with Linux containers and
Docker Compose. The [installation guide](docs/docker-installation.md) contains
the complete YAML and named-volume/bind-mount alternatives.

### Quick start

```bash
docker pull ghcr.io/kvasexert/tanukarr:1.0.0
docker run -d --name tanukarr \
  -p 127.0.0.1:6970:8989 \
  -e PUID=1000 -e PGID=1000 -e TZ=Etc/UTC -e UMASK=002 \
  -v tanukarr-config:/config -v /path/to/media:/media \
  --restart unless-stopped ghcr.io/kvasexert/tanukarr:1.0.0
```

Replace `/path/to/media` with your actual writable media/download parent and
adjust PUID/PGID to the host user's IDs. Then open **http://localhost:6970** and
configure authentication. Upstream Sonarr can keep using host port 8989.
Use the release tag after its publishing workflow has completed; a version
change in the source does not establish that the GHCR image is available.

### Or with Compose

Save the standalone `compose.yaml` from the
[installation guide](docs/docker-installation.md#docker-compose-named-configuration-volume)
and run:

```bash
docker compose config
docker compose pull
docker compose up -d
```

The example maps `127.0.0.1:6970` to container port 8989. Keep this Compose project
and `/config` separate from other Sonarr instances, and give each instance its
own download category. A public GitHub repository and a public GHCR package are
separate settings; see the guide if the registry denies the pull.

### Updating

```bash
docker compose pull
docker compose up -d
```

Back up `/config` consistently first and change the image tag or digest in your
YAML to the desired release. Recreating the container uses the new image while
retaining the configuration volume. Restarting alone keeps the old image.
Database migrations may require a pre-update backup to roll back. See
[version pins and updates](docs/docker-installation.md#version-pins-and-updates).

### Settings that matter

| Variable | Default | What it does |
|---|---|---|
| `PUID` / `PGID` | `1000` | UID/GID used by the application. Match the host's media owner/group and grant access to mounted directories. |
| `TZ` | `Etc/UTC` | Timezone, so scheduled tasks and log timestamps match your clock. |
| `UMASK` | `002` | File creation mask. Compose resolves `TANUKARR_UMASK`, then legacy `SONARRWEEB_UMASK` / `SONARR_PRO_UMASK`; the entrypoint also accepts legacy `UMASK_SET` when `UMASK` is omitted. |

Mount completed downloads and the library under a single `/media` parent on the
same underlying filesystem. That arrangement allows hardlinks and efficient
moves when the filesystem and permissions support them. Matching container paths
also avoids unnecessary Remote Path Mappings.

### Publishing workflow tags

The release image path is `ghcr.io/kvasexert/tanukarr`. Check the
[package page](https://github.com/users/KvasExert/packages/container/package/tanukarr)
and release notes for available tags, architectures and digests.

| Tag | What you get |
|---|---|
| `1.0.0` | Tanukarr branding, Jellyfin refresh, AniList include/exclude controls and request-limit recovery, and corrected Nyaa query encoding; use after publication completes. |
| `latest` | The newest published release; it can change when a later version is released. |
| `edge` | Every commit to `main` after workflow tests and the container smoke test. Development builds may still change behavior. |
| `sha-<full commit>` | Build tag identifying one source commit; use a digest to pin the exact image. |

The current release workflow publishes **`linux/amd64`**. Dockerfile support for
another architecture does not establish a published or tested image; additional
architectures require their own verified artifacts. Release notes identify the
actual image architecture, digest and executed checks.

Historical SonarrWeeb 0.0.1 and 0.1.0 assets keep their original image namespace
`ghcr.io/kvasexert/sonarrweeb`, filenames and immutable tags. The stable 0.1.0
image predates the AniList request-limit and Nyaa encoding fixes.

### Coming from Sonarr

Stop the source instance for a consistent backup and **copy** its configuration
into a separate Tanukarr configuration location. Never share a live `/config`
between instances. First-start migrations can make the copy incompatible with
upstream Sonarr; keep the original backup and check your client/library settings.

Existing SonarrWeeb users should change the image while preserving their original
configuration mount and Compose project/volume identity. A new `tanukarr-config`
volume starts an empty instance; the project rename does not move existing data.
Tanukarr preserves Sonarr's core assembly names, `config.xml`, `sonarr.db` and
container port 8989. **API v3 and v5 remain supported; v4 is not implemented.**
See [compatibility and backups](docs/compatibility-and-backups.md) for migration,
tested ZIP restore behavior and the outstanding raw-XML restore limitation.

### Building locally

The checked-in `docker-compose.yml` builds `tanukarr:local` from this working
tree with a separate local configuration directory. `TANUKARR_*` variables take
precedence over legacy `SONARRWEEB_*` and `SONARR_PRO_*` values, including config
and media paths. The service/container name `sonarr-pro` and existing mounts are
retained for compatibility with local deployments:

```sh
mkdir -p media
docker compose up -d --build
```

That command builds source instead of pulling GHCR. See
[building from the GitHub source](docs/docker-installation.md#building-from-the-github-source)
and [container checks](docker/tests/README.md). The optional
[Windows/WSL launcher](docker/start-wsl.ps1) expects an already configured
`SonarrWeebBuild` distribution and Docker Engine; Docker Desktop is an alternative.

---

## First-run setup

**Choose metadata per title.** The Add New Series source selector offers
TheTVDB, TMDB, AniList and MyAnimeList. TheTVDB/TMDB supply TV catalogue entries,
including anime they list; AniList/MyAnimeList supply anime entries. Configure
TMDB's v3 API key under **Settings → Metadata Source** when using TMDB metadata
or episode groups. Public AniList/Jikan lookup and authenticated AniList list
imports have different requirements; see [Metadata providers](docs/metadata-providers.md).

**Check ordering support before adding a series.** TMDB episode groups apply
to TMDB-owned entries. Use **Default** for TheTVDB/AniList/MyAnimeList entries.
The inherited **Refresh Imdb Dataset** task can build its local index and expose
choices, but the current backend does not apply IMDb ordering; building that
index does not make the option functional.

One thing worth knowing before you add a long series: **the episode ordering is fixed when the series is added.** Season and episode numbers go into file and folder names, so changing it afterwards would rename your library on disk. To change it, remove the series and add it again.

---

## Contributing

Contributions are welcome, **including AI-assisted ones**. This fork has its own
policy; see [CONTRIBUTING.md](CONTRIBUTING.md) for tools, tests, documentation and
source/model attribution requirements.

What's asked instead:

- **Explain the reasoning, not just the change.** A PR that says what broke and why this is the right fix is worth several that only say what changed.
- **Say what you actually verified.** "Tests pass, ran it against a real library" and "compiles, untested" are both fine answers. Claiming the first while meaning the second is not.
- Run the solution build, relevant backend tests and all frontend checks listed
  in [CONTRIBUTING.md](CONTRIBUTING.md). Document skipped or unavailable checks.

Suggestions and bug reports are welcome — [open a Tanukarr issue](https://github.com/KvasExert/Tanukarr/issues).

### Building locally

```bash
dotnet build src/Sonarr.sln -c Debug
```

Build the **solution**, not individual project files. `Directory.Build.props` loads `stylecop.json` through `$(SolutionDir)`, which is only defined for a solution build — building a bare `.csproj` silently drops the StyleCop config and then fails on every `using` directive in the repo.

```bash
yarn install --frozen-lockfile
yarn typecheck
yarn build
```

The frontend uses Vite and Node 24. `yarn build` creates the production bundle;
the old webpack `--env production` argument is no longer supported.

---

## License

[GPL-3.0](LICENSE.md), the same as upstream Sonarr.

Tanukarr is a derivative work of [Sonarr](https://github.com/Sonarr/Sonarr) and the
credited Sonarr Pro forks. The Sonarr contributors' copyright notices are retained.
Credit for the upstream application belongs to its contributors; the feature and
integration credits identify the adapted and added work. Tanuki branding uses the
user-supplied `Tanuki_Logo.zip` artwork; its artist and license were not identified
in this session. The archive is preserved in
[resources/branding](resources/branding/Tanuki_Logo.zip), with the reproducible
[asset helper](docker/branding/update-assets.ps1).
The [contributor register](CONTRIBUTORS.md) preserves the additional fork and
integration credits; third-party components retain their own notices and licenses.
