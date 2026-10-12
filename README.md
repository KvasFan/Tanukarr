# SonarrWeeb

**Release 0.0.1** brings together Sonarr `develop` through `57816c149` (earlier
integration checkpoints used `698bb1dbb` and `59c22e859`), Kakarotto features, compatible
gaboy and ikatun features, AniList identity/list fixes and security hardening.
These are pinned Git snapshots, not a promise to track the latest upstream head.
Get the source and release assets from
[KvasExert/SonarrWeeb](https://github.com/KvasExert/SonarrWeeb) and
[release v0.0.1](https://github.com/KvasExert/SonarrWeeb/releases/tag/v0.0.1).
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

A fork of [Sonarr](https://github.com/Sonarr/Sonarr) that adds the metadata and downloading features long-running and anime libraries need, and ships as a single Docker image.

> **Unofficial.** SonarrWeeb is not affiliated with, endorsed by, or supported by the Sonarr project. Report SonarrWeeb problems in [this repository](https://github.com/KvasExert/SonarrWeeb/issues), rather than upstream Sonarr's issue tracker, forums or Discord.

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

## Why this exists

Sonarr resolves every series through TheTVDB. That works well for weekly Western television and poorly for everything else — a 1,100-episode anime becomes twenty-odd invented seasons, a recut that TheTVDB folds into its parent can't be added at all, and an `S01-S05` pack is rejected outright.

SonarrWeeb retains the upstream application and adds ways to handle those cases.

## What's added

**Four metadata sources, picked per series** — TheTVDB, TMDB, AniList, and MyAnimeList (via Jikan), chosen from a dropdown above the search box when adding a series. Only TMDB needs a key; the rest need no account.

**Selectable episode orderings** — TMDB publishes alternate numbering schemes per series, and IMDb's dumps carry another. Where a series has more than one, you pick at add time. TMDB's *Absolute* order produces continuous numbering in season 1; episode counts depend on the provider's current catalogue. DVD order, story arcs and streaming orders use the same selection. IMDb supplies ordering data, not an additional series metadata owner.

**Series TheTVDB won't list separately** — recuts and spin-offs that TheTVDB folds into a parent series can be added from TMDB or AniList. Upstream can't add these at all.

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

**Anime-aware search matching** — `2nd Season`, `S2`, `Part 2` and `II` all read as "season 2"; accents and articles are stripped; scoring uses Jaro-Winkler rather than upstream's single-character tolerance. Release-to-series matching also compares against every title a provider knows, so a release using a series' English title still matches when it's stored under the romaji one.

**AniList custom lists** — import lists can target one of your own custom lists instead of only Planning and Watching. ([Sonarr#6772](https://github.com/Sonarr/Sonarr/issues/6772))

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

**Jellyfin library refresh (development main)** — a dedicated Connect integration
selects one library and refreshes it after imports, at configurable intervals
(minimum 1 minute, default 60), or both. Idle intervals make no requests; pending
imports survive restarts and failed requests are retried. See the
[Jellyfin guide](docs/jellyfin.md). This addition follows the published 0.0.1 release.

Full detail, including the reasoning and the trade-offs behind each, is in [FORK.md](FORK.md).

## What's removed

SonarrWeeb doesn't contact Sonarr's servers for updates, crash reports, or announcements. Those endpoints describe upstream Sonarr builds, so an update offered there would replace this program with a different one — and the Sonarr team shouldn't receive crash reports or install metrics for a fork they don't maintain. Updates happen through Docker instead.

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
docker pull ghcr.io/kvasexert/sonarrweeb:0.0.1
docker run -d --name sonarrweeb \
  -p 127.0.0.1:6970:8989 \
  -e PUID=1000 -e PGID=1000 -e TZ=Etc/UTC -e UMASK=002 \
  -v sonarrweeb-config:/config -v /path/to/media:/media \
  --restart unless-stopped ghcr.io/kvasexert/sonarrweeb:0.0.1
```

Replace `/path/to/media` with your actual writable media/download parent and
adjust PUID/PGID to the host user's IDs. Then open **http://localhost:6970** and
configure authentication. Upstream Sonarr can keep using host port 8989.

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
| `UMASK` | `002` | File creation mask. Compose reads `SONARR_PRO_UMASK`; the entrypoint also accepts legacy `UMASK_SET` when `UMASK` is omitted. |

Mount completed downloads and the library under a single `/media` parent on the
same underlying filesystem. That arrangement allows hardlinks and efficient
moves when the filesystem and permissions support them. Matching container paths
also avoids unnecessary Remote Path Mappings.

### Publishing workflow tags

The release image path is `ghcr.io/kvasexert/sonarrweeb`. Check the
[package page](https://github.com/users/KvasExert/packages/container/package/sonarrweeb)
and release notes for available tags, architectures and digests.

| Tag | What you get |
|---|---|
| `0.0.1` | This specific SonarrWeeb release; recommended for the initial installation. |
| `latest` | The newest published release; it can change when a later version is released. |
| `edge` | Every commit to `main` after workflow tests and the container smoke test. Development builds may still change behavior. |
| `sha-<full commit>` | Build tag identifying one source commit; use a digest to pin the exact image. |

The inherited release workflow can build `linux/amd64` and `linux/arm64`. Published
architecture availability and executed validation belong to each release's notes;
earlier local runtime verification covered `linux/amd64`.

### Coming from Sonarr

Stop the source instance for a consistent backup and **copy** its configuration
into a separate SonarrWeeb configuration location. Never share a live `/config`
between instances. First-start migrations can make the copy incompatible with
upstream Sonarr; keep the original backup and check your client/library settings.

### Building locally

The checked-in `docker-compose.yml` builds `sonarr-weeb:local` from this working
tree with a separate local configuration directory. Its `SONARR_PRO_*` variables
remain for compatibility with existing local deployments:

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

**Add a TMDB API key** if you want TMDB metadata or episode groups — Settings → Metadata Source. It's free from [themoviedb.org](https://www.themoviedb.org/settings/api). Nothing else needs a key, and the other three sources work without it.

**Build the IMDb index** if you want IMDb orderings — System → Tasks → *Refresh Imdb Dataset*. It downloads the published episode dataset and builds a local index; dataset and index sizes vary. After initialization it refreshes monthly. Until it runs, IMDb does not appear in the ordering dropdown.

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

Suggestions and bug reports are welcome — [open a SonarrWeeb issue](https://github.com/KvasExert/SonarrWeeb/issues).

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

SonarrWeeb is a derivative work of [Sonarr](https://github.com/Sonarr/Sonarr) and the
credited Sonarr Pro forks. The Sonarr contributors' copyright notices are retained.
Credit for the upstream application belongs to its contributors; the feature and
integration credits identify the adapted and added work.
The [contributor register](CONTRIBUTORS.md) preserves the additional fork and
integration credits; third-party components retain their own notices and licenses.
