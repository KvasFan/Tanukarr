# Nyaa search verification

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](README.md) and [Tanukarr 1.0.0 verification](releases/1.0.0-verification.md). Original SonarrWeeb names, versions, image references and test results below describe the recorded build and remain unchanged.

This October 13, 2026 checkpoint checks the native Nyaa indexer following the
AniList request-limit correction. No real library or acquisition is used.

## Protocol and inherited defect

The public Nyaa RSS endpoint returned HTTP 200 for a title query during this
investigation. Both legacy and modern query parameter names selected the title
correctly. Nyaa's [own request implementation](https://github.com/nyaadevs/nyaa/blob/master/nyaa/views/main.py)
accepts `page=rss`, `q`/`term`, `c`/`cats` and `f`/`filter`; legacy names alone
are not a search defect.

Before this correction, the Nyaa implementation and its existing fixtures
matched the integrated upstream snapshot `57816c149`. Its query preparation
only replaced spaces with `+`. Raw episode/season titles can therefore inject
extra parameters through `&`, truncate the transmitted query at `#`, or turn a
literal `+` into a word separator. The correction URL-encodes title values while
keeping spaces and episode-number formatting compatible. Special-episode
queries normalize the existing cleaned `+` word separators before encoding.
Configured category/filter/uploader parameters are preserved.

The immutable `v0.1.0` release and earlier AniList-only correction do not contain
this encoding fix. Native Nyaa also requires a configured website URL; it has no
default URL. Anime absolute-number searches and complete-series title searches
are available; an anime season search without a season-specific alias requires
**Anime Standard Format Search**. Daily-series query methods do not generate
Nyaa requests.

## Executed checks

| Check | Result |
| --- | --- |
| Windows Debug solution | Passed; zero warnings and errors |
| Nyaa request and parser fixtures | 16 passed, zero failures or skips |
| New encoding regressions | Seven passed: reserved characters, Unicode, parameter/fragment injection and special-episode word separators |
| Broader indexer/search fixtures | 282 passed, zero failures, one existing AwesomeHD ignored case; includes the 16 Nyaa tests |
| Baseline public Nyaa Docker check | Ten checks passed; connection test and V3/V5 complete-series plus V5 episode queries returned parsed metadata |
| Baseline reserved-character fixture | Reproduced the malformed query at the actual HTTP boundary |
| Linux amd64 image build | Passed; Core 6,349, API 56, HTTP 57 and focused Common 96 passes; zero failures, 27 Core skips |
| Corrected image startup/authentication checks | 12 smoke and 16 functional/security checks passed |
| Corrected public Nyaa searches | Ten checks passed; same V3/V5 metadata queries succeeded |
| Corrected reserved-character HTTP fixture | Nine checks passed; both full terms and episode suffixes preserved |
| Semgrep 1.180.0 | 65 applicable rules on the changed production file, zero findings/errors; approximately 100% parsing coverage reported |

The public baseline used `sonarr-weeb:local`, image identifier
`sha256:74ef0cfab1539064700cad61b56cdc34495efe0d046547977fd716c760f0568f`,
containing the previous AniList correction. In a disposable instance, a native
Nyaa RSS connection test succeeded. A synthetic AniList-owned `Cowboy Bebop`
series returned six complete-series metadata records through both API v5 and
v3, and 85 absolute-episode metadata records through API v5. The test verified
torrent info hashes, sizes and seeder fields. Counts are a timestamped snapshot,
not an expected permanent catalogue size. No download clients, acquisition
commands, queue/history records, torrent files or media were created.

The synthetic library contains only one episode, so normal quality/episode
rules can reject packs from the returned metadata. These checks establish
search retrieval and parsing rather than an actual grab or import into a
populated library.

A separate local RSS fixture reproduced the defect for the synthetic title
`Weeb & Alpha #1 + Plus`. Both generated absolute-episode requests arrived
with `term=Weeb ` and an extra ` Alpha ` parameter: `#` truncated the rest of
the term and its episode suffix.

## Corrected image and deployment

The corrected local image is `sonarr-weeb:nyaa-fix`, identified by
`sha256:4bf51000f519b3d386ba4bf2ece29b636e3cda8820e60c11da1142f3fd9199fc`.
It was built from the tested working tree with OCI revision label
`nyaa-query-working-tree-2026-10-13`. Its runtime reports product 0.1.0,
internal version 10.0.0.66, .NET 10.0.12 and migration 243. The internal assembly
revision uses the existing time-based scheme and can reset across midnight;
the independently versioned product release and schema remain unchanged.

All **6,558 Linux backend tests** passed. The unchanged frontend stage reused
its previously verified build cache; no UI source was changed. On the corrected
image, the real public Nyaa queries again returned six complete-series records
and 85 episode records. The local fixture received exactly
`Weeb & Alpha #1 + Plus 1` and `Weeb & Alpha #1 + Plus 01`, with configured
category/filter parameters intact and no fragment or injected query parameter.
Both runs left queue/history empty, configured no download clients and created
no acquisition commands, torrent files or media. Their disposable containers
and data were removed afterward.

The existing Compose instance at `http://localhost:6970` was updated to the
tested image after a private stopped-container configuration/database backup.
Authentication, provider IDs and library identities/paths were preserved.
The live database had zero series both before and after this deployment, so
this does not establish migration or acquisition using a populated real library.
No existing indexer configuration or real-library search was changed or triggered.

Logs, TRX and disposable-container receipts are retained under the ignored
`_artifacts/nyaa-verification-2026-10-13/` directory.

## Attribution

Original Nyaa work remains credited to MythJuha and subsequent Sonarr/Nyaa
contributors in [CONTRIBUTORS.md](../CONTRIBUTORS.md). The local correction and
verification use OpenAI Codex based on GPT-6; no more specific model variant is
established.
