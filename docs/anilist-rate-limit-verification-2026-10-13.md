# AniList request-limit correction verification

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](README.md) and [Tanukarr 1.0.0 verification](releases/1.0.0-verification.md). Current product terminology is used in this archive. Numeric results, hashes, tags and original published assets retain their recorded identities; historical commands use placeholders for their original local names. This wording update does not repeat verification.

This October 13, 2026 checkpoint covers a correction after the immutable
historical `v0.1.0` release. That published release remains unchanged.

## Reported failure

The supplied October 12 log, with exception banner `10.0.0.36364`, showed
that the custom import list `Top` was read successfully: it produced 65 reports
at 20:53:39 and 51 reports at 20:58:56. Both attempts subsequently failed with
HTTP 429 while `AddSeriesService` called `AniListProxy.GetSeriesInfo` to fetch
the full metadata for the bulk addition. A concurrent connection test also
received HTTP 429. The original metadata provider logged that it was backing
off but immediately threw the error without waiting or retrying.

Bulk addition gathers metadata before committing its series batch. An
unhandled request-limit error can therefore abort the entire batch even after
the custom list has been retrieved correctly. The relevant AniList metadata
provider was added by the source fork; it is not present in the recorded
upstream `57816c149` snapshot. Custom-list matching and exclusion logic were
not identified as the cause of this logged failure.

## Correction

List pagination, list connection tests, public search and anime metadata now
use one shared request service. The service reuses Sonarr's domain rate limiter,
paces requests at least 2.1 seconds apart, shares its lock and cooldown across
provider instances, observes `Retry-After` and `X-RateLimit-Reset`, and allows
at most three attempts for temporary HTTP 429 responses. Lower advertised
allowances widen the interval, and exhausted successful responses also start a
cooldown. Repeated failures remain explicit errors. HTTP 429 response bodies
are omitted from request-limit exceptions, and HTTP client body logging is
disabled for these requests; this does not sanitize every existing non-429
exception path.

AniList's [official documentation](https://github.com/AniList/docs/blob/master/docs/guide/rate-limiting.md),
checked for this investigation, states a normal limit of 90 requests per minute
and a current degraded limit of 30, plus a separate burst limiter. A shared
local budget cannot coordinate unrelated applications or other instances behind
the same public IP, so server-directed cooldown remains necessary.

## Verification

| Check | Executed result |
| --- | --- |
| Windows Debug solution | Passed; zero warnings and errors |
| Focused AniList/provider suite | 140 passed, zero failures or skips; overlaps the complete Core suite |
| Windows offline Core | 6,346 passed, zero failures; 27 TRX NotExecuted cases, versus 19 console skips |
| Windows API and HTTP | 56 and 57 passed respectively, zero failures or skips |
| New regression cases | 38 passed: 25 shared request-service, eight metadata and five import-list cases |
| Linux amd64 Docker build | Passed; Core 6,342, API 56, HTTP 57 and focused Common 96 passed, zero failures; 27 Core skips |
| Corrected-image container checks | 12 smoke and 16 functional/security checks passed |
| Corrected-image synthetic AniList sync | Seven checks passed; HTTP 429 recovered, three eligible native series and six episodes added |
| Latest-source Semgrep 1.180.0 | 65 applicable rules, four changed production files, zero findings/errors; approximately 100% parsing coverage reported |
| Initial-release Docker reproduction | Custom `Top` list validated/fetched two pages; metadata HTTP 429 aborted the command with zero series added |

The complete Windows Core/API/HTTP result is **6,459 passes**, with no failures.
The synthetic baseline used the published 0.1.0 image identified by registry
digest `sha256:39ceb00be64219959634a6c72738e6a284f45ef3030e7fd59ca8fa971d8225d9`.
A local HTTPS GraphQL fixture used its own generated CA installed in the
container's normal trust store and a container-only hostname override. TLS
certificate validation remained enabled. It returned three matching entries,
a false custom-membership flag and an exclusion veto on a later page. A
simulated HTTP 429 on the first full metadata request reproduced the supplied
failure. The interval from list fetching to metadata was only 0.488 seconds.

The corrected Linux backend total is **6,551 passes**. The Docker frontend stage
passed its 16 existing tests, lint, stylelint, typecheck and production build;
the final retry reused that unchanged stage's build cache. The initial Linux
attempt exposed a new test fixture's logging-initialization issue and an inherited
one-second asynchronous logging check failure. After the fixture initialization
correction, the complete fresh image build passed; the inherited logging test
was not changed. A transient registry IPv6 connection error also required a retry.

The corrected local image is `<recorded-local-image-reference>`, identified by
`sha256:74ef0cfab1539064700cad61b56cdc34495efe0d046547977fd716c760f0568f`.
It was built from the tested working tree with OCI revision label
`anilist-rate-limit-working-tree-2026-10-13`; it is not the immutable release
artifact. The runtime reports product 0.1.0, internal core 10.0.0.42511,
.NET 10.0.12 and migration 243.

Against the same HTTPS fixture, `ImportListSync` completed after retrying the
same metadata ID successfully **2.105 seconds** after HTTP 429. Only AniList
IDs 101001, 101002 and 101003 were added; each had two refreshed episodes.
The false `Top` membership and the later-page exclusion veto were preserved.
Observed request arrival gaps were at least 2.098 seconds during import and
2.019 seconds when including the initial connection-test TLS negotiation;
arrival gaps can be shorter than client dispatch gaps because network/TLS
latency varies. No indexers, download clients, acquisition commands, queue
entries or history were present. All disposable containers, configuration,
certificates and HTTPS fixtures were removed afterward.

The existing Compose instance at `http://localhost:6970` was updated to this
tested local image after a private, stopped-container configuration/database
backup. API/authentication settings, provider IDs and library identities/paths
were preserved. The user database contained zero series before and after the
update, so this deployment check does not establish migration of a populated
real library. No manual synchronization of the user's account was triggered.

The reported private account and real downloads were not used by these fixtures.
Native logs/TRX, synthetic baseline receipts, source hashes and scanner results
are retained in the ignored `_artifacts/anilist-rate-limit-2026-10-13/` directory.

## Attribution

Original provider work remains credited to Xabis, Private Contributor,
KakarottoCake and the source-declared co-authors in
[CONTRIBUTORS.md](../CONTRIBUTORS.md). This correction and its verification use
OpenAI Codex based on GPT-6; no more specific model variant is established.
