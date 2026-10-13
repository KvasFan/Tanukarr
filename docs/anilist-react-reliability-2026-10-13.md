# AniList and browser reliability checkpoint — October 13, 2026

This report records the verified local post-1.0.0 development update. Native and
Linux Docker builds/tests, all six isolated runtime gates, the packaged
covered-group browser check and configuration-preserving local deployment
passed. GitHub publication is a separate future stage. It does not change the
immutable `v1.0.0` tag or borrow test results
from the [earlier request-limit report](anilist-rate-limit-verification-2026-10-13.md)
or [grouping checkpoint](anime-season-groups-verification-2026-10-13.md).

## Sanitized observations

A read-only audit covered the available `sonarr`/debug logs and rotations.
Three October 13 metadata-limit failures at **10:33:46.8, 10:44:11.8 and
11:07:36.2** propagate through `AniListProxy.GetSeriesInfo`, batch `AddSeries`,
`ImportListSyncService.ProcessListItems` and `CommandExecutor`. Five other
same-day failures affect optional `GetAnimeRelations` discovery and return
`Unavailable`. Times are recorded as logged, without asserting a UTC conversion.
The shared request service records ten retry waits and eight terminal quota
warnings, mostly with 59–60-second delays. These are deduplicated log events,
not a count of independent HTTP requests; one failure can appear in several
components.

Successful account-list retrieval saves complete membership before metadata
addition. Fetch/parser failures do not replace it with partial or empty data.
The original batch stages additions until its final insertion and catches only
validation failures, so a later metadata HTTP exception can discard pending
successful work. This policy was already present in upstream
`59c22e85993bb1120bfcf9fd1d6f509a510203df`; the additional provider/grouping
requests make its availability cost more visible.

No React error 130 text appears in the audited backend logs. The inherited
frontend error boundary has no backend logging transport. A separate owned
browser fixture subsequently reproduced error 130 using the actual previous
production bundle and a grouped series with a poster. The `SeriesImage` lazy
branch received the CommonJS namespace object from `react-lazyload` as a React
component; its nested default export is the component that must be rendered.
Earlier generated browser fixtures used entries without covers and did not
exercise this branch. An ordinary standalone poster also passed when its
caller explicitly disabled lazy loading. The component, relevant Vite and
`react-lazyload` versions, and production Vite settings matched upstream; fork
configuration differences concerned development tooling. The covered grouped
consumer exposed an inherited CommonJS interoperability defect, rather than a
newly introduced Vite upgrade or component edit.

Four mounted regressions exercise the actual installed dependency shape, with
two failing and two passing before the correction; all four then passed. The
full frontend suite, type check, lint, style checks and Vite build passed.
Chrome using the locally built corrected production UI subsequently passed all
three library views with a generated local JPEG: Posters and Overview rendered
the group, real lazy-loading component and loaded cover; Table retained the
group, with no React errors. The previous bundle failed Posters and Overview
while Table passed. The browser rerun subsequently passed on the final
immutable Docker image without copying replacement UI
files into the container. A read-only
aggregate of the current instance found cover-bearing entries and groups; this
supports the affected path's relevance but is not a claim that the corrected UI
has been tested against the owner's library. No private titles or accounts are
recorded here.

## Implemented backend behavior

- Automatic list-add batches preserve metadata already prepared. HTTP 429
  defers the rest of that metadata source for the current batch; other sources
  can continue. Other metadata HTTP failures defer the affected entry. Manual
  single-series additions still report errors directly. Completion counts the
  series actually inserted.
- The normal five-minute Import List Sync task can resume eligible saved
  AniList entries between full account-list refreshes, whose existing minimum
  interval remains 12 hours. A complete successful fetch must first be
  witnessed in this process. Its saved definition/settings SHA256 and actual
  successful-sync timestamp must still match, automatic additions must remain
  enabled, and the list must be unblocked. The witness contains no raw settings
  or private HTTP body and resets on restart.
- A settings/token change, failed or partial later fetch, mismatched status or
  missing process-local witness refuses saved-list resume. A failed fetch
  does not create permission to use older membership. Complete empty responses
  still synchronize normally. Unknown custom-list membership, malformed flags,
  ambiguous exclusions and GraphQL/pagination errors retain the previous list;
  they never become a successful empty import.
- Public catalogue metadata has a five-minute cache and official relations a
  15-minute cache, shared across provider instances. Simultaneous requests for
  one public key share a fetch; returned objects are cloned. The first full
  metadata query supplies root relations and later schedule pages use a smaller
  selection. Private account lists and OAuth credentials are outside these
  bounded public caches.
- Pacing spreads requests against the remaining advertised server allowance
  and reset time, with an observed-window fallback. Existing shared cooldown,
  conservative minimum spacing, bounded HTTP 429 recovery and safe quota error
  messages remain. External server limits and other clients can still prevent
  progress.
- Metadata pagination checks GraphQL errors, media identity and page boundaries.
  Authoritative `hasNextPage` controls continuation, with a compatibility
  fallback for old recordings without page info. The existing 20-page cap
  remains; capped incomplete schedules are not reused as cached metadata.
  The official schema permits a nullable optional airing schedule, so its
  absence alone is not rejected or interpreted as removal of a catalogue entry.
- Exact expected `SonarrCloudRequestPolicy` blocks are logged at Debug for the
  system-time and shared scene-mapping consumers. Existing mappings are retained
  and unexpected errors remain visible. Requests to `services.sonarr.tv` and
  `sentry.sonarr.tv` remain disabled; this does not enable network access to them
  or claim that remote system-time verification occurred.

See [Library tools](library-tools.md#pending-additions-and-the-development-reliability-update)
for operating conditions and [Metadata providers](metadata-providers.md#anilist-request-reuse-and-pagination)
for bounds and numbering/ownership limits. The protocol references are AniList's
[rate-limit guide](https://github.com/AniList/docs/blob/master/docs/guide/rate-limiting.md)
and [pagination guide](https://github.com/AniList/docs/blob/master/docs/guide/graphql/pagination.md).

## Verification status

The final local image is
`sha256:5ed5aeaf68233a7bc77886e712e69c162f3e0823a81f0bf812dcdaf8f5410997`,
built from the corrected working source and labeled
`anilist-react-reliability-2026-10-13`. This is the local tested image identity,
not a published registry manifest or a claimed final Git commit.

| Check | Executed result |
| --- | --- |
| Sanitized log and upstream-source audit | Executed; no raw log copies, usernames, titles, credentials or provider response bodies published |
| Narrow cached-resume source whitespace check | Passed; this is not a compile or runtime check |
| Coordinated solution build | Passed, zero warnings and errors |
| Native backend suites | Core 6,815 + API 190 + HTTP 57 + targeted Common 101 = **7,163 nonoverlapping passes**; Core console reports 19 ignored cases, others zero |
| Focused backend suite | 256 passed; a subset of the broader suites above and not added to their total |
| Initial backend attempts | StyleCop and generated-fixture JSON-shape failures corrected before the passing focused checkpoint; not hidden as production success |
| Frontend error reproduction and focused regressions | Actual previous bundle reproduced error 130 with a covered grouped entry; four corrected mounted cases passed after two pre-fix failures |
| Full frontend verification | 75 tests passed (71 existing + four installed-dependency regressions); type check, full ESLint, stylelint and Vite build passed |
| Locally built production UI in Chrome | Posters/Overview/Table passed with a generated local JPEG and zero React errors; an independent checkpoint alongside the packaged-image proof below |
| Linux Docker build and backend suites | Core 6,811 + API 190 + HTTP 57 + targeted Common 101 = **7,159 nonoverlapping passes**, 27 ignored; separate from native counts above |
| Final packaged Docker UI in Chrome | All three views passed with a covered synthetic group and loaded local JPEG; Posters/Overview used the real lazy-loading component; zero React/other JS exceptions, zero acquisition and owned cleanup confirmed |
| Changed-production C# Semgrep | 11 files scanned, zero findings and errors; scoped changed-file check rather than whole-project certification |
| Workflow actionlint | Passed |
| Trivy final-image scan | Cached/offline vulnerability database: zero Critical and three distinct High ImageSharp 3.1.12 CVEs (`CVE-2026-106110`, `CVE-2026-106113`, `CVE-2026-106115`, reported fix 4.1.2); zero secrets; inherited findings remain open |
| Isolated AniList quota/resume fixture | **12 checks passed** on the final image; cleanup confirmed, synthetic data and no acquisition |
| AniList fixture runner-ownership repeat | The same 12 checks passed with actual UID/GID 1001 and the supplementary Docker socket group; cleanup confirmed; repeated cases are not added to the 83-check total |
| All six isolated runtime gates | **83 checks passed**: smoke 12 + functional/security 16 + grouping 21 + reserved-title Nyaa 9 + multi-season packs 13 + AniList quota/resume 12 |
| Current-instance deployment | Final image running at `http://localhost:6970`; private consistent backup retained, all 35 protected tables and the configuration XML hash preserved exactly |
| Deployed data and API identity | 11 series, one import list, 11 list items and zero global exclusions retained; authenticated v3/v5 product/release identity checks passed |
| GitHub publication | Future stage; follow the repository [Actions](https://github.com/KvasExert/Tanukarr/actions) and [Docker workflow](https://github.com/KvasExert/Tanukarr/actions/workflows/docker.yml); no unexecuted hosted run or registry digest claimed here |

The quota fixture first saved **one series and three filtered list items**
after three terminal metadata-429 attempts. A later automatic sync reached
three series while account-list query counts remained **4 → 4**. Exactly three
successful full metadata queries (one per ID) and one relation lookup were
observed, with refresh reusing the public cache. Its generated posters were
served through the authenticated application API.

Changing the synthetic exclusion settings and returning unknown membership
retained the previous three items without adding the now-excluded series.
Clearing a blocked status and passing a connection Test did not recreate the
complete-fetch witness. A subsequent explicit complete sync applied the new
filters and stored **two list items with two series**. No personal configuration,
account, indexer or download client was used.

Initial runtime-fixture attempts failed at seven and eight checks because its
cover request lacked the required authentication/used unsupported HEAD, and
because its save helper assumed the same override parameter on v3 and v5.
Those script-only issues were corrected before the 12-check run; application
source did not change for those corrections. Initial receipts remain available
privately alongside the final result.

The local deployment used the same pinned image after a consistent private
backup. An initial readiness assertion incorrectly expected the compatibility
`appName` to be Tanukarr. That field intentionally remains `Sonarr`; the
assertion was corrected to check `productName`, and the same deployment then
passed without a second redeploy. The product branding did not require changing
the native application/configuration identity.

The authenticated real-owner library UI was not exercised in this checkpoint.
Browser and quota-recovery behavior were verified with owned synthetic data,
while the current instance's real saved configuration/data were checked for
exact preservation. This is not a live private-account synchronization or
real-media acquisition claim. These scopes must remain distinguishable.
The earlier raw-XML restore limitation and inherited dependency findings are
not resolved by these reliability changes.

Implementation, regression fixtures, sanitized audit and English documentation
used **OpenAI Codex based on GPT-6**; no exact variant is established. Original
Sonarr, AniList import and metadata-provider contributors remain credited in
[CONTRIBUTORS.md](../CONTRIBUTORS.md).
