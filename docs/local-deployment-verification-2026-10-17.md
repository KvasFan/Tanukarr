# October 17, 2026 local deployment and queue-removal verification

This ledger records the checks actually executed on October 17, 2026 while
rebuilding the local Docker environment, deploying the current `main` source
locally, running the available suites and fixing the defects they exposed. It
starts from source `72841c02b7695195592596ac5574199f19de0b0b` plus the changes
committed with this ledger. Earlier release reports keep their own source and
image identities; their counts are not reused here.

## Environment recovery

The registered WSL2 distribution `SonarrWeebBuild` still existed, but its
`ext4.vhdx` disk was gone, so the previous Docker Engine, images and the running
container were lost. The bind-mounted `./config` directory (database, settings,
logs) was intact. Before any deployment it was copied to a private local backup;
the copied `sonarr.db` matched the live file byte for byte (SHA-256
`8bdb12124dda5774e1c44644969d606cf54c59c1e239d76e1c5834c2578128fc`).

The distribution was recreated from the retained
`ubuntu-base-24.04.5-base-amd64.tar.gz` (SHA-256
`e77b6f10c2590cef872b33ee9f635a0e3fd1f57fb074c0e52b5c7f56147a0c86`, matching the
retained `SHA256SUMS`), Docker CE 29.9.1 and Compose v5.6.0 were installed from
Docker's repository, `systemd` boot was enabled, and Python 3.12.3, OpenSSL
3.0.13 and FFmpeg 6.1.1 were added for the container fixtures. The local
toolchain is unchanged: .NET SDK 10.0.402, Node 24.22.0, Yarn 4.18.1.

## Windows source checks

| Check | Result |
| --- | --- |
| `dotnet build src/Sonarr.sln -c Debug` | Zero warnings, zero errors (repeated after each fix) |
| Core (non-integration) | Before fixes 7,869 passed / 19 skipped; after fixes **7,875 passed / 19 skipped**, including the six new pending-queue cases |
| API / HTTP / Host | **348 / 80 / 36 passed** before and after the fixes |
| Common security subset (CI filter) | **113 passed** |
| Common complete | 741 passed, 20 skipped, 1 failed: `Should_be_able_to_start_python` starts the Microsoft Store `python.exe` alias, which prints only to stderr; the same test passes with a real Python on `PATH`. Environment, not product |
| Libraries / Update / Windows | 2 / 17 / 28 passed (2 platform skips) |
| Integration | Before the client fix 84 passed, 8 failed, 12 ignored; after it **92 passed, 12 ignored, 0 failed** |
| Frontend | `yarn install --immutable`, TypeScript, **211 tests**, ESLint, Stylelint and the production Vite build passed |
| Compression worker (WSL, FFmpeg integration) | **29 cases passed** |

Observed but not changed: `GetById_should_return_null_if_process_doesnt_exist`
fails when the Common suite is filtered down to a handful of tests (the NLog
test target has not captured the warning yet) and passes in the complete suite;
`ProcessProvider` and the fixture are identical to upstream.

## Defects found and fixed

### v3 pending-queue removal returned 404

`DELETE /api/v3/queue/{id}` for a pending (delayed or
download-client-unavailable) release always answered `404 NotFound`, so third
party v3 clients could not remove such items. The v3 queue lists one item per
release/episode pair (`pending-{id}-ep{episodeId}`), but
`FindPendingQueueItemObsolete` searched the grouped v5 list (`pending-{id}`),
which can never match. Upstream `develop` at `2b39049ee` (October 16, 2026)
still contains the same lookup. Reproduced on the running local instance
(`DELETE /api/v3/queue/714539178` → 404, pending count unchanged). The lookup
now uses `GetPendingQueueObsolete()`.

### Removed pending releases stayed in the queue

Both v3 and v5 removals deleted the database rows but left the static in-memory
pending list untouched, so the removed release stayed visible (and the v5 count
unchanged) until the next RSS sync, series/profile edit or restart. Reproduced
on a disposable copy of the real database after the first fix: the v3 delete
answered 200 while the count stayed at 244. `RemovePendingQueueItems` and the
obsolete variant now drop the deleted ids from the list and publish
`PendingReleasesUpdatedEvent`, so the queue controllers broadcast the change.

`FindPendingQueueItemObsoleteFixture` adds six regression cases (lookup by each
v3 episode id, every v3 queue item resolvable, v3/v5 id separation, unknown id,
and immediate removal from both queues with one published event for v3 and v5).
Without the production change three of the lookup cases fail; with it the
complete Core suite passes.

### Integration-test client serialized fork resources incorrectly

Eight integration tests failed: the four `v2_categories_*` indexer cases and
the four `CutoffUnmet` cases. The RestSharp 106 client serializes request bodies
with its bundled SimpleJson, which turns a non-empty Newtonsoft `JArray` (the
fork's `movieCategories` default `[2000]`) into nested arrays and the
`CodecMultiplierOverrides` dictionary into a list of key/value pairs. The same
requests sent as standard JSON to a Windows Debug instance succeeded (indexer
POST with array, omitted, null, string and empty movie categories → 201;
quality-profile PUT as-is, with a cutoff change, and with omitted, null, string
or PascalCase `codecMultipliers` → 202). The client now serializes bodies with
the application's Newtonsoft settings (`JsonNetSerializer`); no API behaviour
changed.

## Linux image builds

Each build runs the frontend checks and the Core/API/HTTP/Host suites plus the
Common security subset inside the image.

| Image | In-image results |
| --- | --- |
| `sha256:d158469aa2630624623b49f75b8e252b08d12b2439b4d79e8a6e9bb885821c0c` (source `72841c02b`) | Core 7,865 / 27 skipped, API 348, HTTP 80, Host 32 / 4 skipped, Common subset 113; frontend 211 tests, ESLint, Stylelint, TypeScript, Vite build |
| `sha256:7af1470ad8b865a989470afe67bc87e7b0c5973ef35eb07a0cfd61e02190ff5e` (lookup fix) | Core 7,869 / 27 skipped, API 348, HTTP 80, Host 32 / 4, Common subset 113 |
| `sha256:6c02ae038a8d4d1a44e85443f065e5a76c807b26511920e6e890e1636bd9bfeb` (both fixes, deployed) | Core 7,871 / 27 skipped, API 348, HTTP 80, Host 32 / 4, Common subset 113 |

The image label records `72841c02b…-dirty` because the fixes were built from
the working tree before this commit existed.

## Container fixture reports

All fixtures ran inside the rebuilt WSL engine with disposable configurations,
generated media and local catalogue doubles; the owner's configuration was not
used. The `tanukarr:local` tag moved during the feature run, so the first eight
feature reports record image `d158…` and the last eight record `7af1…`; none of
the exercised code changed between those builds except the queue lookup.

| Report | Image | Checks |
| --- | --- | ---: |
| `smoke.json` | `d158…` | 12 |
| `functional-security.json` | `d158…` | 19 |
| `first-run.json` | `d158…` | 4 |
| `anime-groups.json` | `d158…` | 21 |
| `nyaa-fixture.json` | `d158…` | 9 |
| `series-packs.json` | `d158…` | 13 |
| `anilist-reliability.json` | `d158…` | 30 |
| `group-import-collisions.json` | `d158…` | 12 |
| `import-snapshots.json` | `d158…` | 20 |
| `myanimelist-reliability.json` | `d158…` | 15 |
| `list-set-operations.json` | `d158…` | 25 |
| `simkl-reliability.json` | `7af1…` | 23 |
| `quality-codecs.json` | `7af1…` | 22 |
| `custom-qualities.json` | `7af1…` | 28 |
| `artwork-reliability.json` | `7af1…` | 14 |
| `metadata-sources.json` | `7af1…` | 19 |
| `movie-catalogues.json` | `7af1…` | 14 |
| `backup-tasks.json` (API only, no browser) | `7af1…` | 10 |
| `jellyfin-refresh.json` (genuine Jellyfin 12.2) | `7af1…` | 12 |
| `smoke.json` (final) | `6c02…` | 12 |
| `functional-security.json` (final) | `6c02…` | 19 |
| `first-run.json` (final) | `6c02…` | 4 |
| `v3-pending-delete.json` (disposable copy of the real database) | `6c02…` | 9 |

Every report passed and removed its own containers, networks and temporary
data; only the deployment container remained afterwards. The last report is a
one-off local script, not a repository fixture: it copies the backed-up
`config.xml` and `sonarr.db`, replaces the API key, marks every scheduled task
as just executed and disables the indexer and import list so the copy makes no
outbound request, then verifies a v3 single delete (200, 244 → 243), a v3 bulk
delete (200, 243 → 241), a v5 delete (204, 241 → 239), an unknown v3 id (404)
and the absence of provider requests in the container log.

The browser-driven collection, backup and first-run gates and the genuine
qBittorrent download fixtures were not run here; the PostgreSQL fixture was not
run locally. A multi-agent code audit was started but every agent stopped on
the account's session limit before producing findings, so it contributed no
evidence.

## Local deployment

`docker compose up -d --no-build` recreated `sonarr-pro` from the final image
(`sha256:6c02ae03…`) on `127.0.0.1:6970`, bind-mounting the existing `./config`
and `./media`. The first deployment of the day migrated the existing database
from schema 252 to 253 (Main and Log databases committed; `PRAGMA
integrity_check` returned `ok`), and the final redeploy started on the migrated
data without further migrations. Verified on the live instance: `/ping` OK,
Forms authentication enforced (anonymous `/` → 302 to `/login`, API without key
→ 401), login page and compiled assets served, v3/v5 status report schema 253,
292 series retained, the 244 non-fallback pending releases loaded about two and
a half minutes after start, health only reports the owner's missing download
client and empty `/tmp` root folder, and the container log contains no
warnings beyond the expected Kestrel port override. The scheduled series
refresh then ran against the owner's catalogues as part of normal operation.

Also observed, unchanged: the v3 queue lists per-episode items (1,737) while
the v5 queue and `queue/status` count releases (244), which is upstream's v5
design; and the startup log prints the `Sonarr.Host` assembly build number
while the API reports `Sonarr.Common`'s, because the inherited `10.0.0.*`
wildcard numbers each assembly separately. The product version `0.1.0` is
consistent across both APIs and the frontend.

## Scope

Compiles, tests and the listed fixtures were run; the deployed instance was
exercised through its API and checked for persistence. No release was published
and no image was pushed. Real downloads, the browser suites, Linux arm64 and a
PostgreSQL-backed deployment remain outside this ledger.
