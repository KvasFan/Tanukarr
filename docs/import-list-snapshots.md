# Complete import-list snapshots and provider identity

Tanukarr 1.0.5 is being prepared to reduce duplicate list requests and evaluate
supported filters against complete import-scoped snapshots. This guide describes
the intended contract; executed results belong in the release verification
report. The published 1.0.4 results do not verify this new implementation.

## Read complete eligible data before filtering

A snapshot covers one configured account or explicitly selected remote list.
It does not download an entire public catalogue. Once all pages are validated,
each configured import definition applies its own selection locally. Included
custom lists are combined, excluded members are removed, and applicable account
status, release status and score filters remain independent conditions.

Definitions sharing the same provider, authenticated identity and request
projection can reuse a complete snapshot during one synchronization. A new
synchronization or unsaved Test starts a fresh scope. Different accounts, grants,
endpoints or server-side filter projections do not share private data. An
incomplete response, failed page or ambiguous identity retains the previous
saved membership rather than treating the failure as an empty list.

Existing refresh intervals still apply. A synchronization can use the last
saved membership for a definition that is not due; that is separate from raw
account-response retention. A due or explicitly forced read uses a new
snapshot, and standalone **Test** always evaluates fresh unsaved inputs.

| Provider | Applicable snapshot boundary |
| --- | --- |
| AniList account/custom lists | The selected user's complete ANIME collection, including every default and custom group; statuses and custom selection are evaluated locally. |
| MyAnimeList account lists | The official account API's complete paginated anime list; status, score, release state and list-tag filters remain local. Jikan provides public metadata separately. |
| TMDB lists | Each explicitly selected, complete remote list. Unselected lists are not downloaded. |
| Trakt lists | Each selected list and exact server-side filter projection. Arbitrary additional server parameters retain their server-side meaning. |
| MDBList | Each selected complete paginated list, followed by local set subtraction. |
| Plex Watchlist | The authenticated account's complete Watchlist, with native IDs and pagination totals validated. |
| SIMKL AUTH V2 | One staged account inventory/activity snapshot plus applicable selected custom memberships. The trusted private incremental baseline and cursor remain necessary between synchronizations. |

Private temporary snapshots are held in memory and references are released when
the operation ends, including failures. They are not written as raw response
files. Existing saved import membership is retained because synchronization,
exclusions and originating-account progress depend on it. SIMKL's existing
restricted baseline is retained for incremental correctness; it is not a raw
temporary export to delete after each import.

The shared retention budget is 64 snapshots, 8 MiB per retained snapshot and
32 MiB of measured serialized payload per operation. Providers also enforce
complete-response bounds. These limits describe retained payload, not a strict
CLR working-set limit or immediate secure erasure of immutable strings.

## AniList collection and grouping requests

AniList documents `MediaListCollection` for complete user lists and warns that
entries hidden from ordinary status groups can remain in custom groups. All
groups must therefore be considered. Its documented ceiling is 11,000 unique
entries and each collection chunk supports at most 500 entries. A response at
the ceiling cannot establish completeness for an oversized account, so it must
not silently replace a previously complete result. See the official
[media-list guide](https://github.com/AniList/docs/blob/master/docs/guide/graphql/queries/media-list.md)
and [query reference](https://github.com/AniList/docs/blob/master/docs/reference/query.md).

Complete direct relation data can populate the existing bounded public metadata
cache. This cache contains public catalogue relations, not private list status,
custom membership or OAuth credentials. Relations outside the downloaded
account collection and full metadata/airing refresh may still require network
requests. Fetching the lists first reduces duplicate work; it does not make
every subsequent metadata operation offline or remove AniList's quotas.

AniList's nullable progress/update fields do not determine list membership.
When custom-list options are null, names and membership are derived from every
custom group in the complete collection, including names first encountered in
a later chunk. Malformed identities, unknown group flags and incomplete or
changing responses still preserve the previous membership. An unused unnamed
standard group is valid; a custom group must have a usable name.

The request coordinator continues to honor the provider's remaining allowance,
reset time and retry headers. AniList documents a temporary reduced allowance
and a separate burst limiter in its
[rate-limit guide](https://github.com/AniList/docs/blob/master/docs/guide/rate-limiting.md).

## Choose an import metadata source

The planned common **Preferred metadata source** field applies to future
additions from that import list. **Automatic** keeps the existing selection
order. An explicit catalogue can be used only when the item supplies a verified
positive ID for it; otherwise automatic selection remains the fallback.
Existing series keep their current metadata owner, numbering and paths.

AniList, MyAnimeList, TMDB and TheTVDB have separate ID namespaces. A series
retains its chosen catalogue's native `metadataSource` and `foreignId`.
An internal negative `TvdbId` fills Sonarr's inherited unique database slot when
no verified positive TheTVDB identity is supplied. It is not a real TheTVDB ID,
an API failure or evidence that the title does not exist on TheTVDB. Reusing an
AniList number as a TheTVDB number could resolve an unrelated title.

Manual Add New Series already offers the four catalogue choices. AniList's
documented media resource supplies AniList and MAL identities, without a TVDB
field; choosing TheTVDB cannot manufacture a missing mapping. See the
[AniList media reference](https://github.com/AniList/docs/blob/master/docs/reference/object/media.md)
and the existing [metadata-provider guide](metadata-providers.md).

## Interpret local logs and storage paths

Log timestamps use the host/container clock. Server log entries such as
`[GET] /api/v5/system/status` describe incoming requests to the local Tanukarr
API. They are not, by themselves, requests to Sonarr's hosted infrastructure.
The older `[0]` addition message prints the incoming object's `TvdbId = 0`
before metadata preparation; it does not describe the database's local series ID.
New addition logs show the native provider identity and distinguish a real TVDB
identity from an internal placeholder.

`Root Folder Path: [/tmp]` comes from the selected import-list root. This path
does not describe temporary list-response storage. A container's `/tmp` is
ephemeral unless separately mounted. Choose a library root under a persistent
media mount for real downloads, and review existing paths before changing them.
No existing series path is moved merely by selecting a metadata preference.

Software licensing and remote-service access are separate matters. Sonarr is
licensed under GPLv3; the license permits running and modifying the software
subject to its distribution requirements. That does not grant unrestricted
access to a third party's infrastructure. This repository's cloud policy blocks
`services.sonarr.tv` and `sentry.sonarr.tv`; TheTVDB metadata still uses its
separate SkyHook route. This is a repository policy, not a declaration that
every HTTP GET is unlawful. See the
[GPLv3 text](https://www.gnu.org/licenses/gpl-3.0.txt) and
[Sonarr's published terms](https://forums.sonarr.tv/tos).
