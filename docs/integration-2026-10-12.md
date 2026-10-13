# Sonarr Pro integration — October 12, 2026

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](README.md) and [Tanukarr 1.0.0 verification](releases/1.0.0-verification.md). Current product terminology is used in this archive. Numeric results, hashes, tags and original published assets retain their recorded identities; historical commands use placeholders for their original local names. This wording update does not repeat verification.

This historical report records the first Kakarotto/gaboy integration on
`integration/sonarr-latest-anilist`. Sonarr Pro was rebased onto the latest commit
of Sonarr's default branch available during that work, `v5-develop@698bb1dbb`.
That is a development base, not a stable-release guarantee.

The subsequent [ikatun integration](ikatun-integration-2026-10-12.md),
[initial Docker validation](docker-validation-2026-10-12.md) and
[final functional and security validation](security-functional-validation-2026-10-12.md)
have their own snapshots. See the [documentation index](README.md) for current
instructions and [contributor credits](../CONTRIBUTORS.md) for feature and AI
provenance.

## Sources and attribution

| Repository / branch | Reviewed commit | Contribution |
| --- | --- | --- |
| [KakarottoCake/Sonarr-Pro, main](https://github.com/KakarottoCake/Sonarr-Pro) | `c104bf96ffd2ebaa18999db29196160e4a7210b0` | KakarottoCake's original Pro features and fork design |
| [Sonarr/Sonarr, v5-develop](https://github.com/Sonarr/Sonarr/commit/698bb1dbb7cafc6ff3a5d1d15f3535a1d16da636) | `698bb1dbb7cafc6ff3a5d1d15f3535a1d16da636` | Upstream Sonarr and its contributors |
| [gaboy498-byte/Sonarr-Pro, main](https://github.com/gaboy498-byte/Sonarr-Pro) | `cef1369f86b23910c606c76e0b20d2ed86809ac7` | Earlier copy of the original Pro history |
| [gaboy, investigate/multiseason-and-fakefilter](https://github.com/gaboy498-byte/Sonarr-Pro/tree/investigate/multiseason-and-fakefilter) | `14eaaebf6267791c60c50a01b6d3109e18ccb53e` | Matt Flowers / gaboy498-byte's additional pack, progress and filtering work |

Gaboy's `main` contains an earlier history of features already present in
Kakarotto. The additional changes were on the investigation branch.

The original Kakarotto history is preserved as `source/kakarotto-main`. Because
the fork had rewritten upstream history, changes through merge `220d53447` were
consolidated at `da990630a`; the following 14 commits were replayed, then that
sequence was rebased onto `698bb1dbb`. The local integration checkpoint is
`c4bd6a625b6a0e01d9bd30393d9fe501d6d6695e`.

Local rebase, integration, AniList corrections and validation were performed
with OpenAI Codex (GPT-6). Original Pro AI attribution and recorded historical
model labels are retained in [CONTRIBUTORS.md](../CONTRIBUTORS.md). Integration
credit does not replace the original contributors' authorship.

## Compatibility and AniList identity

- Pro features, metadata language and Sonarr episode orderings are retained.
- Pro migrations 233–240 retain their numbers. The new upstream migration 236
  for `TagExisting` is renumbered to 241 to avoid a collision.
- `/api/v5/settings/metadata` handles language and a masked TMDB key;
  `/api/v5/settings/metadatasource` remains available as an alias.
- Provider slugs use `anilist-`, `mal-` and `tmdb-` prefixes so numeric IDs from
  different catalogs do not collide.
- AniList import lists read entries belonging to the configured user. Personal
  list status (Watching, Planning, Completed, Dropped, Paused, Repeating), anime
  release status and custom-list membership are separate, combined filters.
  A `false` value in `customLists` does not mean membership. These are user
  lists, rather than catalog discovery, popularity or seasonal lists.
- Native AniList entries retain their provider and ID. Sequels, OVAs and recuts
  are not automatically converted into a parent TVDB series. Preview, sync,
  deduplication and cleanup compare identities from the same catalog.
- GraphQL errors, incomplete pagination and invalid responses are failures.
  A partial list does not authorize library cleanup.
- New exclusions retain the provider identity. Older native exclusions that
  stored only a negative TVDB placeholder after deletion cannot be reconstructed
  automatically.
- Private AniList data requires authentication as the owner of the list.
  Public access cannot read another user's private lists.

AniList references: [user lists](https://docs.anilist.co/guide/graphql/queries/media-list),
[pagination](https://docs.anilist.co/guide/graphql/pagination),
[errors](https://docs.anilist.co/guide/graphql/errors) and
[authentication](https://docs.anilist.co/guide/auth/).

## Disabled shared Sonarr cloud services

The fork policy blocks requests to `services.sonarr.tv` and `sentry.sonarr.tv`,
including HTTP redirects. Factories remain available for dependency injection;
the block applies when constructing or sending a request. Errors omit tokens
and query strings.

Cloud updates for scene mappings and the daily-series list, and MyAnimeList
OAuth through Sonarr's services, are therefore disabled. Existing database
scene mappings are retained; the daily-series cache is in memory and can expire.
SkyHook TVDB and the AniList, TMDB and MyAnimeList/Jikan providers remain
available through their respective APIs.

## Additional gaboy features

The first six commits on the investigation branch, from `9cd27f4` through
`79ef6d2`, were integrated and adapted: explicit multi-season ranges,
season-specific scene mappings, legacy-grab import reconciliation, per-episode
progress, and torrent-title/submission-route checks. Pro manual overrides,
requested seasons, qBittorrent reuse and multi-season source retention are
preserved. See the [gaboy feature report](gaboy-integration.md) for commit credits
and behavior.

Magnet metadata retrieval from `872d7cf` and `14eaaeb` was excluded. It requires
`MonoTorrent 3.0.2-sonarrsecurity.1.rev0002`, which was absent from the public
NuGet feed. Official MonoTorrent does not expose the memory limits required by
that code. Magnets without readable metadata therefore remain an inspection
limit.

## Verification at this checkpoint

| Check | Result |
| --- | --- |
| `dotnet build src/Sonarr.sln -c Debug --no-restore` | Passed; 0 errors, 0 warnings |
| Core, excluding the `IntegrationTest` category | 5,805 passed, 17 skipped, 0 failed |
| API | 39 passed, 0 failed |
| HTTP | 57 passed, 0 failed |
| Offline shared-cloud HTTP policy tests | 12 passed, 0 failed |
| `yarn typecheck`, `yarn build` | Passed |
| `yarn lint`, `yarn stylelint -f github` | Passed across the frontend |
| Windows startup with a new SQLite database | Passed; migrations through 242 |
| Settings API | Language, masked TMDB key and legacy alias verified |
| Live AniList lookup through the local API | Cowboy Bebop, `aniList:1`, correct slug and ordering |

The first runs identified rebase regressions and outdated assertions about
upstream authentication logs. Core and HTTP were rerun after corrections and
passed; authentication retains upstream behavior. After the shared-cloud block,
285 focused Core tests for lists, metadata and mappings also passed. Startup,
queue API and AniList lookup were checked again. The distinct Core, API, HTTP
and policy results total 5,913 passing tests, plus 17 skipped Core tests.

Tools: .NET SDK `10.0.401`, Node `24.19.0`, Yarn `1.22.22`. Local tools and full
logs are in `<local-tool-directory>`; TRX results are under
`test-results`. Portable Git is at `git\cmd\git.exe` within that tool directory.

No real library was used. Live downloads, media moves, AniList account
authentication and private data were not tested. External-service
`IntegrationTest` cases were excluded at this checkpoint. No repository or
Docker image was published.

## Container startup and later validation

The included `docker-compose.yml` builds `<recorded-local-image-reference>` from these sources.
After configuring volumes and user IDs:

```sh
mkdir -p media
docker compose build
docker compose up -d
```

The public Kakarotto image referenced by the original README does not contain
these local changes. Docker was unavailable during this first integration
checkpoint; it was subsequently installed and the actual Linux image was built
and deployed. See the [initial Docker report](docker-validation-2026-10-12.md)
and [final validation report](security-functional-validation-2026-10-12.md)
for those later results.

Current branch ancestry also includes upstream
`v5-develop@59c22e85993bb1120bfcf9fd1d6f509a510203df`; see the current
[README](../README.md) and [fork design notes](../FORK.md). The `698bb1dbb`
reference and test counts in this report identify the first integration snapshot.
