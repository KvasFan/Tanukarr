# Sonarr Pro

This local integration includes Sonarr `v5-develop` through `59c22e859` (the
initial integration checkpoint used `698bb1dbb`), Kakarotto features, compatible
gaboy and ikatun features, AniList identity/list fixes and security hardening.
These are pinned Git snapshots, not a promise to track the latest upstream head.
See the [documentation index](docs/README.md),
[integration report](docs/integration-2026-10-12.md)
and the [ikatun feature and validation report](docs/ikatun-integration-2026-10-12.md).
The included Compose file builds `sonarr-weeb:local` from this working tree.
The [security and functional report](docs/security-functional-validation-2026-10-12.md)
records the final verified Linux amd64 image, test results, remaining findings and
local Windows/WSL deployment. The [initial Docker report](docs/docker-validation-2026-10-12.md)
preserves the earlier packaging checkpoint.

A fork of [Sonarr](https://github.com/Sonarr/Sonarr) that adds the metadata and downloading features long-running and anime libraries need, and ships as a single Docker image.

> **Unofficial.** Sonarr Pro is not affiliated with, endorsed by, or supported by the Sonarr project. Please don't raise Sonarr Pro problems on Sonarr's issue tracker, forums, or Discord — they can't help with code they didn't write. Report them [here](https://github.com/KakarottoCake/Sonarr-Pro/issues) instead.

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

Sonarr Pro keeps everything upstream does and adds a way around each of those.

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

Full detail, including the reasoning and the trade-offs behind each, is in [FORK.md](FORK.md).

## What's removed

Sonarr Pro doesn't contact Sonarr's servers for updates, crash reports, or announcements. Those endpoints describe upstream Sonarr builds, so an update offered there would replace this program with a different one — and the Sonarr team shouldn't receive crash reports or install metrics for a fork they don't maintain. Updates happen through Docker instead.

## A word on Usenet

NZBGet, SABnzbd and NZBVortex remain available. Complete-series searches target
torrent indexers, and fake file filtering requires a `.torrent`. Shared queue,
numbering and automatic-search rules also apply to Usenet releases. Pack imports
have only been exercised with torrent fixtures; live Usenet downloads have not
been validated for this integration.

---

## Getting started

You need [Docker](https://docs.docker.com/get-docker/). That's the only prerequisite.

### Quick start

```bash
docker build -t sonarr-weeb:local .
docker run -d --name sonarr-pro -p 127.0.0.1:6970:8989 -e PUID=1000 -e PGID=1000 -e TZ=Etc/UTC -v sonarr-pro-config:/config -v /path/to/media:/media --restart unless-stopped sonarr-weeb:local
```

Then open **http://localhost:6970**. Upstream Sonarr can keep using port 8989.

### Or with Compose

From this repository, create the empty local media directory and run:

```bash
mkdir -p media
docker compose up -d --build
```

Compose also defaults to port 6970; set `SONARR_PRO_PORT` to change it. Keep this
Compose project and its `/config` directory separate from upstream Sonarr. To start
with an empty library, copy only download client settings, root folders and remote
path mappings, and give each instance its own download category.

### Updating

```bash
docker compose build && docker compose up -d
```

Without Compose, rebuild `sonarr-weeb:local` from the updated checkout and recreate
the container using the same mounts and environment. If you use a published image
from another repository, pull its chosen tag instead. Restarting an existing
container keeps its old image, even after a build or pull.

Compose binds to `127.0.0.1` and uses `./config` plus `./media` by default. Set
`SONARR_PRO_MEDIA_PATH` to the media/download parent you want to mount, and
`SONARR_PRO_CONFIG_PATH` for a separate configuration directory. Use
`SONARR_PRO_PUID` / `SONARR_PRO_PGID` for the host user's IDs and ensure the media
directory is writable by that user. `SONARR_PRO_BIND_ADDRESS=0.0.0.0` allows
connections from other hosts; `SONARR_PRO_TZ` selects the container timezone.

Verify a built image with `python docker/tests/smoke.py --image sonarr-weeb:local`.
The [container smoke test](docker/tests/README.md) checks UI, API, native media
probe, permissions and persistence using disposable data.

On this Windows workstation, Docker Engine runs in the dedicated WSL distribution
`SonarrWeebBuild`. The [WSL launcher](docker/start-wsl.ps1) reuses a hidden keepalive
session and starts Compose; it expects an already configured WSL Docker Engine:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docker\start-wsl.ps1
```

Add `-Build` to rebuild first. Docker Desktop is an alternative environment; the
launcher does not install WSL or Docker and is not required on other hosts.

The included Compose file builds this local integration. Public Kakarotto images
do not include these local changes.

Your database and settings live in the `/config` volume and survive the upgrade. There's no in-app updater to wait on and nothing to reinstall.

### Settings that matter

| Variable | Default | What it does |
|---|---|---|
| `PUID` / `PGID` | `1000` | The user files are written as. Run `id` on the host and use those numbers, or downloads arrive owned by root and you won't be able to edit them. |
| `TZ` | `Etc/UTC` | Timezone, so scheduled tasks and log timestamps match your clock. |
| `UMASK` | `002` | File creation mask. Compose reads `SONARR_PRO_UMASK`; the entrypoint also accepts legacy `UMASK_SET` when `UMASK` is omitted. |

Mount your download client's completed folder and your library under a **single** `/media` parent. If they're separate mounts, every import becomes a full file copy instead of an instant move.

### Publishing workflow tags

These tags describe the configured publishing workflow in the repository that
runs it. No registry image was published by this local integration; its tested
image is `sonarr-weeb:local`.

| Tag | What you get |
|---|---|
| `latest` | The newest release. Use this one. |
| `edge` | Every commit to `main` after workflow tests and the container smoke test. Development builds may still change behavior. |
| `sha-<full commit>` | Immutable build of one commit from the publishing repository. |
| `1.2.3` | A specific release version; the workflow removes the Git tag's `v` prefix. |

The release workflow requests `linux/amd64` and `linux/arm64`. Local build/runtime
verification covered `linux/amd64`; arm64 execution was not tested here.

### Coming from Sonarr

The config directory layout is unchanged, so pointing Sonarr Pro's `/config` at a copy of an existing Sonarr config works. **Copy it, don't move it** — the database is migrated on first start and isn't readable by upstream Sonarr afterwards. Keep the original until you're satisfied.

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

Suggestions and bug reports are equally welcome — [open an issue](https://github.com/KakarottoCake/Sonarr-Pro/issues).

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

Sonarr Pro is a derivative work of [Sonarr](https://github.com/Sonarr/Sonarr), copyright the Sonarr contributors, whose copyright notices are retained. Credit for the several hundred thousand lines this is built on belongs to them; the changes listed above are the only part that's new here.
The [contributor register](CONTRIBUTORS.md) preserves the additional fork and
integration credits; third-party components retain their own notices and licenses.
