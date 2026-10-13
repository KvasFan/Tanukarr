# AniList membership and inclusion-priority checkpoint

This report covers the owner-requested changes after the
[settings cleanup checkpoint](anilist-settings-cleanup-2026-10-13.md), based on
`main` commit `ba8587540876bb23d91ab87e2b5ad016bbd610ee`. It is separate from the
immutable Tanukarr 1.0.1 release and its verification report. The product version
remains 1.0.1; no new release or replacement of an existing tag is part of this
change.

## Requested behavior

An explicitly **Included Custom Lists** match wins over an **Excluded Custom
Lists** match. An entry must still pass its enabled account-list status and media
release-status filters. Watching alone does not override an excluded custom
list; the priority comes from a named included custom list.

Included names are alternatives. If any included names are configured, an entry
must match at least one. If the included names are empty, custom-list exclusions
still veto an otherwise eligible entry. Eligible explicit inclusion is retained
by native AniList media ID across pages, so an earlier or later excluded copy
cannot undo it. Exact names remain distinct; the existing case-insensitive
fallback is accepted only when it identifies one name.

The obsolete six Exclude status controls remain absent. The eleven Import
checkboxes, literal-name custom-list tags, OAuth controls and hidden legacy-scalar
compatibility remain available. The visible explanation states the inclusion
priority and the remaining status requirements. See
[Library tools](library-tools.md#anilist-lists) for examples and the selection
table.

## Reported intermittent failure and live investigation

The previous deployment still logged the missing-custom-membership guard after
its 16:35 UTC deployment, including a warning at 16:48:42.9 UTC on October 13.
This establishes that the reported error could occur in that running build. It
does not establish which upstream response field was missing or why.

Two small, anonymous queries to AniList's official GraphQL endpoint were
executed at 17:06:55 and 17:08:37 UTC, more than a minute apart. Both returned
HTTP 200 without GraphQL errors. Each sampled Page response had three entries
with non-null membership maps and non-null array representations. Array entries
used the documented `name` and `enabled` properties. The second sample selected
Watching/Planning states; all three returned entries were Planning.

The first query also inspected the target account's public collection. Its user
identity matched the queried account and `hasNextChunk` was false. It exposed
two defined custom-list names, one populated custom-list group, 412 unique media
IDs and 523 occurrences across standard/custom groups. The reported target list
contained 111 occurrences. Repeated entries across groups explain why the
occurrence count exceeds the requested chunk size. This anonymous view does not
establish whether an omitted defined custom list was empty or private. No
OAuth credentials or private settings were used in these public queries, and
the null-membership failure was not reproduced by them.

The owner's existing authenticated connection was also tested without saving,
syncing or requesting downloads at 17:10:43 UTC. The unsaved test used the reported
excluded custom-list name and no included names. It returned HTTP 204 without
validation errors, and the stored definition was unchanged. This is a real
account connection test, not proof of an imported or updated real library. The
intermittent failure was not reproduced during that test.

AniList's primary references describe the nullable
[MediaList membership field and its array argument](https://github.com/AniList/docs/blob/master/docs/reference/object/medialist.md),
the [collection's owner and chunk-completion fields](https://github.com/AniList/docs/blob/master/docs/reference/object/medialistcollection.md)
and [named standard/custom groups](https://github.com/AniList/docs/blob/master/docs/reference/object/medialistgroup.md).
The live observations support the response-shape handling; they do not identify
the cause of the intermittent missing-field response.

An additional complete public collection query at 17:31:53 UTC confirmed the
selected subset, retaining counts rather than media IDs. With Watching selected,
Finished/Releasing media states and no included names, 99 public entries were
eligible before the reported custom-list exclusion. Of these, 55 belonged to
the excluded list and 44 remained. This demonstrates that these public filters
did not produce an empty result at that checkpoint; it does not verify a private
import or authorize changing the saved list. The response had a matching owner,
`hasNextChunk: false`, HTTP 200 and no GraphQL errors.

The first attempt at that additional query returned HTTP 403. Its failed receipt
was preserved, and no partial counts were accepted. More than a minute later,
one request using the same explicit Tanukarr User-Agent as the earlier public
queries succeeded. This observation does not establish why the first request
was rejected; no further retries were issued.

## Missing-membership recovery

The normal page membership map remains the first source. An explicit, valid
`customLists(asArray: true)` response remains sufficient when the map is missing;
known empty membership is distinct from unknown membership.

The new recovery path requests a collection only when a membership-dependent
filter needs information unavailable in both shapes. It uses the same target
username and authentication context as the page request and passes through the
shared AniList pacing and retry service. The collection snapshot is scoped to
one fetch or connection test, rather than cached across account operations.

The snapshot must validate the target user and collection owner, list
definitions, typed group markers, positive anime media IDs and complete bounded
chunk pagination. Requests use 500 entries per chunk, with a maximum of 100
chunks; the final response must explicitly report no next chunk. Repeated
occurrences in standard/custom groups are merged by media ID rather than
incorrectly counted as extra unique entries. Every sparse Page entry must be
present in that complete collection. A covered entry's membership can then be reconstructed from its
named custom groups. Malformed data, a wrong owner, a missing requested ID,
GraphQL/HTTP errors or incomplete chunks retain the previous successful list.
An anonymous public view must not be described as proof of private membership.

AniList's [full-list query guide](https://github.com/AniList/docs/blob/master/docs/guide/graphql/queries/media-list.md)
also documents a limit of 11,000 recently updated unique collection entries.
The fallback cannot recover an older sparse Page entry omitted by that provider
limit; the explicit Page-ID coverage check retains the previous list instead of
inferring that entry's membership. Standard and custom groups are both read,
including entries hidden from standard status groups.

## Verification state

The new local Linux amd64 package has immutable image ID
`sha256:256747315347632afcdb1589da2292cc91c81ddc8116bffbeb204a82e167e883`,
configuration digest
`sha256:5229f6976f6a16bf9d7b48000658cad21660b6f267103aa0fa7b859fd29aba07`
and descriptive revision label `anilist-inclusion-membership-2026-10-13`.
Its actual packaged status identifies Tanukarr 1.0.1, core `10.0.0.31619`,
.NET `10.0.12` and migration `247`. The local worktree source is based on the
commit stated above; that descriptive label is not a published source commit.

The expanded disposable test was first run against the previous immutable local
image `sha256:de7ff7883b8a577c4fa592faad1507c915d23ae71ba23bc4bd02925f1b96a77b`,
with revision label `anilist-custom-exclusions-only-2026-10-13` and core version
`10.0.0.29390`. Seventeen baseline checks passed before the new overlap check
failed with `Explicit custom-list inclusion did not override membership in an
excluded custom list`. Its owned fixture was cleaned. This demonstrates the
requested priority differed from the old behavior; it does not reproduce the
live null-membership failure. The before-change receipt is separately retained
under `_artifacts/anilist-membership-include-precedence-2026-10-13`.

The first native solution build completed in 48.57 seconds with zero warnings
and errors. The first focused backend run passed 338 of 339 checks; its old
space-preservation assertion still expected exclusion to win. That test was
corrected for the requested priority, and a separate exclusion-only assertion
continues to verify literal spaces. The final native solution rebuild completed
in 11.40 seconds with zero warnings and errors. All 339 focused backend checks
passed with no skips, and all 190 API checks passed. These are distinct suite
scopes; later Docker suite counts must not be added to them as new unique tests.

All 77 frontend tests passed, together with ESLint, Stylelint, TypeScript checking
and the Vite build. Vite completed in 2.48 seconds and retained the existing large
bundle warning. An initial PowerShell wrapper treated that warning written to
stderr as an execution failure; the final run checked Vite's native exit status
and passed. Semgrep checked five changed production C# files with 27 C# rules and
reported no findings. This is scoped source scanning, not a complete security
guarantee.

The disposable AniList reliability fixture passed all 30 checks on the new
immutable image, and the functional/security fixture passed all 16 checks.
The AniList checks exercised explicit inclusion winning within one page and in
both duplicate-page orders, unchanged account/media state requirements, and
exclusion remaining effective when no included name matched. A typed membership
array needed no extra collection request. A simulated response with both shapes
missing recovered through a complete two-chunk collection, fetched once for
that sync; a represented entry's known empty membership was also accepted.
Incomplete chunks, a missing media ID, a wrong owner and unavailable collection
data retained the prior successful rows and failed Test safely. A later complete
response recovered. TLS validation remained enabled, external provider routing
was disabled, and the owned fixture was cleaned. Its stopped-fixture legacy
settings seed also passed under host UID 1001, with the app restoring UID 1000.

These sparse/null responses were deliberately simulated. The fixture did not
reproduce the intermittent failure against the live private account.

The complete Linux Docker backend suites passed 7,222 checks, with 27 ignored:
Core 6,874, API 190, HTTP 57 and Common security 101. These broad counts include
the relevant focused cases; they are not additional unique tests to add to the
native focused/API counts above. The final image also passed all seven required
disposable runtime gates:

| Runtime gate | Passed checks |
| --- | ---: |
| Smoke | 12 |
| Functional/security | 16 |
| Series/anime/movie groups | 21 |
| Nyaa query handling | 9 |
| Complete-series and season packs | 13 |
| AniList reliability and membership priority | 30 |
| Group import collisions | 12 |
| Total required runtime checks | 113 |

All seven reports identify the same immutable local image. The five packaged
browser checks below are separate, giving 118 executed runtime/browser checks
across those scopes. This does not mean a private-account import or a media
download was performed.

The actual packaged UI passed all five Playwright 1.64 Chromium menu checks on
that immutable image. The visible AniList modal displayed the complete new
inclusion-priority explanation. All eleven Import controls remained separate;
literal included/excluded tags preserved commas, while the six removed status
exclusions and obsolete scalar textbox stayed absent. An unsaved Test with
missing OAuth returned the expected local validation HTTP 400; Cancel preserved
the empty fixture's saved lists, library and acquisition settings. The browser
reported no unhandled JavaScript errors or forbidden Sonarr-cloud requests.
Only generated Forms credentials were used, no live metadata lookup or owner
configuration was accessed, and the owned fixture was cleaned. Its three setup
checks are ancillary and are not added to the five browser checks or mandatory
container totals.

The browser helper also passed Node 24 syntax checking, changed Python helpers
parsed without imports and the English localization resource parsed as JSON.
The maintained-document check passed for 17 documents and 237 local file links,
unchanged version metadata and the unaltered supplied PNG. It does not check
Markdown anchors or remote publication.

Trivy scanned the new immutable image and reported zero Critical findings,
21 High package occurrences covering three distinct vulnerabilities in
ImageSharp `3.1.12`: `CVE-2026-106110`, `CVE-2026-106113` and
`CVE-2026-106115`. The scanner advertises `4.1.2` as the fixed version; these
dependency findings remain unresolved. No image secrets were detected. This
High/Critical scan and the scoped Semgrep result do not establish that every
lower-severity finding or every possible application weakness is absent.

## Local deployment and publication boundary

The same tested immutable image was deployed to the local instance at
17:44:39 UTC on October 13. A fresh, consistent private backup was taken before
the change. SQLite integrity passed; all 35 protected configuration/library
tables and the `config.xml` hash were preserved exactly. The existing 100 series,
one configured list, 99 saved list items and authentication settings remained.
The instance at `http://localhost:6970/series` reported the new core
`10.0.0.31619`. No older test snapshot replaced the owner's newer library.

Two real authenticated unsaved owner Tests passed HTTP 204 after deployment at
17:45:43 UTC. One used only the reported excluded custom-list name; the other
used that same name in both Included and Excluded Custom Lists. The saved
definition remained exactly unchanged. Neither test saved settings, started a
sync or requested downloads. The live missing-membership response was still not
reproduced. The public 44-remaining/55-overlap counts above come from the separate
anonymous collection observation, not from these connection-test responses.

A follow-up database check at 17:47:08 UTC confirmed SQLite integrity and the
same exact 35-table/configuration preservation against the fresh backup, still
with 100 series, one list and 99 saved list items. Disposable test resources were
removed, leaving the main application container.

At this checkpoint, hosted publication of the new source is not yet recorded
here. Earlier reports retain their original results and
image identities; they are not verification of these changes. The immutable
1.0.1 release/tag/stable images remain separate. A later published development
image must be identified through its committed source and hosted CI/registry
receipts rather than this local descriptive revision label.

Ignored local receipts are under
`_artifacts/anilist-inclusion-membership-2026-10-13`. They are not included in a
normal clone. Public-research receipts retain shapes and counts rather than
media titles, tokens or raw account payloads.

## Attribution

These changes adapt the existing AniList integration rather than replace its
original authorship. The current local implementation and documentation use
**OpenAI Codex based on GPT-6**. The
[contributor register](../CONTRIBUTORS.md) retains the source authors, original
commits and declared inherited AI credits.
