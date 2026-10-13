# What this fork changes

The reasoning behind Tanukarr's integrated changes, and the trade-offs they carry.
[README.md](README.md) covers what the features are and how to install; this file
covers why they work the way they do.

Metadata providers and import options are additive. This integration also changes
automatic acquisition: complete monitored seasons are skipped, and approved packs
are preferred before individual episodes. Explicit manual searches retain the
normal upgrade rules.

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
configuration and [release notes](docs/releases/1.0.0.md) for packaging scope.

Use the [documentation index](docs/README.md) for current guides and dated
validation checkpoints. This file describes design; it does not replace the
tested-image and remaining-finding details in the
[security report](docs/security-functional-validation-2026-10-12.md).

---

## Turn-on checklist

Three things need doing before the new features do anything.

1. **TMDB API key** — Settings → Metadata Source. Free from
   <https://www.themoviedb.org/settings/api>. Without it, TMDB metadata and
   episode groups are silently unavailable; everything else works.
2. **IMDb index** — System → Tasks → Refresh Imdb Dataset, run once. Downloads
   the published episode dataset and builds a local index; sizes vary. Until then IMDb ordering does not
   appear in the dropdown. After that it refreshes itself monthly. The scheduled
   run deliberately does nothing until the index exists, so the download only
   ever happens because someone asked for it.
3. **Skip Unmonitored Episodes In Packs** — Settings → Media Management, off by
   default. See below for why.

Nothing else needs configuring.

---

## Metadata sources

Upstream resolves every series through SkyHook, which is a proxy in front of
TheTVDB. This fork adds three more sources and lets a series be owned by any of
them, chosen from the dropdown above the search box on the Add New Series page.

| Source | Good for | Needs |
|---|---|---|
| TheTVDB | Default. Scene mappings, which the indexer ecosystem is built on | nothing |
| TMDB | Artwork, episode groups, IMDb ids | API key |
| AniList | Anime. Lists recuts, OVAs and specials separately, and knows every alternative title | nothing |
| MyAnimeList (via Jikan) | Anime with per-episode titles, which AniList does not publish | nothing |

Jikan is a free public mirror of MyAnimeList, with no key and no account.

It answers from its own cache where it can and asks MyAnimeList otherwise, and
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

### Series that TheTVDB does not list

TheTVDB's editors sometimes decline to list content separately, folding a recut
or spin-off into its parent series, while TMDB and AniList list it in its own
right. Upstream cannot add such a series at all.

Here it is added with a placeholder TVDB id, allocated as a negative number.
Every TVDB-specific behaviour tests for a positive id, so those paths skip
themselves: the series is searched by title rather than by id, gets no XEM scene
mapping, and shows no TheTVDB link. All three are fallbacks Sonarr already uses.

---

## Episode ordering

TMDB publishes alternate numbering schemes per series, and IMDb's dumps carry
another. When a series has more than one, a dropdown appears on the add dialog.

The one that motivates this: **TMDB group type 2 is "Absolute"** — one
continuous run with no seasons, which is how long-running anime is released and
discussed. Selecting it produces a continuous season 1; exact counts depend on
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
single character of difference, which is useless for anime. Where two entries
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

Tanukarr 1.0.0 supports multiple included
and excluded custom lists, plus explicit exclusion checkboxes for all six
watching states. Included names form an OR condition, including the retained
legacy `CustomList` name. Empty inclusion controls omit that condition.
Membership in any excluded custom list or an explicitly excluded watching state
vetoes an entry, even when Watching or an included custom list would otherwise
accept it. The release-state filter remains an independent AND condition.
Custom names retain spaces and punctuation, including commas.

Exact name matches distinguish lists that differ only in case. A unique
case-insensitive fallback remains convenient for manual entry; an ambiguous
excluded name fails the sync rather than weakening the exclusion. Unknown
membership with custom-list exclusions configured also fails the fetch. Vetoes
persist across pagination before deduplication, so a later excluded copy of the
same AniList media ID cannot leave an earlier accepted copy in the result.
The previous cached list is retained when the fetch fails.

Filters belong to one saved AniList connection. They do not write global
exclusion records or directly delete shows or files; existing list-cleanup
policy remains separate. Source attribution is preserved in
[CONTRIBUTORS.md](CONTRIBUTORS.md), and executed checks belong to the
[AniList filter verification checkpoint](docs/anilist-filters-verification-2026-10-13.md).

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
