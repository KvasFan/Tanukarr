# Tanukarr

<p align="center">
  <img src="Logo/800.png" alt="Tanukarr Tanuki logo from the supplied Tanuki_Logo.zip" width="200" />
</p>

**Tanukarr 0.1.3** manages TV series, anime
and movie collections in one Sonarr-derived application. It includes
graphical account creation, collection and quality
fixes, seven configurable metadata catalogues, native films and IMDb lookup.
Existing list filtering,
download-client, import, backup and Jellyfin refresh/watched features are retained.
0.1.1 fixed pending-release removal through the v3 and v5 queue APIs; 0.1.2 is
the first release published from the `KvasFan/Tanukarr` repository and
`ghcr.io/kvasfan/tanukarr` after the previous namespace became unavailable;
0.1.3 loads collection covers in the library grid, shows collection-search
progress and makes Quality page saves fast on large libraries.
Links into the previous namespace, including those in historical reports,
no longer resolve. The repository is also published at
[gitlab.com/tirael8308/Tanukarr](https://gitlab.com/tirael8308/Tanukarr),
which receives the same commits, tags and release assets.

See the [0.1.3 release notes](docs/releases/0.1.3.md) and
[verification ledger](docs/releases/0.1.3-verification.md), the
[0.1.2 release notes](docs/releases/0.1.2.md),
[publication report](docs/releases/0.1.2-publication.md) and
[verification ledger](docs/releases/0.1.2-verification.md), then the
[0.1.1 release notes](docs/releases/0.1.1.md),
[publication report](docs/releases/0.1.1-publication.md) and
[verification ledger](docs/releases/0.1.1-verification.md) for executed checks
and exact source/image identities. The [0.1.0 release notes](docs/releases/0.1.0.md),
[publication report](docs/releases/0.1.0-publication.md) and
[preparation ledger](docs/releases/0.1.0-verification.md) retain that release's
evidence and remaining
findings. The [1.1.0 publication](docs/releases/1.1.0-publication.md)
retains the base release's original evidence. The [upstream review](docs/upstream-audit-2026-10-14.md)
records the latest fetched Sonarr, Kakarotto, gaboy and ikatun heads and the
compatibility decisions. Source attribution remains in [CONTRIBUTORS.md](CONTRIBUTORS.md).

Use the [Docker installation guide](docs/docker-installation.md),
[collection guide](docs/anime-season-groups.md), [quality guide](docs/quality-and-codec-size.md)
and [metadata guide](docs/metadata-providers.md) to configure the application.
The [documentation index](docs/README.md) links all maintained guides and dated
reports. Published assets are listed on the [GitHub releases page](https://github.com/KvasFan/Tanukarr/releases).
The [1.0.5 publication report](docs/releases/1.0.5-publication.md) preserves the
earlier release's exact source, image, hosted tests and protected deployment
evidence. Historical reports retain their original tested scope.

A fork of [Sonarr](https://github.com/Sonarr/Sonarr) for TV series, anime and films,
with a metadata provider selected per title, flexible episode orderings and
shared library, search, download and import tools in a single Docker image.

> **Unofficial.** Tanukarr is not affiliated with, endorsed by, or supported by the Sonarr project. Report Tanukarr problems in [this repository](https://github.com/KvasFan/Tanukarr/issues), rather than upstream Sonarr's issue tracker, forums or Discord.

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

## TV series, anime and films in one library

Tanukarr keeps Sonarr's shared library, monitoring, indexer, download-client and
import workflows for episodic TV and anime, with native movie search and import.
Choose the catalogue that identifies
each title and check its numbering against the releases you intend to import.
TheTVDB and TMDB cover TV series, including anime listed in those catalogues;
AniList and MyAnimeList/Jikan provide anime-specific catalogues. TVmaze provides
another TV catalogue. TMDB Movies and OMDb identify films, including IMDb-ID
lookups through their official APIs.

The metadata provider is selected for each series when adding it. Episode
ordering is a separate choice: TMDB-owned series can use available TMDB episode
groups. Use **Default** for TheTVDB, AniList and MyAnimeList owners; selecting a
TMDB group for those owners is not applied. The inherited IMDb choices are also
not applied by the current backend. IMDb IDs for films are resolved by TMDB
Movies or OMDb; the application does not scrape IMDb. See the
[metadata provider guide](docs/metadata-providers.md) for selection, identity,
configuration and catalogue limits.

## What's added

**Seven metadata catalogues, picked per title** — TheTVDB, TMDB TV and TVmaze
for TV; AniList and MyAnimeList/Jikan for anime; TMDB Movies and OMDb for films.
Enable sources and choose their order in Settings → Metadata Source, then select
a catalogue or search all enabled catalogues when adding a title.
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

**Series, anime and movie groups** — AniList/MyAnimeList entries can use
official linear prequel/sequel relations, manual season/movie assignments or
Disabled mode. Related members appear together in library views. Newly
generated paths use configurable season, strict story-arc or movie leaves under
one canonical group. Movie-only sagas can use `Film NN`. Manual grouping is
available for all catalogue owners. Existing paths and native
provider episode numbering remain unchanged. See the
[grouping guide](docs/anime-season-groups.md) for supported formats, ambiguity,
individual actions and media-server numbering limits.

**Absolute numbering that still matches season releases** — a series on ordinary season ordering accepts absolute-numbered releases too. `Tensei Shitara Slime Datta Ken - 88` resolves to S04E16.

**Multi-season packs** — `Series.S01-S05` is accepted and mapped to every season it contains, rather than refused. On by default.

**Complete-series search** — a dedicated interactive search in the series toolbar,
plus automatic pack-first searches for missing monitored episodes. Existing
complete seasons are skipped by Search Monitored and automatic RSS upgrades.
Scene/library numbering is checked before acquiring or importing ambiguous files;
manual episode choices remain available. Queue coverage follows each episode's
actual season. See the [ikatun integration report](docs/ikatun-integration-2026-10-12.md)
for scope, dry-run usage and validation.

**Interactive season packs** — search pack-only results from a
native season or grouped season panel. The request uses that member's native
series ID and season number, while the usual interactive search retains episode
results. Searching does not acquire a release.

**Fake release filtering** — the `.torrent` file is inspected before it reaches your download client, rejecting `Episode.mkv.exe`, bare executables and installers, and password-protected archives containing no video. Split RAR sets pass, because those are normal and legitimate. On by default, and every rejection is logged with a specific reason.

**Season pack trimming** — skip episodes you deliberately left unmonitored instead of importing the whole pack. Off by default.

**Alternative titles and season matching** — lookup handles season labels such
as `2nd Season`, `S2`, `Part 2` and `II`, and ranks titles using normalized names
and Jaro-Winkler scoring. Release matching can use alternative titles supplied
by the owning provider, including English and romaji names for anime; matching
still follows the configured series and episode numbering.

**Complete list snapshots and provider choice** — version 1.0.5
reads complete eligible account/list data before applying each import
definition's filters locally. Reuse is limited to matching provider, account,
grant and request scope during one operation; temporary private snapshots are
released afterward. Saved membership and SIMKL's incremental baseline remain
necessary. Public metadata and relations outside the collection may still need
requests and remain subject to provider quotas. See the
[snapshot and identity guide](docs/import-list-snapshots.md) and
[provider capability matrix](docs/library-tools.md#provider-capabilities).

**Metadata provider for new series** defaults to **Automatic**. An explicit
catalogue is used only when the list supplies its positive ID; an unavailable
ID keeps the existing automatic fallback. Existing series retain their source,
numbering and paths. AniList and MyAnimeList may still need an internal negative
TVDB slot while retaining their real native IDs. The field does not manufacture
cross-catalogue mappings or turn an AniList number into a TheTVDB number.

**AniList custom lists** — target your own custom lists alongside Planning and
Watching. Tanukarr supports multiple included lists, excluded lists and
the existing Import checkboxes for account-list and media-release states.
Matching any included custom list selects a candidate; matching any excluded
custom list always removes it, including overlaps. Account-list and media-release
Import filters still apply. Empty included names select all otherwise eligible
entries before exclusions.
([Sonarr#6772](https://github.com/Sonarr/Sonarr/issues/6772))

Version 1.0.2 removes the six separate Exclude Watching/Planning/Completed/
Dropped/Paused/Repeating controls and their behavior. Select states through the
Import checkboxes and exclude entries through custom lists. This simplification
follows the immutable 1.0.1 release, whose original settings remain unchanged.
The duplicate single-list textbox is also removed: an older saved custom-list
name becomes an **Included Custom Lists** tag, so its filter stays visible and
can be edited there. The [settings cleanup checkpoint](docs/anilist-settings-cleanup-2026-10-13.md)
records this later source change and its verification scope.
The earlier [membership and priority checkpoint](docs/anilist-membership-priority-2026-10-13.md)
records an intermediate inclusion-first development rule. The final
exclusion-veto rule remains. The historical
[1.0.2 report](docs/releases/1.0.2-verification.md) records the previous paged
membership-recovery implementation; 1.0.5 instead starts with the complete
selected user's anime collection, including custom groups. Incomplete or
ambiguous collection data retains previous membership.

Grouped AniList settings and **Load custom lists** retain name autocomplete,
literal manual names and OAuth. Loading names or testing unsaved settings does
not save or sync the connection. Their temporary scope is separate from a
saved synchronization and its private snapshots.

Version 1.0.1 includes progress-preserving metadata
imports, eligible saved-list retries on the normal five-minute task and bounded
public metadata/relation reuse. It retains the existing exclusion safeguards;
the immutable 1.0.0 release remains unchanged. See the
[reliability checkpoint](docs/anilist-react-reliability-2026-10-13.md) for executed
local checks and publication status, and [Library tools](docs/library-tools.md#pending-additions-and-the-development-reliability-update)
for the retry conditions.

AniList account-list membership (Watching/Planning/Completed/etc.), anime release
status (Releasing/Finished/etc.) and custom-list membership are separate filters.
All configured filters apply together. A native AniList item retains its own
provider ID throughout preview, synchronization, exclusions and cleanup; it is
not converted into a parent TVDB series. See [Library tools](docs/library-tools.md)
and the [integration report](docs/integration-2026-10-12.md).

**Other list providers** — TMDB List, Trakt List and MDBList can combine several
included sources and subtract excluded sources; at least one included source is
required. MyAnimeList account imports use the official MAL API, registered
Client ID/public username or direct OAuth, account states, minimum score, airing
states and exact list tags. Matching an excluded tag always removes an entry.
Jikan remains a separate public catalogue metadata provider. The
[provider capability matrix](docs/library-tools.md#provider-capabilities)
describes supported selectors, credentials and legacy-setting preservation.

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
indexed. This connection was introduced in historical 0.1.0 and is retained in
Tanukarr 1.0.4.

**Jellyfin watched status and list progress** — an optional reader records one
selected user's played episodes by matching registered physical file paths.
Separate opt-ins can unmonitor watched episodes, restore only monitoring changed
by this connection, and advance compatible own-account AniList/MAL/Trakt/SIMKL
progress. Public/foreign lists stay read-only. See the
[watched-state guide](docs/jellyfin-watched.md) for setup and limitations.

**Successful backup timing** — manual backups update Last Execution and restart
the configured next-backup interval. Failed backups retain the last successful
timing and their command error. See [compatibility and backups](docs/compatibility-and-backups.md).

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
docker pull ghcr.io/kvasfan/tanukarr:0.1.3
docker run -d --name tanukarr \
  -p 127.0.0.1:6970:8989 \
  -e PUID=1000 -e PGID=1000 -e TZ=Etc/UTC -e UMASK=002 \
  -v tanukarr-config:/config -v /path/to/media:/media \
  --restart unless-stopped ghcr.io/kvasfan/tanukarr:0.1.3
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
| `UMASK` | `002` | File creation mask. Compose resolves `TANUKARR_UMASK`, with compatibility fallbacks for older installations; the entrypoint also accepts legacy `UMASK_SET` when `UMASK` is omitted. |

Mount completed downloads and the library under a single `/media` parent on the
same underlying filesystem. That arrangement allows hardlinks and efficient
moves when the filesystem and permissions support them. Matching container paths
also avoids unnecessary Remote Path Mappings.

### Publishing workflow tags

The release image path is `ghcr.io/kvasfan/tanukarr`. Check the
[package page](https://github.com/users/KvasFan/packages/container/package/tanukarr)
and release notes for available tags, architectures and digests.

| Tag | What you get |
|---|---|
| `0.1.3` | Library-screen fixes: collection covers load in the virtualized library grid, collection search shows progress, Quality page saves publish one profile-batch event (about 3 s instead of 17 s on a large library) and keep edits made during a save. The release tag runs the light gate profile on code that passed the full profile on `main`; see the 0.1.3 release notes. |
| `0.1.2` | First release from `KvasFan/Tanukarr`: the 0.1.1 application with repository, package and logo links pointing at the new namespace, and a release workflow that tolerates a missing earlier image on main builds only. Executed checks are recorded in the 0.1.2 verification ledger. |
| `0.1.1` | Maintenance release of 0.1.0: v3 pending-queue removal, immediate pending-list refresh after removals, integration-client serialization and cross-published Dockerfile stages. Published under the previous namespace, which is no longer reachable; its executed checks are recorded in the 0.1.1 verification ledger. |
| `0.1.0` | Graphical first-run setup, collection/quality fixes, ordered metadata sources and TVmaze. Publication and exact checks are recorded in the verification ledger. |
| `1.1.0` | Published and locally deployed: full collection pages, alternate artwork, custom qualities and editable profile codec factors. Nineteen runtime gates passed (342 checks); public assets/image and the protected upgrade verified. See the publication report for remaining findings. |
| `1.0.5` | Published and deployed: complete scoped list snapshots, local filtering and native metadata-provider preference. All fourteen hosted gates passed (251 checks); public assets/image and the protected upgrade verified. |
| `1.0.4` | Published and deployed: optional Jellyfin watched state, guarded source-account progress and corrected manual backup timing. All thirteen hosted gates passed; source/assets, immutable image and protected-data preservation verified. |
| `1.0.3` | Published hourly quality-size editors, mixed profile summaries, validated partial global updates and heuristic codec-normalized size decisions; exact checks/digest are in the publication follow-up. |
| `1.0.2` | Published list-control/provider checkpoint: exclusion vetoes, complete AniList membership recovery, combined TMDB/Trakt/MDBList sources and direct MAL account status/tag filters. |
| `1.0.1` | Historical grouping, season-pack searches, metadata retry/cache improvements, covered-library React correction and safe group-slot allocation; retains its original list controls. |
| `1.0.0` | Historical first Tanukarr release with Jellyfin refresh, AniList include/exclude controls and request-limit recovery, and corrected Nyaa query encoding. |
| `latest` | The newest published release; it can change when a later version is released. |
| `edge` | Every commit to `main` after workflow tests and the container smoke test. Development builds may still change behavior. |
| `sha-<full commit>` | Build tag identifying one source commit; use a digest to pin the exact image. |

The current release workflow publishes **`linux/amd64`**. Dockerfile support for
another architecture does not establish a published or tested image; additional
architectures require their own verified artifacts. Release notes identify the
actual image architecture, digest and executed checks.

Historical [0.0.1](https://github.com/KvasFan/Tanukarr/releases/tag/v0.0.1)
and [0.1.0](https://github.com/KvasFan/Tanukarr/releases/tag/v0.1.0) assets
retain their original namespaces, filenames and immutable tags. Obtain their
exact image references from those releases' manifests. The 0.1.0 image predates
the AniList request-limit and Nyaa encoding fixes.

### Coming from Sonarr

Stop the source instance for a consistent backup and **copy** its configuration
into a separate Tanukarr configuration location. Never share a live `/config`
between instances. First-start migrations can make the copy incompatible with
upstream Sonarr; keep the original backup and check your client/library settings.

Existing Tanukarr users should change the image while preserving their original
configuration mount and Compose project/volume identity. A new `tanukarr-config`
volume starts an empty instance; the project rename does not move existing data.
Tanukarr preserves Sonarr's core assembly names, `config.xml`, `sonarr.db` and
container port 8989. **API v3 and v5 remain supported; v4 is not implemented.**
See [compatibility and backups](docs/compatibility-and-backups.md) for migration,
tested ZIP restore behavior and the outstanding raw-XML restore limitation.

### Building locally

The checked-in `docker-compose.yml` builds `tanukarr:local` from this working
tree with a separate local configuration directory. Use `TANUKARR_*` variables
for configuration and media paths. Older environment aliases remain accepted
for existing installations. The service/container name `sonarr-pro` and existing mounts are
retained for compatibility with local deployments:

```sh
mkdir -p media
docker compose up -d --build
```

That command builds source instead of pulling GHCR. See
[building from the GitHub source](docs/docker-installation.md#building-from-the-github-source)
and [container checks](docker/tests/README.md). The optional
[Windows/WSL launcher](docker/start-wsl.ps1) expects a configured
WSL distribution and Docker Engine. Set `TANUKARR_WSL_DISTRIBUTION`
to its actual name; Docker Desktop is an alternative.

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

Suggestions and bug reports are welcome — [open a Tanukarr issue](https://github.com/KvasFan/Tanukarr/issues).

### Building locally

```bash
dotnet build src/Sonarr.sln -c Debug
```

Build the **solution**, not individual project files. `Directory.Build.props` loads `stylecop.json` through `$(SolutionDir)`, which is only defined for a solution build — building a bare `.csproj` silently drops the StyleCop config and then fails on every `using` directive in the repo.

```bash
yarn install --immutable
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
