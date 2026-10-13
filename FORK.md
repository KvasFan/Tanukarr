# What this fork changes

The reasoning behind Tanukarr's integrated changes, and the trade-offs they carry.
[README.md](README.md) covers what the features are and how to install; this file
covers why they work the way they do.

Metadata providers and import options are additive. This integration also changes
automatic acquisition: complete monitored seasons are skipped, and approved packs
are preferred before individual episodes. Explicit manual searches retain the
normal upgrade rules.

Tanukarr supports episodic TV and anime through the same library, monitoring,
search, download-client and import workflows. Provider selection follows the
catalogue entry for each title: TheTVDB/TMDB cover TV catalogues, while
AniList/MyAnimeList/Jikan cover anime catalogues. Episode ordering is selected
separately where alternatives are available. [Metadata providers](docs/metadata-providers.md)
documents coverage, credentials and the preserved provider identity.

Originally forked from upstream `v5-develop` at `7e627f6`.
Rebased through `698bb1dbb` on 2026-10-12. The upstream quality-rank migration
remains numbered 236 here; the upstream import-list tag migration is numbered
241 to preserve the fork's existing migrations 233–240. Migration 242 preserves
provider identities in import-list exclusions. See the
[integration report](docs/integration-2026-10-12.md) and
[additional gaboy features](docs/gaboy-integration.md) and
[ikatun features and validation](docs/ikatun-integration-2026-10-12.md).
The integrated history also contains the `59c22e859` translation,
imported-select and indexer-page-size checkpoint, followed by the final
`develop@57816c149` snapshot with development HTML-loading and API documentation
updates. [CONTRIBUTORS.md](CONTRIBUTORS.md) records original feature authors,
maintainers and source-declared AI co-authors. Local adaptation and hardening
were performed with OpenAI Codex based on GPT-6.

Tanukarr 1.0.0 is the renamed continuation of SonarrWeeb 0.0.1 and 0.1.0,
building on the credited Sonarr Pro source forks. The rename changes product
branding, logos, repository and distribution names while retaining Sonarr's
core assemblies, configuration/database filenames, migration history and API
routes. API v3 and v5 remain supported; v4 is not implemented.
Its product version is separate from
Sonarr's inherited internal core/API version. See the
[Docker installation guide](docs/docker-installation.md) for the published-image
configuration and [current release notes](docs/releases/1.0.1.md) for packaging
scope. Version 1.0.1 also includes the later grouping and AniList/browser
reliability work; the immutable 1.0.0 source and assets remain unchanged.

Use the [documentation index](docs/README.md) for current guides and dated
validation checkpoints. This file describes design; it does not replace the
tested-image and remaining-finding details in the
[security report](docs/security-functional-validation-2026-10-12.md).

---

## Turn-on checklist

Configure the services and optional tools needed for your library.

1. **Metadata provider access** — select a catalogue appropriate to each title
   and configure its required access under Settings → Metadata Source.
   See the [provider guide](docs/metadata-providers.md); selecting a provider does
   not configure its credentials or guarantee catalogue coverage.
2. **Supported episode ordering** — choose a TMDB group only for a TMDB-owned
   series, or keep Default. The inherited IMDb task/index and UI choices are
   present, but the current backend does not apply IMDb ordering; initializing
   its dataset does not make those choices functional. See the
   [ordering limitations](docs/metadata-providers.md#episode-ordering-is-a-separate-choice).
3. **Skip Unmonitored Episodes In Packs** — Settings → Media Management, off by
   default. See below for why.

Root folders, indexers, download clients and integrations need their own
configuration. Pack trimming is optional.

---

## Metadata sources

This fork lets each series be owned by one of four metadata providers, selected
from the dropdown above the search box on the Add New Series page. TheTVDB and
TMDB contain TV catalogues that can include anime. AniList and MyAnimeList/Jikan
contain anime catalogues; they are not general TV databases. Each selected entry
retains its owning provider; catalogue coverage and service availability vary.

| Source | Catalogue and metadata |
| --- | --- |
| TheTVDB | TV series, including anime present in its catalogue; compatible scene/XEM mappings where available |
| TMDB | TV catalogue, artwork, external IDs and episode groups where available |
| AniList | Anime entries, alternative titles, release status and user-list integration |
| MyAnimeList (via Jikan) | Anime entries and episode titles where exposed by Jikan |

The [provider guide](docs/metadata-providers.md) records current access
requirements. A provider's available titles and episode detail can differ from
another provider's. An unavailable source does not authorize changing a series'
metadata owner automatically.

Jikan answers from its own cache where it can and asks MyAnimeList otherwise, and
that second path frequently times out with a 504. Two consequences worth
knowing, both handled:

- **Requests are kept as plain as possible**, because Jikan caches on the whole
  query string. `?q=naruto` is answered from cache while `?q=naruto&limit=20` is
  a different key, misses, and times out. Result trimming and the adult filter
  are therefore done here rather than asked of the API.
- **A series still adds when its episode list is unavailable.** The list comes
  from MyAnimeList rather than the cache and fails more often than anything
  else, so the episodes it does return are kept and the rest are filled in from
  the count the series declares. Those arrive numbered but untitled, which is
  enough to search and import; the titles appear on a later refresh.

An uncached search may still come back empty while the other sources answer.
Check <https://api.jikan.moe/v4/anime/1> to tell a MyAnimeList outage from a
problem here.

**A series is owned by exactly one source.** They are selected, not merged. This
is deliberate and worth keeping: if two providers disagreed about how many
episodes a series has, episode rows would be created and deleted on every
refresh, which detaches episode files and makes Sonarr re-download content it
already has. Silent, gradual, and hard to reverse.

### Entries available in another catalogue

TV series, anime editions, recuts, spin-offs and specials can be represented
differently across catalogues. A title absent as a separate entry in TheTVDB can
be added from TMDB, AniList or MyAnimeList/Jikan when that catalogue has the
desired entry and the provider is configured. This preserves the selected
entry's identity; it does not infer that differently listed titles are the same
series.

Here it is added with a placeholder TVDB id, allocated as a negative number.
Every TVDB-specific behaviour tests for a positive id, so those paths skip
themselves: the series is searched by title rather than by id, gets no XEM scene
mapping, and shows no TheTVDB link. All three are fallbacks Sonarr already uses.

---

## Episode ordering

TMDB publishes alternate episode groups for some TV/anime entries. The current
backend applies those groups to **TMDB-owned series only**. Use **Default** in
the Episode Ordering selector for TheTVDB, AniList and MyAnimeList owners;
choosing a TMDB group for those owners is ignored. The inherited IMDb dataset
and UI options remain present, but their ordering application is not wired into
the current refresh/import path. IMDb is not a fifth metadata provider and its
ordering choices are not currently functional.

**TMDB group type 2 is "Absolute"** — one continuous run with no seasons, useful
when a TV or anime release set uses continuous episode numbers. Selecting it
produces a continuous season 1; exact counts depend on
the provider catalogue and chosen group. Every other ordering TMDB publishes appears in
the same list, so DVD order, story arcs and streaming-service orders come along
at no extra cost.

**The ordering is chosen when the series is added and cannot be changed
afterwards.** Season and episode numbers are written into file and folder names,
so changing it later would rename the library on disk. Fixing it at add time
means there are no episode files linked yet and nothing to rename. To change it,
remove the series and add it again.

Episodes an ordering does not mention keep their original numbering rather than
being dropped, so nothing already matched to a file is lost. Specials remain in
season 0 when the selected group omits them.

---

## Anime season and movie groups

The grouping feature included in 1.0.1 separates AniList/MyAnimeList catalogue entries
using explicit, supported prequel/sequel chains, including linear movie members.
Manual membership is available for every catalogue owner and Series Type. Disabled
mode let the user correct or decline those assignments. TheTVDB/TMDB entries
that already contain multiple native seasons retain their existing behavior;
there is no automatic cross-provider title merge.

Membership is additional per-series data. Each member keeps its metadata
source, foreign ID, episode records, native numbering and acquisition history.
The library can display one anime with season and movie members, or a movie-only
saga, without creating a new
metadata owner. A group-season label is not an episode-ordering projection.

Automatically generated new paths use a sanitized group folder and a distinct
member leaf, with `SeasonFolder` disabled to avoid nested native-season
folders. Explicit paths and existing files stay in place. Existing membership
is preserved rather than silently renumbered after a catalogue correction;
manual assignments remain authoritative. Season/movie ordinals are separate;
duplicate slots within the same member kind,
unsafe generated paths and overlaps with registered series directories are
rejected. Folder rearrangement uses the existing explicit path/move workflow.

Metadata preparation can finish minutes before persistence. Version 1.0.1
therefore revalidates member slots against the current library and prepared
batch inside a short addition lock immediately before insertion. A concurrently
occupied slot defers only that automatic-list candidate; valid rows can persist
and the returned additions drive the completion count. A narrowly classified
group-slot UNIQUE race can fall back to individual inserts after resetting IDs
assigned by the rolled-back batch. Unrelated constraint/database failures still
propagate. Manual duplicate assignments become validation errors. These changes
retain the unique indexes and existing member numbers instead of changing the
schema or renumbering occupied seasons/movies. PostgreSQL-specific classification
is reviewed in source; no PostgreSQL runtime verification is claimed. See the
[1.0.1 checkpoint](docs/releases/1.0.1-verification.md).

Five naming expressions configure the group, numbered season, story arc,
movie-title and numbered-film folders. Automatic story-arc naming requires the
exact canonical title prefix plus a colon/dash and a terminal Arc/hen/Chapter
subtitle; it never fuzzy-matches a metadata identity. Numbered and Title styles
override that folder choice. Stored leaves remain fixed when expressions change.
The group-title/arc/season/movie filename tokens keep mandatory native episode
tokens, and sample validation rejects additional fabricated season numbers.

Configured filenames retain their native member numbering, so a group season 2
member can still be native `S01E01`. This protects matching and does not promise
that a media server interprets every group as a shared TheTVDB/TMDB season list.
See [Anime season groups](docs/anime-season-groups.md) for catalogue rules,
controls, folder examples, the original grouping checkpoint and the
[1.0.1 verification report](docs/releases/1.0.1-verification.md).

## Downloading

### Fake release filtering — on by default

Sonarr fetches the `.torrent` file before handing it to the download client, and
that file lists everything inside. This inspects it and rejects four shapes
before anything reaches the client:

- a media extension followed by an executable one, such as `Episode.mkv.exe`
- executables, installers or scripts
- archives with no video alongside a file advertising a password
- neither video nor archives, so nothing to import

Split RAR sets with no loose video are common and legitimate, so they pass; the
password rule only fires when video is absent too. If the file list cannot be
read the grab proceeds — this is a safety net, not a gate.

Rejections are recorded with a specific reason. Silent rejections are what make
this kind of filter get switched off.

*Usenet is unaffected: it has no equivalent pre-grab file list. This fork is
torrent-focused.*

### Multi-season packs — on by default

Upstream rejects `Series.S01-S05` outright. The parser already recognised every
season and discarded all but the first so the release could be refused; this
keeps them, and maps the pack to the episodes of all its seasons.

A pack still has to match at least one wanted episode, so a Seasons 1-9 pack is
not accepted for a Season 3 search on the strength of its title.

Switch off under Settings → Indexers → Options if it misbehaves.

### Season pack trimming — off by default

A pack arrives whole, including episodes left unmonitored because they are
unwanted. With this on, those are skipped instead of imported.

Off by default because a file silently not appearing is harder to diagnose than
one that arrived and is unwanted. Manual imports are exempt: picking a file by
hand is a deliberate choice that outranks the monitored flag. A file covering
several episodes still imports if any one of them is wanted.

Files already held at equal or better quality were already protected upstream.

---

## Search and matching

Series lookup ranking now folds the many ways a season is written — `2nd
Season`, `S2`, `Part 2`, `II` all read as "season 2" — strips accents and
articles, and scores with Jaro-Winkler or word overlap. Upstream allowed a
single character of difference. The ranking supports alternate title and season
notations used by TV and anime releases. Where two entries
match a name equally well, which happens across a franchise that shares an
abbreviation, the more widely rated one comes first.

The *ranking* is used only for lookup, where a person picks from a list.
**Deciding which series a downloaded release belongs to is not loosened**,
because it runs unattended and a bad match files episodes under the wrong
series.

What release matching did gain is more names to compare against exactly. A
provider usually knows several titles for a series, and release groups pick
whichever they prefer: AniList holds One Piece's Fish-Man Island recut under its
romaji title, while releases of it use the English one. Both now match. A title
belonging to nothing in the library still matches nothing.

## Absolute and season numbering

A series on ordinary season ordering also accepts absolute-numbered releases:
`Tensei Shitara Slime Datta Ken - 88` resolves to S04E16, because every episode
carries an absolute number regardless of which ordering was chosen.

The reverse does not hold. A series added with an absolute ordering has no
season 4, so an `S04E16` release matches the series and no episode. **Season
ordering accepts both forms and is the more forgiving choice**; pick an absolute
ordering when the releases being taken are absolute-numbered, or when the series
is genuinely numbered that way, as One Piece is.

---

## Import lists

AniList imports target a user's account list through the configured authenticated
import-list connection. Public catalogue lookup needs no account; saving an
AniList import list requires the OAuth fields validated by the importer.
Private-account synchronization was not exercised in the recorded local tests.
The membership statuses
Watching, Planning, Completed, Dropped, Paused and Repeating describe the user's
relationship to an anime. Media statuses Finished, Releasing, Not Yet Released,
Cancelled and Hiatus describe the anime's release state. Custom-list membership
is a third, separate condition. At least one membership state and one media
release state must be enabled. A named custom list does not override either
state selection.

Current `main` supports multiple included and excluded custom lists. Account-list
states are selected through the existing Import checkboxes; the six separate
status-exclusion controls and their parser behavior have been removed. This
simplification follows the immutable 1.0.1 release and does not alter its tag or
images. Included names form an OR condition. Older `CustomList` scalar values
are transferred into `IncludedCustomLists` and cleared in the normalized settings;
the redundant textbox is not rendered, including under Show Advanced. Only the
legacy scalar is trimmed, matching its old parser behavior. Modern tag names
retain exact spaces, punctuation and case; an exact duplicate is not added
twice. Reading/testing does not rewrite the stored settings JSON, while a later
Save persists the normalized representation. Removing the transferred tag clears
that restriction rather than leaving a hidden scalar active. Empty inclusion
controls omit that condition.
An eligible match in an explicitly included custom list overrides excluded
custom-list membership. This override requires both enabled account-list and
media-release states; inclusion does not bypass either filter. Without explicit
inclusion, membership in an excluded custom list vetoes an otherwise accepted
Watching/Planning entry. This later `main` rule follows the initial exclusion-first
implementation; earlier tags and dated reports retain their original behavior.
The release-state filter remains an independent AND condition.
Custom names retain spaces and punctuation, including commas.

Exact name matches distinguish lists that differ only in case. A unique
case-insensitive fallback remains convenient for manual entry; an ambiguous
excluded name fails the sync when its exclusion must be evaluated, rather than
guessing. An eligible explicit inclusion is tracked by native AniList media ID
across pagination, so an excluded copy on an earlier or later page cannot undo
it. An unselected status or an unmatched custom name does not create that
override. Unknown required membership still fails the fetch unless the new
complete-collection fallback can validate it authoritatively.
The previous cached list is retained when the fetch fails.

Filters belong to one saved AniList connection. They do not write global
exclusion records or directly delete shows or files; existing list-cleanup
policy remains separate. Source attribution is preserved in
[CONTRIBUTORS.md](CONTRIBUTORS.md), and executed checks belong to the
[AniList filter verification checkpoint](docs/anilist-filters-verification-2026-10-13.md).
The [membership and priority checkpoint](docs/anilist-membership-priority-2026-10-13.md)
records the newer rule, public-response observations, authenticated unsaved Test
scope and final implementation/verification state.

Imported AniList entries keep `(AniList, ForeignId)` as their identity. Import,
preview, deduplication, exclusions and cleanup do not replace a separate anime
with a related TVDB parent or use an ambiguous synthetic TVDB number as ownership.
Failed or partial retrieval does not authorize destructive list cleanup. Search
cards and details link to the owning provider, not a negative TVDB placeholder.
See [Library tools](docs/library-tools.md) for configuration and the
[integration report](docs/integration-2026-10-12.md) for migration and tests.
The original custom-list design references
[Sonarr #6772](https://github.com/Sonarr/Sonarr/issues/6772).

Tanukarr 1.0.0 also shares AniList request pacing and server-directed cooldown
between import pages, tests, searches and metadata lookups. Temporary HTTP 429
responses use bounded retries and sanitized errors; a prolonged cooldown keeps
the previous cached list. This request policy is shared within one instance,
not between separate applications using the same public IP. See the
[request-limit checkpoint](docs/anilist-rate-limit-verification-2026-10-13.md).

Nyaa title queries now encode reserved characters before adding them to the
query string, preserving literal `&`, `#` and `+` in the title and configured
filter/category fields. Special-episode word separators remain compatible with
existing cleaned search titles. The inherited unescaped-query defect and
metadata-only runtime checks are recorded in the
[Nyaa checkpoint](docs/nyaa-verification-2026-10-13.md).

## Library tools and permission boundaries

[Library tools](docs/library-tools.md) documents automation pause, diagnostic
searches, import previews, retained versions, guarded restore and companion keys.
Companion `add-series` requests select a registered root and use the generated
series folder. They cannot supply a custom path or trigger searches through add
options. The administrator's master key retains its existing behavior.

The optional [compression worker](docker/compression/README.md) operates on
registered files and rechecks their association and identity before replacement.
Probe, encoding and output validation restrict input protocols and demuxers.
The main Docker image contains network-disabled ffprobe; it does not install the
host worker or imply hardware-encoder availability.

---

## Jellyfin library refresh

Tanukarr 1.0.0 includes a dedicated Jellyfin connection under Settings → Connect.
It targets one selected library, with immediate imports, conditional periodic
ticks (1 minute minimum, 60 minutes default), or both. Migration 243 stores
per-connection import generations and separate immediate/tick acknowledgements,
so failed requests and imports arriving during a request remain pending across
restarts. API-key rotation retains work; target changes and disabling the
connection clear stale work. Existing Emby / Jellyfin behavior is preserved.

The refresh uses Jellyfin's selected-item endpoint, never its global-library
refresh. Jellyfin queues scans asynchronously; accepting the request does not
establish that the scan finished. See [Jellyfin refresh](docs/jellyfin.md) for
configuration, trigger semantics, Docker networking and limitations. The
immutable SonarrWeeb 0.0.1 release predates this addition.

## Notes for whoever works on this next

- **The HTTP client deserializes with Newtonsoft, not System.Text.Json.**
  Provider resource classes must use `[JsonProperty("snake_case")]`.
  `[JsonPropertyName]` is ignored silently and every field defaults to zero or
  null with no error. This made episode groups entirely non-functional once, and
  no unit test caught it because they built resource objects in C# and never
  deserialized. `TmdbResourceDeserializationFixture` guards it now.
- **Services are auto-registered against every interface they implement.**
  `IProvideSeriesInfo` and `ISearchForNewSeries` are injected as single
  instances, so only `SkyHookProxy` may implement them.
  `MetadataProviderRegistrationFixture` pins this.
- **Anything assigning `Series.TvdbId` must guard `> 0`**, and anything sending
  it to an indexer likewise. Two bugs came from `!= 0`: one zeroed the id for
  TMDB-added series, the other wiped the placeholder id on the first refresh.
- **Build the whole solution.** Building `Sonarr.Core.Test.csproj` alone emits
  thousands of spurious StyleCop errors. Run tests with `--no-build` afterwards,
  and never build while a test run is in flight.
- Unit tests did not catch the serializer bug, the registration bugs, or the
  refresh bug. Starting the application and calling the API found all of them.
