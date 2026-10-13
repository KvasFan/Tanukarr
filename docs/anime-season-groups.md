# Series, anime and movie groups

Groups show related catalogue entries as **one title with separate
group seasons and movies**. They support TV series, anime and movie sagas.
A movie-only saga can use numbered film folders.
Each member remains its own series, with its original metadata
provider, catalogue ID, episodes, monitoring, files and download history. The
group season is a display and folder position; it does not replace the owning
catalogue's episode numbering.

This feature is retained in **Tanukarr 1.0.3** and first shipped in 1.0.1 after the
immutable 1.0.0 release, whose source and image retain their original scope.
Use 1.0.3 after verified publication or an `edge` image containing these changes.
Check the image's source revision and the
[1.0.3 verification report](releases/1.0.3-verification.md) for new checks and
publication state. The [1.0.2 report](releases/1.0.2-verification.md) records
its earlier packaged checks separately.
The [1.0.1 report](releases/1.0.1-verification.md) and original
[grouping verification report](anime-season-groups-verification-2026-10-13.md)
retains its earlier source/image checkpoint.

## Choose how a title is grouped

Use **Grouping** when adding or editing a title. These controls
are available for all catalogue owners and Series Types. Automatic official
relation discovery applies to AniList/MyAnimeList owners; other owners can use
Manual or Disabled. Choose the grouping mode:

| Mode | Behavior |
| --- | --- |
| **Automatic** | For AniList/MyAnimeList owners, use the catalogue's explicit prequel/sequel links when they form a supported, unambiguous chain. |
| **Manual** | Choose an existing group or create a group, select Season or Movie membership and assign the corresponding number. This overrides automatic discovery for that member. |
| **Disabled** | Keep this title outside the visual group. Disabling grouping does not move its files or restore an earlier path. |

Automatic is the default for newly added entries. An unsupported, unrelated,
ambiguous or unavailable relationship lookup leaves the series available on its
own. Group discovery does not add related titles to the library, change their
monitoring or start a download. Add each desired catalogue entry explicitly or
through your configured import lists.

The grouping controls are separate from **Series Type** and **Episode
Ordering**. Choose the metadata owner and parsing type that match the title;
see [Metadata providers](metadata-providers.md).

## What automatic relationships mean

AniList calls these links `PREQUEL` and `SEQUEL`; MyAnimeList/Jikan exposes
`Prequel` and `Sequel` relations. A sequel points from the earlier entry to the
later entry, while a prequel points back. These are catalogue-declared links,
not guesses from a similar title or from a label such as "2nd Season". See
the official [AniList relation definitions](https://github.com/AniList/docs/blob/master/docs/reference/enum/mediarelation.md),
[AniList relationship edge schema](https://github.com/AniList/docs/blob/master/docs/reference/object/mediaedge.md)
and [Jikan relations resource](https://github.com/jikan-me/jikan-rest/blob/master/app/Http/Resources/V4/AnimeRelationsResource.php).

Automatic grouping supports anime entries with the formats **TV**, **TV_SHORT**,
**ONA** and **MOVIE**. It requires a consistent, non-circular prequel/sequel
chain. Movies become movie members rather than consuming a season position.
OVA, specials and music videos are excluded from automatic membership.
Side stories, spin-offs, alternate versions, summaries, adaptations and
shared-universe links do not establish automatic season membership. AniList's
[format definitions](https://github.com/AniList/docs/blob/master/docs/reference/enum/mediaformat.md)
and Jikan's [anime schema](https://github.com/jikan-me/jikan-rest/blob/master/app/Http/Resources/V4/AnimeResource.php)
describe these separate catalogue formats.

A conflicting episodic branch, missing entry or lookup limit requires a manual
decision. Relation discovery is bounded; it does not crawl an unlimited
franchise graph. Partial dates, release titles and catalogue IDs are not used to
invent a viewing order. A catalogue's prequel/sequel relationship also does not
guarantee a particular media server's season numbering.

For a newly resolved mixed chain, the first episodic entry supplies the canonical
anime title/identity. Season numbers count only TV/TV_SHORT/ONA members, and
movie numbers count only movie members. A TV → Movie → TV chain therefore has
Season 1, Movie 1 and Season 2. A chain containing only movies is a **Movie
Saga**, rooted at its earliest movie. When an official TV sequel runs beside a
movie alternative, the episodic chain can still supply the TV seasons. That
parallel movie requires a manual decision because its film position is not
unambiguous. A movie's presence does not authorize flattening a TV/TV branch or
treating a TV recut as an additional film.

Automatic groups stay within the selected AniList or MyAnimeList owner. They do
not use fuzzy title matching to merge entries across providers. TheTVDB/TMDB
entries that already contain multiple native seasons retain their normal
multi-season behavior. A multi-season catalogue entry cannot be collapsed into
one group-season slot.

Once a valid membership is stored, later catalogue changes do not silently
renumber it. A manual assignment is retained through refresh. To correct a
group, edit its members deliberately; a failed relationship request does not
authorize moving files or discarding an existing assignment.

## Manual groups and corrections

Use **Manual** when the catalogue has an ambiguous relationship, when your
library needs a different grouping, or when a supported title should join an
existing group. The **Group** dropdown lets you choose a group or **Create a
new group**. Set **Group title** and choose a **Series** group or **Movie Saga**.
Select **Season** or **Movie** membership, then set its separate season/movie
number. Use that same group for subsequent members. An existing group keeps its
shared title and folder identity. A Movie Saga contains movie members; a mixed
group uses the Series layout.

Season and movie numbers must be **1–999** and unique within their member kind
and group. Season 1 and Movie 1 can coexist; two movie entries cannot both be
Movie 1 in that group. Each member must represent a
single native catalogue season. Group titles must be valid nonempty text; the
generated directory name is sanitized for filesystem use.

Version 1.0.1 also rechecks member slots immediately before adding series to the
database. If a manual addition or concurrent update occupies a prepared list
candidate's slot while its metadata is loading, the conflicting candidate stays
pending and other valid additions can be saved. Import-list membership remains
authoritative; an occupied slot does not remove a title from the saved list.
Later eligible syncs may retry it, but do not overwrite the existing member or
assign a different number automatically. Correct an unresolved conflict through
the grouping controls. A manual same-kind duplicate returns a validation error.
The database's separate season/movie unique indexes remain enabled. See the
[1.0.1 report](releases/1.0.1-verification.md) for the reproduced SQLite failure,
implemented safeguards and verification scope.

Manual grouping can organize a single-season entry without changing its
metadata owner. It does not combine episode records or make a title from one
catalogue inherit another catalogue's IDs. Use **Disabled** for a title you want
to keep separate. Switching modes changes membership, not the registered file
location of an existing series.

## New folders and existing libraries

For a newly added grouped entry whose path is generated automatically, the
layout is:

```text
Selected root/
  Canonical anime/
    Season 01/
    Season 02/
    Season 03/
```

With the default expressions, a mixed group stores films by their native title:

```text
Selected root/
  Canonical anime/
    Season 01/
    Movie title/
    Season 02/
```

A movie-only saga instead uses `Saga title/Film 01`, `Saga title/Film 02` and
so on. **Movie folder style** can explicitly choose **Title** or **Numbered**;
**Automatic** uses title folders in Series groups and numbered folders in Movie
Saga groups. The movie number is separate from the season number.

For seasons, **Folder style** can choose **Automatic**, **Numbered Season** or
**Story Arc Title**. Automatic chooses an arc folder only when the native title
starts with the exact canonical group title, followed by a colon or dash, and
the subtitle ends in `Arc`, `hen` or `Chapter`. For example,
`Tokyo Revengers: War of the Three Titans Arc` becomes
`Tokyo Revengers/War of the Three Titans Arc`. A numbered title such as
`The Apothecary Diaries Season 3` instead uses
`The Apothecary Diaries/Season 03` when its official chain assigns season 3.
Numbered Season always uses the numbered expression. Story Arc Title uses the
strictly derived subtitle, or the complete native title when that prefix is
absent. This naming choice does not infer a group identity from title similarity.

Each member is registered at its own leaf. Its **Season Folder** setting is
disabled for this generated layout, preventing an extra nested `Season 01`
directory in a season or movie folder. Other per-series options remain attached
to the individual member. Converting an existing movie saga to a mixed group
does not rename the already stored movie folders.

## Configure folder and filename expressions

Under **Settings > Media Management > Episode Naming**, configure these five
expressions:

| Expression | Default | Required token |
| --- | --- | --- |
| Anime group folder | `{Group Title}` | `{Group Title}` |
| Group season folder | `Season {Group Season:00}` | `{Group Season}` |
| Group story arc folder | `{Group Arc Title}` | `{Group Arc Title}` |
| Group movie title folder | `{Series Title}` | `{Series Title}` |
| Group numbered movie folder | `Film {Group Movie:00}` | `{Group Movie}` |

The expressions produce one directory leaf, rather than a path. Slashes,
backslashes, control characters, unknown tokens and invalid token formats are
rejected. Generated title values are cleaned for filesystem use, reserved names
are guarded and each leaf is limited to 180 UTF-8 bytes. A registered destination
must also pass the normal containment, overlap and collision checks.

Folder expressions support `{Group Title}`, `{Group Arc Title}`, `{Series Title}`
and `{Series Year}`.
Season expressions additionally support `{Group Season}`, while the two movie
expressions support `{Group Movie}`. `:00` pads a number to two digits; number
formats may contain one to six zeroes. Title tokens accept a numeric truncation
length from 1 to 180. The naming page displays examples, including a movie
filename example.

The existing standard, daily and anime filename expressions also accept
`{Group Title}`, `{Group Arc Title}`, `{Group Season}` and `{Group Movie}`. A number belonging to the
other member kind is empty, and all group tokens are empty for an ungrouped
entry. Optional token prefixes/suffixes follow the existing naming rules. Native
episode tokens remain required by the normal filename validation; a group
number cannot replace `{season}` or `{episode}`. For example:

```text
{Group Title} - {Series Title}{ - Group Season:00}{ - Group Movie:00} - S{season:00}E{episode:00}
```

Changing expressions does not move existing files or rewrite stored group/member
folder leaves. The new settings affect newly generated destinations; use the
normal, reviewed move or rename workflow for existing members. Existing API
clients that omit the five new naming fields retain their saved expressions.

## Existing libraries and native numbering

An explicit path supplied when adding a title is retained. Assigning a visual
group to an existing series during refresh or editing retains its registered
path and existing files. Grouping does not automatically move, rename or delete
an existing library. To arrange existing folders under a common group root,
review the destinations and use the normal series path/move workflow. Back up
the configuration first and wait for the move task to succeed before changing
another member's path. See [Library tools](library-tools.md#imports-and-existing-files).

New generated destinations must stay inside the chosen root. Overlapping
registered series paths and duplicate same-kind member slots are rejected; the
feature does not place a new season inside another series' registered directory.
For example, if an existing series owns `Root/Canonical anime`, arrange it in
its own leaf deliberately before adding a new grouped member below that parent.

**Folder position does not remap episode numbers.** A second catalogue entry
can still have native `S01E01`, and an imported or renamed filename can retain
that number inside the group's `Season 02` directory. Search, import matching,
episode counts and configured filename formats keep the member's native
identity and numbering. Grouping alone therefore does not guarantee that
Jellyfin or another server identifies this directory as season 2 of a shared
TheTVDB/TMDB show. Configure the media server and naming to match the catalogue
you use; the [Jellyfin refresh connection](jellyfin.md) requests a scan rather
than translating those IDs or episode numbers. A grouped movie can likewise
retain a native `S01E01` filename inside `Movie title` or `Film 01`.

AniList and MyAnimeList/Jikan metadata can represent anime movie entries in this
series-based application. An explicitly classified movie with an unknown or
zero episode count receives one native `S01E01`; positive counts and existing
episode identities are preserved. This is separate from grouping. TheTVDB and
TMDB integrations in this fork use their TV catalogues, and grouping does not
add a TMDB movie endpoint or a general movie manager. A manual Movie membership
changes presentation and folders, rather than inventing a movie catalogue ID.

## Library views and individual actions

The poster, table and overview library views present members under one anime
group. Use **Show groups** or **Show individual series** in the library
toolbar to choose the presentation. Open the group to view its **Seasons** and
**Movies** sections, with separate ordinal labels and each native entry's own
details/actions.
Episode and file totals come from the stored member series; grouping does not
manufacture missing seasons or change episode counts.

Select or open an individual member to manage its monitoring, quality profile,
searches, files and other series actions. A group is a presentation of those
series, not a replacement series with a shared metadata identity. Library
filters continue to determine which members are visible. Selecting a group
summary selects its currently visible filtered members, so bulk actions still
operate on those native series.

**Search Season Packs** on a native season or grouped season entry requests pack
results for that member's native series ID and season number. A logical Season 2
that owns native Season 1 therefore searches the native season. The regular
interactive search remains available for episode results. Pack search filters
results; it does not download them or translate catalogue episode numbering.

## Verification and attribution

The [dated verification report](anime-season-groups-verification-2026-10-13.md)
records the final source/image and executed backend, frontend and runtime
checks. Earlier 1.0.0 test counts do not verify this post-release feature. Public
catalogue research and offline fixtures are distinct from a successful
authenticated account sync or imports into the owner's real media library.

This local feature, its English guide and verification work used **OpenAI Codex
based on GPT-6**. The AniList/Jikan APIs provide relationship data; that credit
does not claim authorship of their catalogues. Original provider implementations
and upstream authors remain credited in
[CONTRIBUTORS.md](../CONTRIBUTORS.md).
