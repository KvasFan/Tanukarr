# AniList settings cleanup: verification checkpoint

Date: **2026-10-13**. This checkpoint covers the current `main` simplification
after the immutable Tanukarr 1.0.1 release. No version bump or replacement of
that release's source, assets or verification reports is implied.

## Behavior and compatibility

The editor now uses the existing **Import** checkboxes to select account-list
states and anime release states. Exclusion vetoes apply to **Excluded Custom
Lists**. The six separate Exclude Watching/Planning/Completed/Dropped/Paused/
Repeating controls, settings properties and parser vetoes are removed. Older
saved/requested flags are ignored by typed settings deserialization, including
when their values are true. Import controls, OAuth, metadata identity,
custom-list inclusion/exclusion and unknown-membership safeguards remain.

The duplicate legacy single-list textbox is also absent, including under Show
Advanced. Its saved value transfers into **Included Custom Lists** and the
normalized scalar is cleared. Only the legacy scalar is trimmed, matching its
old parser behavior; modern names retain literal whitespace, commas and case.
An exact duplicate is not added twice. Both Newtonsoft and System.Text.Json
deserialization normalize old settings, and validation supports older API
clients submitting the scalar. Reading/testing a connection does not rewrite
its stored settings JSON; a later Save persists the normalized representation.
No database migration, list deletion or OAuth reset is required.

The frontend also handles stale schemas conservatively: it displays a nonempty
legacy name as an included tag, hides the old textbox, and omits it from custom
name-load requests. Editing included tags clears the old scalar through the
existing field-change mechanism. Removing the migrated tag therefore removes
the restriction instead of allowing a hidden value to recreate it.

Excluded custom-list membership still overrides an accepted Watching entry or
an included custom list, including duplicate entries across pages. Unchecked
Import statuses remain independent filters. Unknown custom-list membership,
malformed responses and partial fetches retain the last complete list.
See [Library tools](library-tools.md#anilist-lists) for current configuration.

## Executed checks

| Check | Recorded result |
| --- | --- |
| Final solution Debug build | Passed in 8.24 seconds, zero warnings/errors |
| Fresh focused backend suite | 311 passed, zero failed/skipped after the successful final native build |
| Fresh API suite | 190 passed, zero failed/skipped |
| Final Docker Linux backend suites | 7,194 passed, 27 ignored, zero failures: Core 6,846, API 190, HTTP 57 and focused Common security 101 |
| Complete frontend Node suite | 77 passed, zero failed/skipped |
| Focused AniList React cases within that frontend scope | Eight passed; includes old true status flags, stale scalar migration, literal names and removal without filter resurrection |
| Scoped final ESLint | `AniListFieldGroups.tsx` passed after the formatting/type declaration fixes |
| Complete frontend lint/style/typecheck/Vite | All passed; Vite retains its existing large-chunk warning |
| Semgrep changed-code scan | 27 C# rules, three production C# files, zero findings/errors; separate from image/dependency scanning |
| Browser-helper syntax | `node --check docker/tests/ui-security.cjs` passed |
| Actual packaged settings browser | Four Chromium/Playwright checks passed on the immutable image below; generated Forms credentials and no live lookup |
| Maintained documentation targets | Passed; results are local file-target checks, not remote publication or application execution |
| Changed reusable helper syntax | Four Python fixtures parsed with `ast.parse`; Node browser helper passed `--check`; no bytecode files or imports were needed |

The final local image passed all seven mandatory runtime gates and was deployed
at **2026-10-13 16:35:07 UTC**, preserving the existing configuration/library.
Hosted publication of this later `main` change had not yet occurred at this
checkpoint. No result from an old assembly or the immutable 1.0.1 release is
substituted for these final executions.

## Failed intermediate checks and corrections

One initial cache-witness regression failed because the fixture compared a
fresh subsecond token-expiry value with a serialized value at the provider's
persisted UTC second precision. The fixture now establishes the complete-fetch
witness from the same saved representation before adding the obsolete flags;
it still verifies byte-equivalent effective settings and a single unchanged
provider fetch. This does not weaken the production settings-change guard.

An intermediate native build failed on a new test's `SA1515` comment spacing.
A subsequent `--no-build` focused run therefore used stale assemblies and is
not accepted as final evidence. The spacing was fixed, a fresh successful
solution build completed, and the focused suite above passed on those assemblies.

The expanded frontend initially failed two Prettier layout checks in
`AniListFieldGroups.tsx`; import and ternary formatting were corrected. Typecheck
also reported `TS2322` for the inferred JSON request-body union. The body now
uses its existing mutation contract, `Record<string, unknown>`, without a field
cast or name/value coercion. Scoped ESLint and the complete final frontend gates
passed after those corrections.

## Final local image and runtime verification

The verified local **Linux amd64** image has immutable ID
`sha256:de7ff7883b8a577c4fa592faad1507c915d23ae71ba23bc4bd02925f1b96a77b`
and configuration digest
`sha256:b68b2f40bacc520fab477e2cd245f5fa086b0377f2ab758a84d5ff6407a96c40`.
Its OCI revision label is `anilist-custom-exclusions-only-2026-10-13`, identifying
this local working-tree build rather than a published commit. Browser-fixture
API status observed product **Tanukarr 1.0.1**, compatibility app name **Sonarr**,
internal core **10.0.0.29390**, .NET **10.0.12** and schema **247**. No replacement
of the immutable 1.0.1 release image is implied by the retained product version.

Seven completed mandatory fixture reports identify that exact immutable image:

| Fixture | Passed checks |
| --- | --- |
| Smoke | 12 |
| Functional/security | 16 |
| Grouping | 21 |
| Native Nyaa RSS | 9 |
| Series/season packs | 13 |
| Concurrent grouped-list additions | 12 |
| AniList quota, legacy settings and custom-list exclusions | 18 |

These total **101 passing mandatory checks**. The four browser checks below are
separate, for **105 executed runtime/browser checks** across those scopes. The reports
are `smoke-report.json`, `functional-report.json`, `groups-report.json`,
`nyaa-report.json`, `packs-report.json`, `group-import-collisions-report.json`
and `anilist-reliability-report.json`
under the ignored root-check directory. Fixture scopes remain disposable;
Nyaa uses synthetic RSS, and no real-account OAuth or owner's media acquisition
is inferred from a successful native indexer fixture.

### Remaining image findings

Trivy **0.75.0** scanned the exact `de7ff…` image ID above. Its High/Critical
vulnerability result contains **zero Critical** and **21 High package
occurrences covering three distinct CVE IDs** in **SixLabors.ImageSharp 3.1.12**:
`CVE-2026-106110`, `CVE-2026-106113` and `CVE-2026-106115`. The scanner advertises
**4.1.2** as the fix; that dependency upgrade is not part of this cleanup and
these advisories remain unresolved. The separate secret scan reported **zero
detected secrets**. Repeated package occurrences are not 21 distinct advisories,
and a filtered High/Critical scan does not claim zero lower-severity issues.
`trivy-image.json` and its log retain the exact artifact identity and results.
The changed-code Semgrep result above has separate scope and does not erase
these dependency findings.

### Actual settings browser

The actual packaged `/settings/importlists` UI passed four checks using external
**Playwright 1.64.0** and its Chromium browser. The disposable application used
fresh owned configuration, generated Forms credentials and an empty library.
Anonymous navigation redirected to login; a valid generated account loaded the
application. With advanced settings shown, all 11 Import checkbox fields were
present, the six account-status exclusions and old single-list textbox were
absent, and custom include/exclude tags retained literal comma-containing names.
The unsaved Test returned the expected HTTP 400 before any AniList request due
to intentionally missing OAuth credentials. Its payload retained Import/custom
fields and omitted removed flags; the compatibility scalar was absent or empty.
Cancel retained the fixture's saved-list snapshot. No unhandled JavaScript
exceptions or forbidden Sonarr-cloud requests were recorded.

The helper's optional `menuOnly` mode skips public metadata lookup and the
unrelated series-search scenario; its default full browser mode remains
unchanged. These four menu checks are separate from the mandatory runtime gates.
After interaction, the fixture still had no saved lists, series, queue, indexers
or download clients. The owned container/data were cleaned up. This did not
access the owner's configuration, browser profile, media or AniList account.
Receipts are `menu-browser/ui-security.json`, `fixture-summary.json` and
`browser.log` beneath the ignored root-check directory below.

### Expanded API/storage fixture

The expanded [AniList fixture](../docker/tests/anilist-reliability.py) contains
18 passing checks on the final immutable image: its earlier quota/resume/exclusion cases plus v3/v5 schema absence,
old true flags ignored by saved/Test requests, and legacy scalar migration and
removal. The legacy-storage scenario deliberately seeds one owned stopped
SQLite fixture row, then checks normalized API tags without a stored-JSON rewrite
before Save, no resurrected filter after removing the tag, and compatible older
API inputs. Its targeted seed is part of the fixture, not an owner-data edit.

Together with smoke, functional/security, grouping, Nyaa, packs and group-import
collision fixtures, all seven mandatory Docker gates passed their 101 checks.
The AniList fixture cleaned up its owned resources afterward. Two preceding
helper-only attempts failed: one incorrectly required the v3 provider Test to
use v5's HTTP 204, although the actual v3 success response is HTTP 200; the other
left a host SQLite reader open after leaving its transaction context, interfering
with a later stopped-fixture seed. The helper now checks each API's valid
response and explicitly closes that owned reader. No production change or image
rebuild was needed for either correction. Their failed receipts remain as
`anilist-reliability-v3-status-first-attempt.json` and
`anilist-reliability-sqlite-reader-first-attempt.json`; the final 18-check report
passed with cleanup complete.

All fixtures use owned disposable settings and generated credentials. They do
not establish private-account OAuth or imports into the owner's real library.

## Local deployment and publication boundary

A fresh, privately permission-restricted backup was taken with the application
stopped before deploying the exact verified `de7ff…` image at **16:35:07 UTC**.
The existing Compose project and configuration/media mounts were retained; an
older snapshot was not restored over later owner changes. SQLite integrity
passed, **35 protected tables** compared exactly and the **config.xml SHA-256**
remained unchanged. The current library retained **100 series, one configured
import list and 99 cached list items**. Backup locations, titles, account names
and keys are intentionally absent from this public document.

The instance is ready at **http://localhost:6970/series** with Tanukarr **1.0.1**,
core **10.0.0.29390**, .NET **10.0.12** and schema **247**. Read-only authenticated
v3/v5 checks confirmed all 11 Import controls, both custom-list fields, absence
of the six removed status flags and a hidden blank legacy scalar. The existing
Watching selection remained enabled. No owner's provider Test or import-list
sync was triggered. The immediate 41-line startup log window contained no Error,
Fatal or group-constraint message; this is a scoped startup observation, not a
promise that external providers will never return errors.

`deployment-report.json`, the read-only schema receipt and `deployment.log`
record local execution. The local OCI revision label is descriptive rather than
a Git commit; a hosted image must identify its actual committed source and pass
its own workflow checks. At this checkpoint, pushing this later source and
publishing its `edge` image were still pending. The immutable `v1.0.1` tag,
release assets and historical verification remain unchanged. Check the actual
[main source](https://github.com/KvasExpert/Tanukarr/tree/main),
[Actions runs](https://github.com/KvasExpert/Tanukarr/actions) and
[GHCR package](https://github.com/users/KvasExpert/packages/container/package/tanukarr)
for later source/publication identities rather than treating the local image ID
above as a future registry digest.

Ignored root-check receipts are under
`_artifacts/anilist-custom-exclusions-only-2026-10-13/`; focused frontend/doc
receipts are under `_artifacts/anilist-customlist-only-2026-10-13/`.
Earlier [1.0.1 verification](releases/1.0.1-verification.md),
[AniList/browser reliability](anilist-react-reliability-2026-10-13.md) and other
dated reports retain their original tested source/image identities and counts.
Their remaining dependency findings are not reclassified as fixed by this UI
and settings cleanup. The new exact-image findings are recorded separately above.

## Attribution

The current simplification, backward-compatible normalization, frontend and
backend regressions, runtime fixtures and English documentation use **OpenAI
Codex based on GPT-6**. Original AniList provider/list authors, upstream Sonarr
authors, their source commits and inherited AI declarations remain credited in
[CONTRIBUTORS.md](../CONTRIBUTORS.md). Local verification claims record the
checks actually run and their limits.
