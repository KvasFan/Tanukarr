# Container verification

These reusable scripts verify disposable Linux containers. Feature attribution
is recorded in [CONTRIBUTORS.md](../../CONTRIBUTORS.md); the
[documentation index](../../docs/README.md) links the build, integration and
security reports.

## Anime season grouping

For a 1.0.1 or main-source build containing the grouping feature, run from a
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

The local mock also supplies synthetic custom-list names, a null membership map
with valid typed-array flags/known empty membership, and a response with both
membership shapes unknown. The real unsaved AniList provider Test and name action
must accept the known response, reject unknown membership conservatively and
leave saved import-list settings unchanged. Generated test credentials do not
authenticate a real account. A fresh-schema run does not itself prove an upgrade
from an older migration; actual migration fixtures provide separate evidence.

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
records the final image's 12 passing checks and earlier fixture-helper failures.
These checks use synthetic data; they are not a live-account or owner-library
test. A different image or modified script needs its own recorded execution.
The same final-image fixture also passed its 12 checks with actual host
UID/GID 1001 and the supplementary Docker socket group, including cleanup.
That runner-ownership repeat is separate evidence, not 12 additional distinct
runtime cases.

## Concurrent grouped list additions

Use an already built 1.0.1 image and the Python/Docker/OpenSSL prerequisites above
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

## Docker workflow fixture prerequisites

The development Docker workflow runs smoke, functional/security,
grouping/naming/AniList, reserved-title RSS, multi-season pack and the newer
AniList quota/resume checks before publishing its tested image. The additional
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
legacy scalar in the submitted settings must be empty. Its connection
test intentionally fails local validation
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

## Coverage and evidence

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
records checks executed for the renamed image. The earlier SonarrWeeb image names,
versions and test counts in dated reports remain historical evidence.

An image that passes these checks has demonstrated startup, isolated API/UI
behavior, permissions and persistence. The Jellyfin harness additionally verifies
real imports of generated video files. Downloads through indexers and download
clients, imports of the user's existing media, private AniList OAuth
synchronization, arbitrary decoder inputs, all Git history secrets and VAAPI
hardware remain outside these tests. The local validation used Linux amd64;
an arm64 build is not an arm64 runtime test.
