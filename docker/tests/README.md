# Container verification

These reusable scripts verify disposable Linux containers. Feature attribution
is recorded in [CONTRIBUTORS.md](../../CONTRIBUTORS.md); the
[documentation index](../../docs/README.md) links the build, integration and
security reports.

## Metadata source policy and TVmaze

Run the packaged policy and TVmaze gate with an already built image:

```sh
python3 docker/tests/metadata-sources.py --image tanukarr:verified \
  --timeout 300 --report metadata-sources.json
```

The fixture uses the pinned Python TLS sidecar, an owned temporary configuration,
an internal Docker network and generated TVmaze/AniList responses. It checks
v3/v5 source ordering and enablement, masked-key and older-client preservation,
invalid-policy rejection before mutation, native identities across multiple
catalogues, TVmaze episodes and specials, refresh ownership and malformed-response
retention. It restarts the application to verify saved state and creates no
download configuration. The report records actual image identity, checks and
cleanup. Public-provider availability and a real library are outside its scope.
Pass `--live-tvmaze` to also run a separate disposable, read-only title lookup
through the packaged TVmaze provider. That optional check writes its own
`*-public-tvmaze.json` report, adds no series and is omitted from CI.

The current **1.1.0 preparation** adds five publishing gates to the fourteen
required reports from 1.0.5: alternate artwork, custom qualities, genuine
qBittorrent/folder imports, selected-library Jellyfin refresh, and actual packaged
collection browser checks. The [1.1.0 ledger](../../docs/releases/1.1.0-verification.md)
records their executions; the historical counts below keep their original scope.

## New 1.1.0 gates

Use an already built image and fresh report names. The artwork and quality
fixtures use the digest-pinned Python TLS/catalogue double already required by
the other provider gates. The client fixture additionally uses genuine
qBittorrent pinned to `97333cc1ccf903e0bbcd8eb67e3332e4970e9c1ecabe145e499e9c0847ebcaed`.
Refresh uses the existing pinned Jellyfin server. All generated media and
accounts are disposable; client connectivity and manual import are separate
from completed-download automation or public acquisition.

```sh
python3 docker/tests/artwork-reliability.py --image tanukarr:verified --timeout 300 --report artwork.json
python3 docker/tests/custom-qualities.py --image tanukarr:verified --timeout 300 --report custom-qualities.json
python3 docker/tests/download-client-folders.py --image tanukarr:verified --timeout 300 --report client-folders.json
python3 docker/tests/jellyfin-refresh.py --image tanukarr:verified \
  --jellyfin-image jellyfin/jellyfin:12.2@sha256:357724bf0ae27a672c7cbaa899db2d9abeb13dbd8657ccce750258a4c059d037 \
  --timeout 300 --report jellyfin-refresh.json
```

For the packaged browser gate, install the exact external Playwright 1.64.0
package and its Chromium browser outside the application source. Pass the Node
executable and module directory explicitly:

```sh
python3 docker/tests/collections-browser.py --image tanukarr:verified \
  --browser-node /path/to/node --playwright-module /path/to/node_modules/playwright \
  --playwright-version 1.64.0 --timeout 300 --report collections-browser.json
```

The gate checks generated Forms authentication, real decoded covers at several
display densities, collection/member navigation, monitoring/settings persistence
and read-only searches. It rejects requests leaving its loopback application
origin. Reports record image/runtime/source identities, browser versions and
owned-resource cleanup; screenshots use generated catalogue content.

## Preserved 1.0.5 checkpoint

**Tanukarr 1.0.5 is published and deployed.** Its
[snapshot and identity guide](../../docs/import-list-snapshots.md) describes
complete private account/list snapshots, local filters, safe per-operation
reuse and the optional new-series metadata preference. Public metadata and
relations outside the snapshot can still need requests. The
[provider matrix](../../docs/library-tools.md#provider-capabilities) defines
which selectors and snapshot boundaries each service supports. Earlier 1.0.4
runtime results remain in their [published checkpoint](../../docs/releases/1.0.4-publication.md);
they do not verify the new source. New execution and publication results belong
in the [preparation ledger](../../docs/releases/1.0.5-verification.md) and
[publication follow-up](../../docs/releases/1.0.5-publication.md). The final tag
passed all fourteen required gates, totaling 251 checks.

Supply the exact image being verified through `--image`. The published 1.0.5
result is recorded separately; development commands alone do not establish
publication or results for a different build.
Snapshot fixtures must verify complete collection/list reads before filtering,
custom-only membership, failures retaining saved data, isolation between
accounts/grants and release of temporary references. Preference checks must
exercise v3/v5 persistence and omission compatibility, positive supplied IDs,
fallback, exclusion retention and unchanged existing-series paths/numbering.

## Complete import-scoped snapshots

Run the new mandatory snapshot gate with an already built image:

```sh
python3 docker/tests/import-snapshots.py --image tanukarr:verified \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report import-snapshots-report.json
```

The fixture checks complete AniList chunks and MAL pages, reuse by two saved
definitions during one operation, a fresh subsequent synchronization, independent
local filters, grant isolation, partial-read retention and shared public relation
data during grouping. Nullable AniList progress/update fields, absent custom-name
options and unnamed standard groups exercise valid API responses; identity and
completion safeguards still apply. It tests actual importer/API/database behavior
with generated responses on trusted local TLS and an internal Docker network.
No owner's configuration or private provider account is used. JSON records the
actual image identity, executed checks and cleanup result. The final local
1.0.5 candidate passed 20 checks. A separate exact-source hosted run also passed
20 checks and its report is verified in the published release assets.

## Anime season grouping

For a 1.0.2 or main-source build containing the grouping feature, run from a
Linux/WSL host with Python 3, OpenSSL, FFmpeg with `libx264` and Docker available:

```sh
docker pull python:3.13-alpine
python3 docker/tests/anime-groups.py --image tanukarr:local \
  --mock-image python:3.13-alpine --report anime-groups-report.json
```

The script requires both images to be available locally and does not build or
pull them. It creates an internal Docker network and a Python HTTPS sidecar
whose DNS alias is `graphql.anilist.co`. A generated CA is installed only in the
disposable application's normal trust store; TLS validation stays enabled. The
mock returns synthetic catalogue entries and typed official-relation payloads.
It does not forward requests, contact public AniList/Jikan, authenticate an
account or configure indexers/download clients. The application API binds to a
random port on `127.0.0.1` through a third, fixed-destination TCP relay. The app
and mock remain solely on the internal network; only the relay also joins the
separate API bridge. The relay forwards only the fixture application's port and
does not expose an arbitrary HTTP proxy.

Checks cover authenticated v3/v5 preview and rejected API keys, a three-entry
official TV chain, mixed TV/movie/TV chain, movie-only saga, special/spin-off
exclusions, a preferred TV spine beside an ambiguous parallel movie, and true
TV/TV branches, server-owned folder generation, manual
existing/new groups, reserved-name movies and same-kind duplicate rejection,
explicit-path preservation and legacy v3 updates. FFmpeg generates a tiny video;
the application imports and rescans its native `S01E01` filename inside the
group's `Season 02` leaf without nesting another `Season 01` or changing native
episode identity. Generated movie files also import/rescan inside a title leaf
and `Film 02` without an extra native-season folder. Strict Tokyo story-arc and
Apothecary Season 3 cases exercise the title/number distinction. Five naming
expressions, native group-file token samples and legacy naming PUTs are checked.
Grouping edits and restart preserve the imported bytes. This
does not establish media-server season remapping or a real-library migration.

The unsaved AniList Test and name action must leave saved import-list settings
unchanged. The 1.0.5 path starts with a complete selected-user anime
collection and evaluates custom-group membership locally, including entries
hidden from ordinary status groups. Incomplete or conflicting collections must
retain previous membership. Generated test credentials do not authenticate a
real account. Earlier typed-array/missing-map recovery scenarios are historical
checks of the pre-1.0.5 importer. A fresh-schema run does not prove an upgrade
from an older migration; migration fixtures provide separate evidence.

Only this invocation's three containers, two networks and temporary data are removed in
cleanup. `--keep` retains them for inspection; disposable API and CA keys remain
in those temporary files and are not printed. `--openssl` and `--ffmpeg` can
select external executables. Run where the temporary and repository bind paths
are visible to the Linux Docker daemon, as described below. See the
[feature guide](../../docs/anime-season-groups.md) and
[dated verification report](../../docs/anime-season-groups-verification-2026-10-13.md)
for the build and executed results rather than inferring a pass from this script.

## Deterministic AniList quota and saved-list resume

For a development image containing the newer AniList reliability changes, run
with the same Python/Docker/OpenSSL prerequisites used by the grouping fixture:

```sh
python3 docker/tests/anilist-reliability.py --image tanukarr:verified \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report anilist-reliability-report.json
```

Select an already built local image with `--image`; the script does not build
or pull it. It uses an owned internal network, a generated trusted test CA,
synthetic AniList list/catalogue responses and a generated poster. It does not
contact public AniList, use private settings/accounts, configure an indexer or
download client, or acquire media. The fixed-destination relay exposes only the
fixture API on a random loopback port, while the app and mock have no external
route.

The scenario checks that a terminal metadata HTTP 429 saves the successfully
prepared first series and complete filtered list membership, then an automatic
sync resumes pending additions without requesting account-list pages again.
It also checks public metadata reuse during refresh. Changing the synthetic
exclusion settings and returning unknown membership must preserve existing
saved items and refuse stale additions. Clearing a cooldown or passing a Test
must not recreate a complete-fetch witness; a later explicit complete fetch
must apply the new filters normally. All IDs and credentials belong only to
this invocation's disposable data.

The [reliability checkpoint](../../docs/anilist-react-reliability-2026-10-13.md)
records that checkpoint's image, 12 passing checks and earlier fixture-helper failures.
These checks use synthetic data; they are not a live-account or owner-library
test. A different image or modified script needs its own recorded execution.
At that historical checkpoint, the same image's fixture also passed its 12
checks with actual host
UID/GID 1001 and the supplementary Docker socket group, including cleanup.
That runner-ownership repeat is separate evidence, not 12 additional distinct
runtime cases.

The historical 1.0.2 scenario checks the final exclusion-veto selection rule,
overlapping custom names across duplicate pages and complete-collection recovery
after missing membership fields. The 1.0.5 importer uses complete
collections as its primary input instead. Partial/wrong-owner/missing-ID
collections must still retain the previous list. The earlier counts belong to the
[1.0.2 verification report](../../docs/releases/1.0.2-verification.md), rather
than those earlier twelve checks.

## MyAnimeList account lists and Jikan catalogue fixture

Use a built image containing the 1.0.2 direct MAL account-list changes:

```sh
python3 docker/tests/myanimelist-reliability.py --image tanukarr:verified \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report myanimelist-reliability-report.json
```

The trusted local TLS fixture supplies official-shape MAL account pages and
Jikan catalogue/episode responses. It checks public Client ID versus OAuth access,
tag inclusion minus exclusion, account/score/airing filters, pagination,
unknown required tags, bounded HTTP 429 retention and direct PKCE/OAuth
state/replay handling. Unsaved Test renewal and saved Fetch renewal have separate
settings-persistence assertions. Jikan TV/movie catalogue ingestion and episode
gaps are separate from account-list membership. No real MAL account, registered
client, Jikan network response or owner library is used; TLS validation remains
enabled and acquisition stays empty. The report records image identity and
owned-fixture cleanup. This command is reproduction guidance, not a claim that
these cases have already passed for an arbitrary image.

## TMDB, Trakt, MDBList and Plex source-set fixture

The source-set fixture uses generated provider responses and disposable data:

```sh
python3 docker/tests/list-set-operations.py --image tanukarr:verified \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report list-set-operations-report.json
```

It covers provider-native list IDs/slugs/URLs, combined inclusions minus
exclusions, supported list-name actions, hidden legacy-source normalization and
safe retention when a selected list or page is unavailable/incomplete. These
providers require at least one included source. Excluded membership must be read
without optional content filters. Use the executed report for actual case counts,
source/image identity and limitations; synthetic credentials do not authorize
or verify a real account.

The same fixture also exercises the Plex TV Watchlist using official-shape local
TLS responses: 1,001 generated items require an eleventh page, and a failure on
that later page must retain the complete earlier list. Test reads without saving
provider settings; recovery must replace the snapshot only after completion.
Named Plex Lists and a live Plex account are outside this fixture's scope.

## Optional SIMKL V2 custom-list fixture

Use a built image containing the optional registered AUTH V2 implementation:

```sh
python3 docker/tests/simkl-reliability.py --image tanukarr:verified \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report simkl-reliability-report.json
```

Generated trusted TLS SIMKL/Jikan responses exercise V2 authorization, readable
custom-list choices, selected Watchlist status, inclusion intersection minus
exclusions, complete pagination and beta/premium failure retention. Full
inventories, activity/date-based deltas, native-ID deletion reconciliation and
private cursor persistence are checked separately from unsaved Test staging.
The fixture uses no live premium account or owner configuration, downloads no
media and records exact image identity and owned cleanup in its report.

## Concurrent grouped list additions

Use an already built 1.0.2 image and the Python/Docker/OpenSSL prerequisites above
to test the group-slot race against real SQLite in owned disposable data:

```sh
python3 docker/tests/group-import-collisions.py --image tanukarr:local \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report /tmp/tanukarr-group-import-collisions.json
```

The script does not build or pull images. Its generated TLS AniList sidecar and
application run on an owned internal network with normal certificate validation;
a fixed-destination relay publishes only the disposable API on a random loopback
port. It does not read the main configuration, contact public metadata services,
use real credentials, configure acquisition providers or download media.

The scenario holds a metadata response after an automatic list batch prepares
related Season 1, Season 2, Movie 1 and an unrelated entry. A concurrent manual
API addition then claims the prepared Season 1 slot. The repaired flow must defer
only that candidate, save the other three entries, report three actual additions
and retain both the manual occupant and complete four-entry list snapshot. A
later unchanged sync must report zero additions, avoid duplicate rows and
complete without another list fetch or SQLite UNIQUE error. Manual same-kind
season/movie duplicates must return HTTP 400. The fixture checks distinct season
and movie ordinal namespaces, native S01 episode identities, retained unique
indexes and a read-only SQLite `integrity_check`.

To reproduce the older behavior against a known unrepaired image, add
`--expect-old-failure`. That mode requires the exact reported season-slot UNIQUE
exception, a failed `ImportListSync` and rollback of all four prepared additions;
it is baseline reproduction, not a pass of the corrected behavior. The
[1.0.1 verification report](../../docs/releases/1.0.1-verification.md) records
actual image identities and executed results. PostgreSQL is not exercised by
this SQLite fixture.

Cleanup removes only this invocation's containers, networks and temporary data.
`--keep` retains those resources for inspection; generated API/OAuth/CA secrets
remain private to the fixture and are redacted from its captured logs.

## Native Nyaa search metadata

Use an already built image to exercise native v5/v3 complete-series searches and
a v5 absolute episode search in a separate, disposable instance:

```sh
python3 docker/tests/nyaa-search.py --image tanukarr:local \
  --title 'Baki-dou' --episode-count 13 --report nyaa-baki-dou-report.json
```

The script seeds a synthetic AniList-owned native season directly in its own
SQLite database. `--episode-count` supplies the known catalogue count used for
matching; repeat `--alias 'Exact catalogue alternate title'` only for aliases
actually belonging to that title. These supplied values do not verify a live
provider's metadata ingestion or ordering. The fixture is one native identity,
rather than a merged parent anime; complete-series search uses that identity.
An RSS snapshot can return valid but rejected or unmatched releases, and live
RSS availability/counts can change.

The script saves debug request URLs and parsed release metadata, keeps queue
and history empty and configures no download client. It does not acquire
torrents or media. `--fixture` instead runs a local synthetic RSS response to
verify that `&`, `#` and `+` remain part of the generated search term. That mode
does not contact public Nyaa. Neither mode verifies a real owner library,
automatic downloads or media-server catalogue remapping.

## Deterministic native multi-season pack searches

The independent `series-packs.py` fixture exercises a Standard TV entry with
six native episodes across Seasons 1, 2 and 3. It uses a fixed local RSS sidecar,
without an AniList dependency or live indexer requests:

```sh
docker pull python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a
python3 docker/tests/series-packs.py --image tanukarr:local \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --report series-packs-report.json
```

Both v3/v5 complete-series routes must return the individual S01, S02 and S03
packs together with an S01-S03 pack. The six per-season `seasonPacksOnly` calls
must retain pack results and exclude individual-episode decoys. Native IDs,
episode counts and multi-season boundaries remain unchanged. The receipt records
each API call's actual RSS queries and elapsed time to expose episode-search
fanout. Nothing is grabbed: download clients, queue/history, acquisition
commands and torrent/media requests remain empty.

The API binds to a random loopback port. The RSS sidecar publishes no host ports
and joins only its owned network. Cleanup removes both owned containers, that
network and temporary data; `--keep` retains them for inspection. The synthetic
database is separate from an existing deployment and does not verify live TVDB
metadata ingestion or a real library.

## Editable quality limits and codec-size decisions

Use a built 1.0.3 or newer image containing the profile-backed quality summary and codec
policy. This fixture reuses the isolated pack harness and pinned local Python
RSS sidecar; it does not request a public provider or access an owner library:

```sh
python3 docker/tests/quality-codecs.py --image tanukarr:local \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --report quality-codecs-report.json
```

The actual v3/v5 routes must summarize edited profile limits, identify Mixed
values, preserve omitted fields/profile differences and accept explicit null as
a cleared limit. Invalid merged batches must return 400 before changing titles
or profiles. Saved values must survive restart. The read-only codec catalogs
must match across both APIs.

The native Nyaa RSS parser receives twelve synthetic variants: AVC, HEVC, AV1
and MPEG-2 across individual episodes, a single-season pack and a three-season
pack. Minimum/maximum rejection uses the normalized policy while reported
physical release bytes remain unchanged. Preferred-size ranking is covered by
separate native decision tests. Queue/history/acquisition
commands and torrent/media requests remain empty. The fixture seeds only its
own stopped SQLite database, uses random loopback ports and removes its owned
containers, network and temporary files unless `--keep` is selected. Host
UID/GID ownership transitions follow the same guarded helper as the pack test.

This API/RSS fixture does not replace actual React interaction tests or
encoder-quality measurements. Executed counts and exact image identity belong
to the [1.0.3 verification report](../../docs/releases/1.0.3-verification.md).

The optional actual Chromium helper [quality-ui.cjs](quality-ui.cjs) uses an
external Playwright installation and generated Forms credentials supplied only
on stdin. Its caller must own a fresh empty configuration; the helper edits
quality settings and must not be pointed at an owner instance. It exercises
hourly/GiB display, the codec table, real global/profile saves, Mixed focus/blur,
explicit Unlimited, failed-save retention and a valid retry. Screenshots hide
password fields, and saved library/acquisition settings must remain empty.
Its browser receipt is separate from the mandatory API/RSS report and the
mounted frontend tests.

## Backup task timestamps

Run the new 1.0.4 backup fixture on the exact candidate image:

```sh
python3 docker/tests/backup-tasks.py --image tanukarr:verified \
  --report /tmp/tanukarr-checks/backup-tasks.json
```

It creates a fresh Forms-authenticated instance and verifies actual V3/V5 manual
ZIP backups, archive CRC/SQLite integrity and required configuration content.
It checks Last Execution/Start/Next timing, an intentionally non-writable owned
backup path, retained failure details, recovery, the real scheduler's overdue
backup, manual/scheduled archive categories and restart persistence. Existing
lists, media and deployment configuration are never accessed.

For optional actual Chromium button verification, supply external tooling:

```sh
python3 docker/tests/backup-tasks.py --image tanukarr:verified \
  --browser-node "$(command -v node)" \
  --playwright-module /tmp/sonarr-ui-audit/node_modules/playwright \
  --report /tmp/tanukarr-browser-checks/backup-tasks.json
```

The helper reads generated disposable credentials from stdin, opens the packaged
Tasks page, presses Backup and observes the SignalR-triggered task response and
updated Last Execution cell without reloading. It also checks the V3 timestamp,
browser errors and outbound/acquisition requests. Its `backup-ui.json` is a
separate browser receipt; Docker publishing does not require external Playwright.
Use a fresh report path for each attempt so failed evidence is preserved.

## Jellyfin watched state

The 1.0.4 watched fixture uses an actual fresh local Jellyfin server, generated
media and an independent Tanukarr configuration. Pull the pinned server image
and run:

```sh
docker pull jellyfin/jellyfin:12.2@sha256:357724bf0ae27a672c7cbaa899db2d9abeb13dbd8657ccce750258a4c059d037
docker pull python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a
python3 docker/tests/jellyfin-watched.py --image tanukarr:verified \
  --jellyfin-image jellyfin/jellyfin:12.2@sha256:357724bf0ae27a672c7cbaa899db2d9abeb13dbd8657ccce750258a4c059d037 \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report /tmp/tanukarr-checks/jellyfin-watched.json
```

The test selects one local user/library and changes Jellyfin's real Played state,
rather than assuming that a mocked UserData response proves synchronization.
Native imported-file paths establish the local episode mapping. Actual observed
check names, image/runtime identity and cleanup state are recorded in its JSON.
An owned TLS AniList response service can exercise the configured originating
account write path; it does not contact an external account. OpenSSL is required
for that service's private test certificate. The fixture does not use an owner's Jellyfin instance or private AniList/MAL/
Trakt/SIMKL credentials. Source-account writer unit/response tests and live
external-account execution remain separate evidence. See the
[watched guide](../../docs/jellyfin-watched.md) for the opt-in/provider contract.

## Docker workflow fixture prerequisites

The development Docker workflow runs smoke, functional/security,
grouping/naming/AniList, reserved-title RSS, multi-season pack and the newer
AniList quota/resume checks before publishing its tested image. Version 1.0.2
also requires the MyAnimeList/Jikan and provider source-set fixtures. The latter
includes TMDB/Trakt/MDBList selections and complete Plex Watchlist pagination;
SIMKL V2 has its own additional required fixture. Version 1.0.3 adds the
editable quality/codec fixture. Version 1.0.4 adds actual backup/task timing and
local Jellyfin watched-state fixtures. Smoke and functional/security plus eleven
additional reports formed thirteen publishing gates for 1.0.4. Version 1.0.5
adds the complete import-scoped snapshot gate, making fourteen required reports:
smoke, functional/security and twelve additional provider/feature reports.
Release packaging aggregates those twelve additional reports in
`provider-verification.json` and
records their actual check counts in `release-manifest.json`, using the same
tested image throughout.

Since 0.1.3 the workflow has two gate profiles. Pushes to `main`, pull requests
and manual runs use the **full** profile: every fixture above. A release tag
uses the **light** profile, because releases are tagged on a `main` commit whose
code already passed the full profile there: the tag rebuilds the image with all
in-image suites and repeats smoke, functional/security, first run (API and
form, no browser), quality/codec decisions, custom qualities and the
populated-library upgrade from the earlier release. Release packaging requires
a passing report for every gate of the selected profile and records the profile
as `gateProfile` in `release-manifest.json`; the release notes name the `main`
run that executed the full profile for the same code.
The additional
runtime checks use locally generated catalogue/RSS data;
live AniList/Jikan/Nyaa responses and private account credentials are not
required. They remain distinct from optional public-provider investigation.

On Debian/Ubuntu, install the host media/certificate utilities and pull the
pinned Python fixture image before reproducing the checks:

```sh
sudo apt-get update
sudo apt-get install --yes --no-install-recommends openssl ffmpeg
docker pull python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a
python3 docker/tests/anime-groups.py --image tanukarr:local \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report groups-report.json
python3 docker/tests/nyaa-search.py --image tanukarr:local --fixture \
  --report nyaa-fixture-report.json
python3 docker/tests/series-packs.py --image tanukarr:local \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --report series-packs-report.json
python3 docker/tests/anilist-reliability.py --image tanukarr:local \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report anilist-reliability-report.json
python3 docker/tests/quality-codecs.py --image tanukarr:local \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --report quality-codecs-report.json
```

OpenSSL creates the grouping fixture's test CA. FFmpeg with `libx264` generates
its tiny import videos. The pack/RSS checks need Python 3 and Docker but do not
generate videos. Dependency installation and image pulls still require their
package registries; the statement about local fixtures concerns provider data.

The Nyaa and multi-season pack fixtures seed only their own stopped SQLite
database. On POSIX hosts, the runner's UID/GID may differ from the app's
`1000:1000`, as on GitHub's UID 1001 runner. A shared helper validates the
temporary root, fixture container label/ID, stopped state and exact config bind,
then uses a network-disabled, disposable process from that same app image to
return only the owned config directory to the host runner for seeding. It does
not widen permissions or change another deployment. Restart restores the app,
config and database to UID 1000; receipts record both ownership transitions.
Both fixtures passed their full 9/13 checks in actual UID/GID 1001 subprocesses,
including restart and cleanup. This portability check is separate from proving
a live metadata import or acquiring media.

## Jellyfin integration test

For development main's dedicated Jellyfin connection, run:

```sh
docker pull jellyfin/jellyfin:12.2
python3 docker/tests/jellyfin.py --image tanukarr:local \
  --jellyfin-image jellyfin/jellyfin:12.2 --report jellyfin-report.json
```

This optional test starts disposable Tanukarr and Jellyfin containers on its
own Docker network, with random loopback ports. It initializes an isolated
Jellyfin administrator, creates two TV libraries with real-time monitoring
disabled, generates baseline videos using Jellyfin's packaged FFmpeg, and runs
an initial setup scan to initialize both physical library folders. That initial
scan belongs only to the disposable test setup. The connection subsequently
refreshes only its selected library. The test adds
Cowboy Bebop through the public AniList provider without indexers or download
clients, then imports only generated files. Internet access is needed for that
metadata lookup; no private AniList account or existing media is used.

Checks cover the Connect schema/dropdown/key masking, immediate and one-minute
conditional ticks, import batching, restart persistence, combined mode, retry
after Jellyfin is unavailable, stale/uninitialized-library rejection, and
confirmation that a newly generated episode in the unselected library remains
unindexed while its existing baseline remains available. The test takes
several minutes because it waits for real scheduled ticks and asynchronous scans.
`--jellyfin-image` can select another version; only executed versions establish
compatibility. The script removes only its own containers, network and temporary
data, and its report contains no credentials. `--keep` preserves only those
resources for failure inspection and prints their locations without credentials.
See the
[feature guide](../../docs/jellyfin.md) and
[verification report](../../docs/jellyfin-verification-2026-10-12.md).

For optional Chromium checks of the actual Connect editor, supply an external
Node executable and Playwright installation:

```sh
python3 docker/tests/jellyfin.py --image tanukarr:local --browser-only \
  --browser-node /path/to/node --playwright-module /path/to/node_modules/playwright \
  --report jellyfin-browser-report.json
```

The browser verifies all three modes, both library choices, credential masking,
and clearing/refetching options after host changes. It cancels the editor and
checks that no saved connection changed. Omit `--browser-only` to run generated
imports as well. Browser screenshots hide credential inputs; credentials are
passed to Node through stdin, never command-line arguments.

## Startup smoke test

Build the image, then verify its runtime using Python 3 and the Docker CLI:

```sh
docker build --progress=plain -t tanukarr:local .
python docker/tests/smoke.py --image tanukarr:local --report smoke-report.json
```

The script needs Python 3 and a running Docker Engine configured for Linux
containers. Run it where temporary bind-mount paths are visible to that Docker
host. On Windows with Docker Desktop, use `--docker C:\path\to\docker.exe` if
the executable is not on PATH. For a Docker Engine installed inside WSL, run the
commands inside that WSL distribution with Linux Python and Docker; a Windows
temporary path is not automatically a valid mount source for that daemon.
It uses only Python's standard library and does not install or build anything.
Add `--anilist` to verify one live AniList lookup; it keeps the AniList identity
and checks that no series is added.

Each invocation creates its own new config and empty media directories under the
host's temporary directory. It seeds a random API key without printing it, mounts
those directories into a new container, and publishes an automatically assigned
port only on `127.0.0.1`. It never uses the Compose config or an existing library.
The temporary empty media directory is made writable so the test works when the
host UID differs from the container's UID 1000.

The checks cover HTTP readiness, the actual compiled UI JavaScript, V5 runtime
status, initialized SQLite, the packaged native ffprobe, an empty series list
and queue, UID/GID 1000 and the application's `002` umask, file/directory creation
in both mounts, metadata route
aliases and masked key saves, and persistence after a container restart. The
TMDB value is a synthetic test value; no TMDB request or real credential is used.
This verifies container startup and persistence, not a real library, indexer,
download client, download, import, or media transfer.

The default cleanup removes only the exact container ID created by the script
and its own temporary directory. `--keep` preserves those two resources for
inspection and prints their locations. Stop/remove the printed container before
removing the printed temporary directory. The retained `config.xml` contains
the temporary API key. A failure returns exit code 1 and can be saved using
`--report`; the JSON report contains no credentials.

The older `run-all.sh` and `mono/` scripts target historical Mono packages and
`_tests_linux`/`_output_linux` layouts. They are retained for upstream history;
they are not the .NET 10 runtime verification path and were not run in the
current validation.

## Functional and security checks

Run the isolated authentication, permissions, validation and persistence checks:

```sh
python3 docker/tests/functional-security.py --image tanukarr:local \
  --report functional-security-report.json
```

Add `--anilist` to exercise a real public AniList lookup, native-series addition,
refresh, duplicate rejection and complete-series dry run against an empty local
Torznab fixture. Only the disposable instance is modified; no download is started.
The live path also verifies twelve rejected companion-key add requests across
v3/v5: custom paths, unregistered roots and implicit search flags must not change
the series database or schedule acquisition commands. The checks distinguish
membership status, anime airing status and custom lists.

```sh
python3 docker/tests/functional-security.py --image tanukarr:local --anilist \
  --report functional-security-live-report.json
```

The default checks cover required Forms authentication, missing/incorrect API
keys, valid login, masked password, HttpOnly/SameSite cookie, login lockout,
invalid reset tokens and redirects. They also exercise explicit AllowedHosts,
DNS-rebinding Host variants, CORS, static-path traversal attempts, invalid JSON
and release-search scopes, companion endpoint allowlists and revocation, TMDB
key masking and route aliases, AniList schema fields, and restart persistence.
The restarted temporary database is checked for salted PBKDF2 password storage.

For a real browser run, install Playwright and its Chromium browser in an external
tool directory, then supply `--browser-node /absolute/path/to/node` and
`--playwright-module /absolute/path/to/node_modules/playwright`. Both arguments
are required together. For example, on a Linux host with Node/npm installed:

```sh
npm install --prefix /tmp/sonarr-ui-audit playwright@1.64.0
/tmp/sonarr-ui-audit/node_modules/.bin/playwright install chromium
python3 docker/tests/functional-security.py --image tanukarr:local --anilist \
  --browser-node "$(command -v node)" \
  --playwright-module /tmp/sonarr-ui-audit/node_modules/playwright \
  --report /tmp/sonarr-ui-audit/reports/functional-security.json
```

Playwright 1.64.0 is the version recorded in the October 12 browser artifacts.
Chromium also requires the host's browser system libraries. These tools are
external test dependencies and are not added to the application package.
The Node helper
reads disposable credentials from stdin and redacts them from reports. It checks
login, navigation, provider attribution/links, native-series detail and lookup,
complete search and AniList list settings. The AniList editor scenario also
checks custom-list inclusion/exclusion controls, literal names
containing commas, all eleven Import status controls and the submitted settings
payload. The six removed account-status exclusion controls must be absent from
the form and request; custom-list exclusions remain available. The duplicate
legacy single-list textbox is absent even with advanced settings shown, and any
legacy scalar in the submitted settings must be empty. The visible help is the
current `en.json` text: included custom lists select entries and Excluded
Custom Lists remove them, while account/media Import status filters remain
separate. The Add Series source selector must offer the combined entry plus all
seven catalogues of a fresh configuration in their default order. The connection test
intentionally fails local validation
without OAuth credentials; cancelling the editor leaves saved lists unchanged.
For an owned settings-only invocation, the Node helper accepts `menuOnly: true`
in its stdin JSON. That mode retains login, AniList form/payload/cancellation
checks and JavaScript/cloud-request checks, while skipping live provider lookup
and series-search cases. Its report records `menuOnly`; the regular Python
wrapper continues to request the default full browser scenario.
The native-series/complete-search
scenario needs `--anilist`; the browser's lookup scenario requires network access
even if that flag is omitted. Browser reports and screenshots are
written alongside the specified report. These optional live checks require network
access. Docker CI runs both default scripts without live AniList or Playwright.

The harness also verifies that the app runs as UID 1000 with no effective Linux
capabilities and cannot write its executable, application directory or startup
script, with Docker's seccomp filter active and no privileged mode, Docker-socket
mount or host networking. The entrypoint initially uses root to assign volume
ownership before dropping privileges; the checks inspect the running application.
It does not use existing Compose configuration, real media, a download
client or a private AniList account. Cleanup removes only the instance and temporary
directories created by the invocation.

## Native movies, mixed collections and populated-library upgrades

Run the added catalogue, transfer and compatibility checks against one explicitly
selected image:

```sh
python3 docker/tests/movie-catalogues.py --image tanukarr:verified \
  --timeout 300 --report movie-catalogues.json
python3 docker/tests/download-packs.py --image tanukarr:verified --jellyfin --movies \
  --timeout 300 --report download-packs.json
python3 docker/tests/library-upgrade.py --image tanukarr:verified \
  --timeout 300 --report library-upgrade.json
```

The movie catalogue fixture exercises actual HTTPS/Newtonsoft deserialization,
IMDb lookup through TMDB Movies/OMDb, native one-feature identity, mixed TV/anime/
movie/standalone-special membership, generated imports, malformed refresh recovery
and raw disk/console Trace privacy before report redaction. Catalogue responses
and API credentials are generated locally; this does not verify a live account.

The download fixture uses genuine pinned qBittorrent and Jellyfin containers.
Owned full-runtime videos travel through private torrents and an internal web
seed, then the application's normal completed-download handler imports them.
It checks single/multiple-season packs, explicit retained-season reuse, native
movie features and same-collection movie packs, SHA-256 bytes, naming and native
identities. Jellyfin probes cover immediate, scheduled, restarted and retried
selected-library refreshes; a bounded request recorder distinguishes the selected
film library from untouched TV libraries. No public torrent is acquired.

The upgrade fixture first populates the pinned earlier image's schema252, then
starts the candidate with the same disposable configuration and media. It checks
the schema253 migration, existing anime-film filenames, provider/episode/file IDs,
monitoring, paths and bytes through rescan, owning-provider refresh and restart.
It never opens the owner's configuration or media.

Use `collections-browser.py --gui-audit` with the documented Playwright arguments
for desktop/touch routes, Settings submenus, movie category editors, mixed-group
actions and native Library Import. Review the saved PNGs individually or compare
their hashes with a recorded visual review; successful screenshots alone are
not a visual inspection.

`postgres-collections.py --report postgres-collections.json` starts its own
loopback-bound PostgreSQL17 container and runs persisted migration, slot-range
and concurrent-update regressions against the already-built test assemblies.
It generates private credentials, does not inherit an existing database
connection, and removes only its owned container and volumes.

Reports retain exact image/source identities, actual checks, errors and cleanup
evidence. A failed or older-image report does not verify a later candidate.

## Coverage and evidence

### First-run account creation

Run the account setup check against the exact built candidate image:

```sh
python3 docker/tests/first-run.py --image tanukarr:local --timeout 300 \
  --report first-run-report.json
```

The fixture starts with an empty owned configuration, never a real library. It
checks protected anonymous access, the account creation form, antiforgery
cookie/token validation, server-side username/password validation, successful
creation and login, stale-form rejection, restart persistence, salted password
storage and password absence from process/application logs. Optional
`--browser-node /path/to/node --playwright-module /path/to/playwright` runs
Chromium against the actual form, mismatch feedback, authenticated navigation,
logout and another login. A 375x812 viewport check verifies every field and the
submit button fit without horizontal clipping. Desktop and mobile screenshots
are captured before credentials are entered; requests outside the owned local
fixture are blocked and fail the check.
The Docker workflow requires that browser gate and packages its image-qualified
report. Existing accounts and explicitly configured initial credentials are
preserved; a fresh installation no longer generates a password in startup logs.

The entrypoint rejects root or malformed PUID/PGID, stops on config ownership
failure and restricts the config directory to its owner and group. Media umask
behavior remains configurable. Compose sets `no-new-privileges`; a deployment
with `--user` can also avoid the root ownership preparation step, provided its
mounted configuration and media already have the required permissions.

Disposable fixtures keep UID 1000 and use the host runner's non-root primary
group as PGID (1000 when the runner is root or on Windows). This lets an ordinary
CI runner inspect its own generated SQLite and artwork while retaining config
mode 0750. Ownership preparation for a stopped database still verifies the exact
fixture container, labels and mounted temporary path before making changes;
restart restores the application's UID and configured group.

These scripts return exit code 1 on a failed check. `--report` records results and
the exercised options. Functional reports include the inspected image ID and
whether browser checks ran. `--keep` retains disposable resources and their
temporary credentials for debugging; handle them as private runtime data.
Screenshots are visual artifacts and should be reviewed before sharing.

The Dockerfile also runs frontend tests, lint, stylelint, typecheck and the Vite
build. The native amd64 backend stage runs Core/API/HTTP tests excluding
`IntegrationTest`, and the Common cloud-policy/log-sanitizer subset. These are
build checks, not every available .NET suite. Current whole-suite and platform
results are recorded in the
[functional and security validation report](../../docs/security-functional-validation-2026-10-12.md)
and the later
[Jellyfin verification report](../../docs/jellyfin-verification-2026-10-12.md),
followed by the
[AniList filter verification report](../../docs/anilist-filters-verification-2026-10-13.md).

The [Tanukarr 1.0.0 verification report](../../docs/releases/1.0.0-verification.md)
records checks executed for the renamed image. Earlier image identities,
versions and test counts in dated reports remain historical evidence. Archive
prose uses the current product name; linked release manifests retain original
published asset names.

An image that passes these checks has demonstrated startup, isolated API/UI
behavior, permissions and persistence. The Jellyfin harness additionally verifies
real imports of generated video files. Downloads through indexers and download
clients, imports of the user's existing media, private AniList OAuth
synchronization, arbitrary decoder inputs, all Git history secrets and VAAPI
hardware remain outside these tests. The local validation used Linux amd64;
an arm64 build is not an arm64 runtime test.
