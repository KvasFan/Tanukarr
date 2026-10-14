# Jellyfin integration verification — October 12, 2026

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](README.md) and [Tanukarr 1.0.0 verification](releases/1.0.0-verification.md). Current product terminology is used in this archive. Numeric results, hashes, tags and original published assets retain their recorded identities; historical commands use placeholders for their original local names. This wording update does not repeat verification.

This report covers the dedicated Jellyfin connection added to development main
after the immutable historical 0.0.1 release. It extends the existing upstream
Emby / Jellyfin integration without changing that connection. Configuration and
trigger semantics are in the [Jellyfin guide](jellyfin.md); original Sonarr
authors, Jellyfin API contributors and OpenAI Codex based on GPT-6 are credited
in [CONTRIBUTORS.md](../CONTRIBUTORS.md).

## Verified source and images

Verification started on October 12 and finished on October 13, 2026 in
Europe/Rome; all final receipts are dated October 12 in UTC. The tested source is
[`2805532c4c6ebbee6fcdca90f0df746646fbdd91`](https://github.com/KvasExpert/Tanukarr/commit/2805532c4c6ebbee6fcdca90f0df746646fbdd91).
Subsequent documentation-only commits do not change this implementation.

The local Linux amd64 image used for the complete runtime checks was
`<recorded-local-image-reference>`, image ID
`sha256:a4f6eac864c12f38ddd5bcef399f6ac5a4c889192cee19d4384fc9e368b7ad81`.
The independently built GitHub image was published as
the original image reference for [commit `2805532c4c6ebbee6fcdca90f0df746646fbdd91`](https://github.com/KvasExpert/Tanukarr/commit/2805532c4c6ebbee6fcdca90f0df746646fbdd91) in the linked publishing receipt,
with immutable digest
`sha256:d3d8cbff3e2858a2db1be158df3d2c5e3ea3c20ad820b9bca7f0421383085d7d`.
Both images identify the same source revision. The published image was pulled
anonymously using a fresh Docker configuration, checked with the actual Jellyfin
settings UI, and deployed to the existing local instance. The immutable
`v0.0.1` release and its stable image remain unchanged and predate this feature.

The [CI run](https://github.com/KvasExpert/Tanukarr/actions/runs/38312658105)
and [Docker publishing run](https://github.com/KvasExpert/Tanukarr/actions/runs/38312658464)
both completed successfully for this source revision. The publishing workflow
runs its container checks against the image before pushing it.

## Source and behavior

The new provider supports exactly one selected library per connection, immediate
refresh per eligible imported file, conditional interval ticks from one minute,
and both together. Migration 243 stores import generations, independent
immediate/tick watermarks, last tick, mode, target fingerprint and state epoch.
API keys are excluded from refresh state. Disabled/deleted connections clear
pending work; API-key rotation preserves it; target changes reset it.

The implementation uses the official `Library/VirtualFolders` and selected
`Items/{libraryId}/Refresh` APIs. It rejects stale IDs, redirects and responses
other than HTTP 204. Canonical `Authorization: MediaBrowser Token="..."`
authentication works when Jellyfin 12 disables legacy token headers. API-key
characters unsafe for a quoted header are rejected. HTTP error-response logging
now honors `LogHttpError=false`, including the shared trace representation, so
Jellyfin error bodies cannot reflect a token into that trace.

Library dropdown query keys include connection identity and server settings;
changing the host does not retain options belonging to another Jellyfin server.
The provider inherits Sonarr's import/upgrade/tag filters and does not subscribe
to import-complete events that would duplicate per-file notifications.

## Executed checks

The complete Windows Debug solution compiled with zero warnings and errors.
A fresh run of all eleven .NET suites passed **7,368 tests with zero failures**:

| Suite | Passed | TRX results marked NotExecuted |
| --- | ---: | ---: |
| Core | 6,299 | 28 |
| Common | 778 | 26 |
| API | 56 | 0 |
| HTTP | 57 | 0 |
| Host | 21 | 0 |
| Update | 17 | 0 |
| Libraries | 2 | 0 |
| Windows | 28 | 3 |
| Mono | 12 | 46 |
| Integration | 92 | 12 |
| Automation | 6 | 0 |
| Total | 7,368 | 115 |

The console reports 54 skipped cases; the TRX files additionally record
platform-specific and excluded cases as `NotExecuted`. The 115 recorded
non-executed cases are not counted as passes. Windows automation used Chrome 154
with a matching external ChromeDriver. Python was available for the existing
Common process tests.

All **124 focused Jellyfin cases**, included in Core above, passed. They cover
validation, actual Newtonsoft HTTP responses, selected endpoints,
authentication, redirects, status handling, SQLite state, concurrency, retry,
restart persistence, mode changes, actual notification dispatch, tags/upgrades,
one-minute task registration and migration 243. Four Common cases additionally
verify suppressed response/redirect logging without exposing reflected tokens.

The final Linux amd64 Docker build passed **6,455 backend tests**: Core 6,246,
API 56, HTTP 57 and the focused Common subset 96. Core reported 27 skips. The
build recovered from 13 `MSB3026` copy-retry warnings and finished with zero
errors. All 16 frontend tests, lint, stylelint, typecheck and the production
Vite build passed; Vite retained its existing large-chunk warning.

On the final local image, the startup harness passed all 12 checks and the
functional/security harness passed all 19 checks, including five Chromium
scenarios. These exercise required authentication, key/password masking,
cookies, login lockout, host/CORS/traversal validation, companion access scopes,
public AniList/native-series behavior, distinct list-status fields, restart
persistence, non-root permissions and the compiled UI.

The real Jellyfin **12.2** harness passed all ten checks, including four
Chromium settings scenarios. Two TV libraries had initialized baseline media
and real-time monitoring disabled. Generated-video imports demonstrated an
immediate selected-library refresh, a burst batched at a real one-minute tick,
pending work surviving a Tanukarr restart, independent immediate/periodic
work in combined mode, and recovery after Jellyfin was stopped and restarted.
The unselected library retained its one baseline episode while an additional
file there remained unindexed. Stale IDs and uninitialized libraries were
rejected. Browser checks covered all three modes, both libraries, masked keys
and clearing/refetching the dropdown when the host changes.

An executed Jellyfin 12.2 cold-library experiment confirmed that an empty library
can have no indexed physical folder, accept HTTP 204, and scan nothing. The
connection now checks every selected-library location through a paginated,
scoped folder listing. Uninitialized folders produce a validation error and
retain imports; the application does not request a global scan or change
Jellyfin configuration to work around this server behavior. A proxy/SQLite
regression test verifies zero POSTs/acknowledgments while cold and successful
delivery after initialization.

An additional seven-check real-server recovery experiment verified that an
import made while the selected library was cold remained pending, produced no
refresh POST, and was acknowledged after an explicit initial scan performed as
disposable-server setup. This setup scan is outside the connection's behavior;
the provider never uses a global-refresh fallback. The isolated containers,
network and generated media were removed after the checks.

The exact published image separately passed disposable Jellyfin setup,
connection/schema checks and the four Chromium settings scenarios. Complete
import/tick/recovery tests above used the local image from the same source;
these independently built artifacts have different digests.

## Security checks and deployment

Semgrep 1.180 ran 259 rules on 20 tracked production/helper files changed for
this integration and reported zero findings. It parsed approximately 95.9% of
the scanned lines; one Dockerfile shell section was only partially parsed.
This is a scoped scan, not proof that every repository path or execution path
is free of vulnerabilities.

Trivy 0.75 scanned the final local image for vulnerabilities and secrets. It
reported **15 distinct CVEs: zero critical, three high, nine medium and three
low**, with **zero secret findings**. Findings belong to inherited ImageSharp
3.1.12 and Ubuntu packages; this integration does not patch those dependencies.
The earlier [release security checkpoint](releases/0.0.1-verification.md)
documents their background. The separately built published artifact was not
presented as the image scanned by this local Trivy run.

The published immutable digest above is running in the existing `sonarr-pro`
Compose container at `http://localhost:6970`. Deployment created a private
configuration/database backup and verified the running image, product version
0.0.1, internal version `10.0.0.39209`, .NET runtime 10.0.12, migration 243 and
the Jellyfin notification schema. A read-only comparison found zero series in
both the original pre-deployment database and the current database, with no
series identities or paths added or removed. Authentication/API credentials
were preserved. This does not establish migration of a populated user library.
No Jellyfin connection using the user's own credentials was created.

Machine-readable reports, TRX files, logs and redacted browser screenshots are
kept in the ignored local `_artifacts/jellyfin-2026-10-12/` directory. They are
verification artifacts rather than published release assets. Reproduction
commands are in [docker/tests/README.md](../docker/tests/README.md).

## Limits

Jellyfin accepts refresh requests asynchronously. HTTP 204 verifies acceptance,
not successful scan completion. Tanukarr cannot retry a later server-internal
failure it does not observe. Transport timeouts can cause duplicate accepted
requests. Busy immediate requests may wait for the background retry task, while
retaining one request per pending import. Scheduler checks occur every 30 seconds;
the configured interval is approximate.

The user's production Jellyfin credentials and library have not been supplied.
Owned test servers and generated videos are distinct from verification of a
particular user-managed Jellyfin deployment. Downloads through real indexers
and download clients, private AniList OAuth synchronization, hardware VAAPI
and an arm64 runtime were not exercised. Existing vulnerability findings,
non-executed tests and scanner parsing limits above remain open limitations.
