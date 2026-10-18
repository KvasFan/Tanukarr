# October 18, 2026 library-screen defect verification

This ledger records the checks actually executed on October 18, 2026 while
investigating six screenshots the maintainer took on the deployed Tanukarr
0.1.2 instance, fixing the defects they exposed and re-running the suites and
fixtures from the previous ledgers. It starts from source
`7dc2e6057` (the 0.1.2 publication commit) plus the changes committed with this
ledger. The deployed instance was not modified; every reproduction ran on a
disposable copy of its configuration.

## Reproduction environment

The screenshots show the library poster grid, the collection search modal and
the Settings → Quality screens. To reproduce them against the owner's real
catalogue without touching the deployment, `config.xml`, `sonarr.db` and the
`MediaCover` directory were copied to a Linux temporary directory, Forms
authentication was switched to `None` for local addresses with a generated API
key, every scheduled task was marked as just executed and the import list was
disabled; the indexer stayed enabled so collection searches behaved as in
production. A container started from the published 0.1.2 image (`c0a76e30…`)
served that copy on a loopback port and Chromium (Playwright 1.64.0) drove it
through the scenarios. The copy and the container were removed afterwards.

## Defects found and fixed

### Collection cards in the library grid stayed on the Tanuki placeholder

Screenshot 6 shows collection (group) cards with the placeholder while native
series cards show their posters. On the copy, scrolling the poster grid with
real wheel events left collection cards that mounted during the scroll on the
placeholder (8 of the 13 visible after the first wheel step, 2 of 15 still
after the last step and a further wait), while every visible native card
loaded at every step. `AnimeGroupSummary` rendered
`CollectionPoster` in its default lazy mode: inside the virtualized
`react-window` grid a card mounts after `react-lazyload` has already evaluated
the scroll position, so the lazy image never resolves. The native cards avoid
this with `lazy={false}`; collection cards now pass the same eager setting and
request the same 250 px artwork (the collection page hero keeps its eager 500
px cover). Regression tests cover the summary card in the poster and overview
views and the poster size defaults.

### Collection search showed no progress while searching

Screenshot 5 shows "Searching 0 of 1 entries…" with a disabled entry and no
other feedback. On the copy the "Assassination Classroom" search completed in
about 18 seconds (2 of 2 entries, 5 releases) but during that time the modal
showed only that static line: the count excluded the entry in progress and no
spinner was rendered, so the search looked stuck. The modal now shows a loading
indicator while any entry is pending, counts the entry being searched ("1 of
2"), and labels entries as searching, waiting or failed in the selector. A
regression test follows the states through two sequential searches.

### Saving quality-definition sizes re-mapped every pending release once per profile

Screenshots 1–4 show the quality profile modal, the Quality page profile
buttons, the codec table and the definition rows. Functionally these screens
worked on the copy (profile buttons open the matching modal, allowed-quality
and size edits persist and reopen correctly, a profile can be added from the
page, the codec table equals `/qualitydefinition/codecs`, definitions save and
apply to every profile, duplicate creates a custom preset that appears in the
schema, the profiles and the modal, delete/restore work, and deleting a quality
still allowed in a profile is refused with the profile names). What the copy
exposed instead was timing: with the owner's ~3,800 pending releases a profile
save took about 2.8 seconds because `PendingReleaseService` re-maps every
pending release on each `QualityProfileUpdatedEvent` (inherited upstream
behaviour), and a Quality page save took about 17 seconds because
`UpdateAllSizeLimits` published that event once per profile (six times). The
batch now publishes a single `QualityProfilesUpdatedEvent`, which the pending
release service handles once; a unit test verifies the single batch event and
the absence of per-profile events.

### Edits made while a Quality page save was in flight were discarded

With saves that long, a size edited after pressing Save Changes was wiped when
the earlier save completed, because the page cleared every pending edit on
success. `useManageQualityDefinitions` now records what each save sent and, on
success, forgets only those values; a field edited again during the save keeps
its newer value and is sent by the next save.

An adversarial review of the uncommitted changes (nine Claude Code agents: one
reviewer per change area, two independent refuters per finding; no findings in
the backend or cover/search areas) confirmed two low-severity gaps in the first
version of this change and split on a third, all fixed before the commit: a
submitted value reverted to its pre-save number during the flight was still
dropped as "unchanged" (edits now compare against the sent values while a save
is in flight), the `Ctrl+S` shortcut could start an overlapping save that the
single sent-snapshot could not attribute (a save is now ignored while one is in
flight; the toolbar button was already disabled), and an explicit clear of a
mixed limit made during another save was collapsed on success (the re-applied
edit now uses the same mixed-limit rule as a fresh edit). Four regression tests
exercise the deferred-save sequences.

## Windows source checks

| Check | Result |
| --- | --- |
| `dotnet build src/Sonarr.sln -c Debug` | Zero warnings, zero errors |
| Core (complete assembly, `IntegrationTest` category included) | **7,925 passed, 20 ignored, 0 failed** (3 m 49 s) |
| Profiles / qualities / pending-release fixtures (filtered) | **72 passed**, including the new batch-event case |
| API / HTTP / Host | **348 / 80 / 36 passed** |
| Common security subset (CI filter) | **113 passed** |
| Integration | **92 passed, 12 ignored, 0 failed** |
| Frontend | TypeScript, **218 tests** (211 existing + 7 new), ESLint, Stylelint passed |

## Linux image build

The amd64 image was built in the WSL engine from the working tree (label
revision `7dc2e6057…-dirty`, version `0.1.2`; the version was not bumped
because no release was published):

| Image | Source state | In-image results |
| --- | --- | --- |
| `sha256:7130515fb4fc78483d1d84c15c7a8087832ed85cf0e7337fb71bf03a4f60c4f6` | All fixes before the adversarial review | Frontend 215 tests, ESLint, Stylelint, TypeScript, Vite build; Core 7,872 passed / 27 skipped, API 348, HTTP 80, Host 32 / 4 skipped, Common subset 113 |
| `sha256:5862c561b386dde79f1b3191f9a475440640a74943405102ee39f0ab9b786558` | The committed sources of `676bc897d` (built from the working tree moments before the commit, so the label still reads `7dc2e6057…-dirty`) | Frontend 218 tests, ESLint, Stylelint, TypeScript, Vite build; Core 7,872 passed / 27 skipped, API 348, HTTP 80, Host 32 / 4 skipped, Common subset 113 |

The two images differ only in the Quality page hook (`useQualityDefinitions.ts`)
and its tests; the complete fixture run below used the first image and the
targeted re-run used the second.

## Disposable-copy browser checks on the fixed image

The same disposable copy of the owner's library was started from image
`7130515f…` and driven through the three screenshot scenarios with Chromium:

| Scenario | Published 0.1.2 image | Fixed image |
| --- | --- | --- |
| Poster grid, 17 collection cards mounted on load | 8 placeholders | **0 placeholders**, 17 covers loaded |
| Six wheel-scroll steps plus a wait (visible collection cards) | 8 / 13, 2 / 11, 4 / 18, 3 / 14, 1 / 12, 2 / 15 placeholders | **0 placeholders at every step**; every visible native card loaded in both runs |
| "Assassination Classroom" collection search (2 entries, 5 releases, ~18 s) | "Searching 0 of 1 entries…" static text, no spinner, entries disabled | Spinner visible, "Searching 1 of 2 entries…" then "2 of 2", entries labelled *searching…* / *waiting* / *(5)*, "Searched 2 of 2 entries" at the end, no console or page errors |
| Quality page: six profile buttons open the matching modal; Raw-HD toggled and reverted; Bluray-1080p preferred size changed, reopened and restored; profile added from the page; codec table (15 rows) equals the API; SDTV preferred changed and restored across all six profiles; edit matching; duplicate → custom preset listed in schema, profiles and modal; delete/restore; refused delete of an allowed quality names the profiles | All functional | All functional (22 checks) |
| Profile modal save (`PUT /qualityprofile/{id}`) | 2.7–2.8 s | 3.1–3.2 s (unchanged behaviour, one pending-release re-map) |
| Quality page save (`PUT /qualitydefinition`, six profiles) | **16.6 s** | **3.0 s** |

The only console error recorded during the quality checks was Chromium's
"failed to load resource (400)" for the deliberately refused delete; the
container log carried the matching validation warning and the three usual
start-up warnings (data-protection key storage, Kestrel port override, allowed
hosts) and nothing else.

## Container fixture reports

All fixtures ran inside the WSL engine with disposable configurations,
generated media and local catalogue doubles (the owner's configuration was used
only by the disposable-copy checks above). Every report passed and removed its
own containers, networks and temporary data.

Complete run on image `7130515f…` (baseline, Chromium, provider/feature and
download gates from the Docker workflow):

| Report | Checks |
| --- | ---: |
| `smoke.json` | 12 |
| `functional-security.json` | 19 |
| `first-run.json` | 4 |
| `first-run-browser.json` | 4 |
| `backup-tasks-browser.json` (with `backup-ui.json`) | 11 (+4) |
| `functional-security-browser.json` (with `ui-security.json`) | 22 (+12) |
| `jellyfin-browser.json` (with `ui-jellyfin.json`) | 4 (+4) |
| `collections-browser.json` (`--gui-audit`) | 38 |
| `anime-groups.json` | 21 |
| `nyaa-fixture.json` | 9 |
| `series-packs.json` | 13 |
| `anilist-reliability.json` | 30 |
| `group-import-collisions.json` | 12 |
| `import-snapshots.json` | 20 |
| `myanimelist-reliability.json` | 15 |
| `list-set-operations.json` | 25 |
| `simkl-reliability.json` | 23 |
| `quality-codecs.json` | 22 |
| `custom-qualities.json` | 28 |
| `artwork-reliability.json` | 14 |
| `metadata-sources.json` | 19 |
| `movie-catalogues.json` | 14 |
| `backup-tasks.json` (API only) | 10 |
| `jellyfin-refresh.json` (genuine Jellyfin 12.2) | 12 |
| `download-client-folders.json` (genuine qBittorrent) | 19 |
| `download-packs.json` (genuine downloads, Jellyfin and movies) | 25 |
| `library-upgrade.json` (populated library upgraded from the published 0.1.0 image) | 13 |

Targeted re-run on the final image `5862c561…`: `smoke.json` 12,
`functional-security.json` 19, `first-run.json` 4, `quality-codecs.json` 22,
`custom-qualities.json` 28 and `functional-security-browser.json` 22 (with
`ui-security.json` 12), plus the disposable-copy defect audit and the 22
quality-screen checks recorded above.

PostgreSQL: `postgres-collections.json` ran from the Linux clone at the
committed source `676bc897d` against a genuine PostgreSQL server container and
passed its **28 tests** with complete cleanup.

arm64: the image was cross-built from the same working tree
(`sha256:89f7899b63d4958d8d83a6a4f7268b1f8e66f8da45a635eae7320e4155b8794e`,
`linux/arm64`, label version `0.1.2`), its runtime was confirmed to be an
`aarch64` ELF under QEMU emulation, and it passed `arm64-smoke.json` (12
checks) and `arm64-functional-security.json` (19 checks).

## Hosted runs

The two commits were pushed together, and because the documentation commit
carries `[skip ci]` GitHub created no run for the push. Both workflows were
therefore dispatched on `main` at the pushed source:

- [CI 39081401461](https://github.com/KvasFan/Tanukarr/actions/runs/39081401461)
  succeeded (frontend lint and build 54 s; backend build and suites 5 m 56 s).
- [Docker and Release 39081402989](https://github.com/KvasFan/Tanukarr/actions/runs/39081402989):
  attempt 1 built the image, passed its in-image checks and twenty fixture
  gates (smoke, security, first-run, groups, Nyaa, packs, AniList, collisions,
  snapshots, MyAnimeList, list operations, Simkl, quality codecs, custom
  qualities, artwork, qBittorrent, both Jellyfin fixtures, the packaged
  collections/backup/first-run/metadata-source browser gates) and then failed
  in the native film catalogue fixture at its first configuration write with
  the application error `PUT /api/v3/config/metadatasource/1: attempt to write
  a readonly database`. The rerun of the failed job (attempt 2) passed nine
  gates and failed in the Simkl fixture, after its container restart and
  nineteen checks, with `POST /api/v5/importlist/action/getOAuthToken: attempt
  to write a readonly database`. Neither fixture opens SQLite itself, so the
  error is the application's; the same fixtures passed locally on the same
  source (14 and 23 checks), the 0.1.1 `edge` run had failed once the same way
  in the Simkl fixture and passed on retry, and the published 0.1.2 run had
  passed all gates. The cause was not established here: the artifact held no
  application log for either fixture (the film catalogue fixture wrote none
  and the upload step listed only four fixture logs), which is the missing
  evidence. A follow-up commit makes that fixture save its redacted container
  log like the others and uploads every `tanukarr-*.log`; the fixture was
  re-run locally against the fixed image (passed, 9,085 log lines saved). The
  third attempt passed every gate in 53 m 4 s and published the verified
  image to the registry as `edge` and `sha-faf4003fb…`
  (`sha256:4bdc6702e3a9c8d23cf5b704325db4aafcf9eb75db10b3409c59abc39226218a`);
  the run was not a tag, so no release was created.

## Scope

Compiles, tests and the listed fixtures were run; the disposable copy of the
owner's library was exercised through a real Chromium browser and its API and
then discarded; the deployed instance was not modified and no release was
published (the hosted `main` workflow pushed only its usual `edge` image after
its gates passed). Screenshots 1–4 show screens
that worked functionally on the copy; what changed for them is the measured
save latency and the handling of edits made during a save. Not re-run here:
the Windows Selenium automation suite and the WSL compression-worker
integration cases, which the previous ledgers cover and whose code did not
change.
