# Contributors and feature provenance

This SonarrWeeb register credits the upstream application, original fork implementations,
source co-authors and local adaptations. A maintainer is not assumed to have
authored every feature in their repository. AI credit records tool/model provenance
without assigning legal authorship or replacing the original human notices.

The Git identity snapshot is `ca91432162730c25bf5d524df70204006ce7281d`
(October 12, 2026), before the original documentation-review checkpoint. It contains 12,039
reachable commits, 393 author display names, 380 co-author display names and
748 distinct names in their union. These are recorded identities, including
aliases, bots and translation accounts, not a count of unique people. No
tracked `.mailmap` exists, so spelling/case aliases are preserved. Emails are
omitted. Source-only contributors are credited below even when a consolidated
local commit makes their original author name unreachable from HEAD.

## Project and source credits

- **Sonarr contributors**, including the original NzbDrone work of Mark
  McDowall, Keivan Beigi and Taloth Saldono: the core application and upstream
  maintenance. The integrated history contains `develop@57816c149`, including
  the earlier `v5-develop@59c22e859` checkpoint. Mark McDowall authored the five
  final upstream commits: `bf00a7710`, `fc6736733`, `3c8b8573d`, `6d607b09a` and
  `57816c149`. The fork retains its own AI contribution policy and release CI.
- **Private Contributor**: exact name retained in Kakarotto source history
  for the original provider/ordering, matching, packs, lists and filter work.
- **KakarottoCake**: fork maintenance and search, torrent reuse, requested
  seasons, mobile/path controls, compression and library-tools implementations.
- **Matt Flowers**, author in the **gaboy498-byte** repository: the adopted
  season-range, scene-mapping and torrent-filter enhancements.
- **Codex**, recorded author of 23 source-only **ikatun** commits: complete
  searches, pack decisions, queue/numbering work and build changes. The
  source commits do not record an exact model.
- **Ivo Katunaric (ikatun)**: targeted pack queries, Docker checks, UMASK,
  pack-first bulk searches and default monitoring of specials; his five
  source commits declare Claude Opus 5.5 as co-author.
- **Local OpenAI Codex assistant, based on GPT-6**: this integration,
  AniList lifecycle fixes, compatibility adaptations, test/runtime work,
  security hardening, the English documentation/contributor audit and
  SonarrWeeb 0.0.1 release packaging/installation documentation and the
  subsequent dedicated Jellyfin refresh integration and AniList multiple-list
  inclusion/exclusion controls.
- **Translators, reviewers and automation**: retained in the full Git
  register below and in each source commit; issue numbers alone are not
  evidence of implementation authorship.

## AI tool and model records

| Work | Recorded model/tool | Evidence and limitation |
| --- | --- | --- |
| Local integration, documentation, SonarrWeeb 0.0.1 packaging, subsequent Jellyfin refresh and AniList inclusion/exclusion controls | OpenAI Codex, based on GPT-6 | Explicit identity of the coding session; no more specific variant is established |
| Original Kakarotto README | Claude Opus 5 | Original README declaration, preserved as attribution |
| Preserved Kakarotto source commits | Claude Opus 4.8 (22 trailers), Claude (4 trailers) | Exact Co-Authored-By names; the generic label has no exact model |
| Ikatun final five feature commits | Claude Opus 5.5 | Exact source Co-Authored-By name alongside Ivo Katunaric |
| Ikatun earlier 23 commits | Codex; model not recorded | Git author identifies a tool/display name, not a runtime model |

Historical labels differ between README prose and commit trailers. Both are
retained rather than silently replaced. Source-declared labels do not
independently verify that a particular runtime model executed those sessions.

## SonarrWeeb release identity

The integrated project is distributed as **SonarrWeeb 0.0.1**, with Git tag
`v0.0.1`, under the GitHub profile
[KvasExert](https://github.com/KvasExert/SonarrWeeb). The publishing account
maintains this release; it is not credited as the original author of inherited
features. Naming, version metadata, installation YAML, workflow and release
packaging are local adaptation work; original feature authors and their source
commits remain credited below. Release publication does not transfer credit
for inherited features to the publishing account. The
[release notes](docs/releases/0.0.1.md) and
[installation guide](docs/docker-installation.md) link the public source and
distribution, while the Git log records release changes and preserved trailers.

## Feature-to-contributor register

Original source links identify provenance, not a claim that commits were
cherry-picked unchanged. Local adaptations are recorded by their own commit IDs.
The full upstream Git history credits changes beyond the representative
upstream commits below. Feature and test details are in the
[documentation index](docs/README.md).

### Upstream Sonarr application, metadata baseline, v5 UI, Vite, notifications and integrations

Sonarr contributors.

- [698bb1dbb](https://github.com/Sonarr/Sonarr/commit/698bb1dbb7cafc6ff3a5d1d15f3535a1d16da636): coopa11y. Make Calendar episode groups keyboard accessible.
- [6c9d41960](https://github.com/Sonarr/Sonarr/commit/6c9d41960fb8f14c3f26944539702424140efc8e): Weblate; declared co-authors: Havok Dan, Ion Bazan, atomic, dmmc28, fordas. Multiple Translations updated by Weblate.
- [72abb2885](https://github.com/Sonarr/Sonarr/commit/72abb2885a465d4b8481bd08b5096fbfa5d53bf3): Bogdan. Disable imported select if no eligible records found.
- [59c22e859](https://github.com/Sonarr/Sonarr/commit/59c22e85993bb1120bfcf9fd1d6f509a510203df): Bogdan. Use default page size recommended by the indexer.

### Jellyfin selected-library refresh and conditional periodic updates

The existing Sonarr MediaBrowser integration supplied the notification and
settings conventions. Its representative original commits are:

- [daafb67fa](https://github.com/Sonarr/Sonarr/commit/daafb67fa7e3cc3e320037fc51a61485849b38ba): **Mark McDowall**, original MediaBrowser notifications.
- [ecebe73c3](https://github.com/Sonarr/Sonarr/commit/ecebe73c333582a4651b66a2fa66369d8ffbc337): **hatharry**, Emby library-update correction.
- [ad0dc01cf](https://github.com/Sonarr/Sonarr/commit/ad0dc01cf7ed16ccfa8260717111ad8a44675221): **Jendrik Weise**, matching series-path updates in Jellyfin/Emby.

**Jellyfin contributors** maintain the official library-enumeration and
selected-folder refresh APIs consulted for this work. No third-party Sonarr
plugin or community implementation was copied. **OpenAI Codex, based on GPT-6**,
implemented the dedicated connection, persistent queue, conditional scheduler,
tests and English documentation locally. This work follows the immutable 0.0.1
release. See the [feature guide](docs/jellyfin.md) and
[verification report](docs/jellyfin-verification-2026-10-12.md).

### Provider abstraction, TMDB and episode groups, AniList/Jikan providers and native non-TVDB identities

Private Contributor (source history); KakarottoCake (fork maintainer).

- [000a26dc9](https://github.com/KakarottoCake/Sonarr-Pro/commit/000a26dc91d050fba058edabc69f7f8c21993d07): Private Contributor; declared co-authors: Claude Opus 4.8. Introduce metadata provider abstraction (Phase 1 keystone).
- [1fd8b4fd3](https://github.com/KakarottoCake/Sonarr-Pro/commit/1fd8b4fd3991f513e448f88a2edc2d16100dad60): Private Contributor; declared co-authors: Claude Opus 4.8. Add TMDB metadata provider with episode group ordering.
- [cb223dc7a](https://github.com/KakarottoCake/Sonarr-Pro/commit/cb223dc7a55ce94b1ce12cf49ff61e9eac9bd11f): Private Contributor; declared co-authors: Claude Opus 4.8. Add AniList and Jikan anime metadata providers.
- [d0ca06d26](https://github.com/KakarottoCake/Sonarr-Pro/commit/d0ca06d2623d01c9aa6cb3bc8572c2b12dd1d30c): Private Contributor; declared co-authors: Claude Opus 4.8. Add series from any metadata provider.
- [ee0c88cb6](https://github.com/KakarottoCake/Sonarr-Pro/commit/ee0c88cb6e3fa5e9976721f72efe5c6c074be172): Private Contributor; declared co-authors: Claude Opus 4.8. Let an episode ordering be chosen when adding a series.
- [bffab21fc](https://github.com/KakarottoCake/Sonarr-Pro/commit/bffab21fcc7f22973728fce9b603d69aef02f137): Private Contributor; declared co-authors: Claude Opus 4.8. Fix TMDB and Jikan resources deserializing to empty.
- [1d429bb20](https://github.com/KakarottoCake/Sonarr-Pro/commit/1d429bb2005e97068bcd079896e16c608a37165d): Private Contributor; declared co-authors: Claude Opus 4.8. Allow series that have no entry on TheTVDB.
- [14989aa5b](https://github.com/KakarottoCake/Sonarr-Pro/commit/14989aa5b54688cbb5781837ce018577c4101841): Private Contributor; declared co-authors: Claude Opus 4.8. Let a metadata source be chosen when searching for a series to add.

Local adaptation: `390437b59`.

### AniList custom lists, lookup ranking, season-pack trimming, English-title preference and metadata settings

Private Contributor (source history); KakarottoCake (fork maintainer).

- [3ed8fd17b](https://github.com/KakarottoCake/Sonarr-Pro/commit/3ed8fd17baaa509f538eea1201932c3bf8cf950c): Private Contributor; declared co-authors: Claude Opus 4.8. Add AniList custom lists, better search ranking, and pack trimming.
- [c603efe2c](https://github.com/KakarottoCake/Sonarr-Pro/commit/c603efe2c21f01ec2eec0dda09bcfd05a996feea): Private Contributor; declared co-authors: Claude. Prefer English titles over romaji for AniList series.
- [48997728e](https://github.com/KakarottoCake/Sonarr-Pro/commit/48997728e2e3dcd7481b5d33442c7a3d1cd8803d): Private Contributor; declared co-authors: Claude. Always set Ratings on search results from AniList and MyAnimeList.
- [367d227f0](https://github.com/KakarottoCake/Sonarr-Pro/commit/367d227f062536217a3a85dbf6744a8c7edd769e): Private Contributor; declared co-authors: Claude. Expose the fork settings on the v5 API the UI actually reads.

Local adaptation: `390437b59`, `c4bd6a625`.

### AniList multiple-list inclusion and exclusion precedence

The new controls extend the existing provider and custom-list feature:

- [465a58448](https://github.com/Sonarr/Sonarr/commit/465a58448644907978927e3fa564e96baab81f2f): **Xabis**, original AniList import-list provider.
- [3ed8fd17b](https://github.com/KakarottoCake/Sonarr-Pro/commit/3ed8fd17baaa509f538eea1201932c3bf8cf950c): **Private Contributor**, original named custom-list filter; source-declared co-author **Claude Opus 4.8**, with **KakarottoCake** maintaining the source fork.

**AniList contributors** maintain the official watching-state and custom-list
membership API documentation consulted for this adaptation. **OpenAI Codex,
based on GPT-6**, added multiple included/excluded custom lists, explicit
watching-state exclusions, exclusion precedence across paginated results,
literal custom-name inputs, tests and English documentation on October 13,
2026. This work follows the immutable 0.0.1 release; the inherited author and
model declarations remain unchanged. See the
[configuration guide](docs/library-tools.md#anilist-lists) and
[verification checkpoint](docs/anilist-filters-verification-2026-10-13.md).

### IMDb orderings, bounded dataset import and Jikan fallback

Private Contributor (source history); KakarottoCake (fork maintainer).

- [569121d6a](https://github.com/KakarottoCake/Sonarr-Pro/commit/569121d6afae44704e3a1d1da17ff279d68dff34): Private Contributor; declared co-authors: Claude Opus 4.8. Offer IMDb episode ordering from the published datasets.
- [f67323221](https://github.com/KakarottoCake/Sonarr-Pro/commit/f67323221e63a4d3dd2f190d50872d505bbb4f68): Private Contributor; declared co-authors: Claude Opus 4.8. Make the IMDb index self-maintaining and offer MyAnimeList as a source.
- [4683efb15](https://github.com/KakarottoCake/Sonarr-Pro/commit/4683efb158e07744e52481bc7ad8da20eeaeeba2): Private Contributor; declared co-authors: Claude Opus 4.8. Bound IMDb import memory, and rank search on every known title.
- [85570f111](https://github.com/KakarottoCake/Sonarr-Pro/commit/85570f111dd817bc5795189702bfea45ae27d100): Private Contributor; declared co-authors: Claude Opus 4.8. Stop provoking Jikan timeouts, and add a series without its episode list.

Local adaptation: `390437b59`.

### Multi-season packs, release title aliases and season suffix matching

Private Contributor (source history); KakarottoCake (fork maintainer).

- [3947d76d3](https://github.com/KakarottoCake/Sonarr-Pro/commit/3947d76d380fe0d84a4bbc03c6c8612d48997f1e): Private Contributor; declared co-authors: Claude Opus 4.8. Support multi-season packs instead of rejecting them.
- [87b32bb1f](https://github.com/KakarottoCake/Sonarr-Pro/commit/87b32bb1f54885c412f1664241dc8d1565597032): Private Contributor; declared co-authors: Claude Opus 4.8. Match a release named by any title its provider knows.
- [490ac3a1e](https://github.com/KakarottoCake/Sonarr-Pro/commit/490ac3a1e2615c4b5e8b6500072a48f5cd394da5): Private Contributor; declared co-authors: Claude Opus 4.8. Read a trailing number as the season when the release says it is one.

Local adaptation: `390437b59`.

### Initial fake-release torrent inspection and cloud-service opt-out

Private Contributor (source history); KakarottoCake (fork maintainer).

- [5461d6b90](https://github.com/KakarottoCake/Sonarr-Pro/commit/5461d6b90f0ed136fc1d92211ad20ac24db57d49): Private Contributor; declared co-authors: Claude Opus 4.8. Add torrent content inspection to reject fake releases before grabbing.
- [5d313b971](https://github.com/KakarottoCake/Sonarr-Pro/commit/5d313b9710809b78f14238c6bc4244e1c1510dd9): Private Contributor. Stop contacting Sonarr's servers for updates, crashes and notices.

Local adaptation: `390437b59`, `c4bd6a625`.

### Responsive interactive search, qBittorrent reuse tags, requested-season imports and retained multi-season source files

KakarottoCake.

- [93b63ae06](https://github.com/KakarottoCake/Sonarr-Pro/commit/93b63ae06603c8d80a3371185c7b8e44aecf1ef1): KakarottoCake. Overhaul interactive search with responsive release cards.
- [ab698428c](https://github.com/KakarottoCake/Sonarr-Pro/commit/ab698428c5a6db703a393866e3aac9e44b1b4611): KakarottoCake. Reuse existing qBittorrent downloads with isolated tracking tags.
- [21cdcdbbc](https://github.com/KakarottoCake/Sonarr-Pro/commit/21cdcdbbcbd5bb8a8d3478975351ce09c8b180ef): KakarottoCake. Import requested seasons from multi-season downloads.
- [4f9523ce7](https://github.com/KakarottoCake/Sonarr-Pro/commit/4f9523ce7edda49acc71e704b5f6e26e86b2d17b): KakarottoCake. Keep multi-season sources after import and allow completed previews.

Local adaptation: `6acc333ae`, `0aab003c2`, `ac031dc82`, `00176fb87`.

### Mobile navigation, Plex folder names, path display/picker, per-show compression and H.264 presets

KakarottoCake.

- [acc0999dd](https://github.com/KakarottoCake/Sonarr-Pro/commit/acc0999dd51885c26bff962b7db85f4cb03de5b7): KakarottoCake. Improve mobile navigation with bottom controls and menu sheets.
- [fc09179e4](https://github.com/KakarottoCake/Sonarr-Pro/commit/fc09179e42cb51c9451776b53a9f7feed2ed859d): KakarottoCake. Add easy series folder renaming with Plex year and ID naming.
- [0145299cb](https://github.com/KakarottoCake/Sonarr-Pro/commit/0145299cb21c902ffdd8fc4870bab69f4e6a8f0d): KakarottoCake. Show full series paths and remove header metadata attribution.
- [1e0bc1698](https://github.com/KakarottoCake/Sonarr-Pro/commit/1e0bc1698ece0cc1e9f2a49fa44ef83af4b9e6b1): KakarottoCake. Add path filtering and per-show compression with persistent progress.
- [f40e540a0](https://github.com/KakarottoCake/Sonarr-Pro/commit/f40e540a0461e65ac6db384e1d89ad371a6f0998): KakarottoCake. Replace path text filter with drive and folder picker.
- [5a756d49f](https://github.com/KakarottoCake/Sonarr-Pro/commit/5a756d49f4a630a37355f0fa77665c9185eaf548): KakarottoCake. Use H.264 for software compression presets.

Local adaptation: `2ae9ce27b`, `39fec9271`, `89aefacc4`, `a5522e0e9`, `a1457f9b0`, `a502809e1`.

### Approved library tools, score provenance, manual overrides, retained versions, recycle-bin restore, companion keys, list preview and additional list providers

KakarottoCake.

- [c104bf96f](https://github.com/KakarottoCake/Sonarr-Pro/commit/c104bf96ffd2ebaa18999db29196160e4a7210b0): KakarottoCake. Add approved library, search, import and mobile upgrades.

Local adaptation: `f8b5d9de0`.

### Explicit multi-season ranges, safe automatic import, legacy grab reconciliation and import progress

Matt Flowers (gaboy498-byte source repository).

- [9cd27f4b9](https://github.com/gaboy498-byte/Sonarr-Pro/commit/9cd27f4b9285d466dab933f618e4e9cab46268f3): Matt Flowers. fix: expand explicit multi-season ranges.
- [29c1872dc](https://github.com/gaboy498-byte/Sonarr-Pro/commit/29c1872dc98245cf8d63a7c9a11b06c48ab13389): Matt Flowers. fix: enable safe multi-season auto-import.
- [9dc787449](https://github.com/gaboy498-byte/Sonarr-Pro/commit/9dc7874490939d6cb7a21dfbb31053057c9171a5): Matt Flowers. fix: reconcile legacy multi-season grab membership.
- [e37d5a2fe](https://github.com/gaboy498-byte/Sonarr-Pro/commit/e37d5a2fea6f64f94f0e5b99fffc20af10ab7587): Matt Flowers. feat: add per-episode import progress.
- [0d48e2eef](https://github.com/gaboy498-byte/Sonarr-Pro/commit/0d48e2eefc085d0b36f9653472d1077227eb6205): Matt Flowers. test: cover multi-season size semantics.

Local adaptation: `c4bd6a625`.

### Fake-release submission routing and executable-title checks

Matt Flowers (gaboy498-byte source repository).

- [79ef6d2b1](https://github.com/gaboy498-byte/Sonarr-Pro/commit/79ef6d2b17fe9dc985527f3062b0fa05363535a7): Matt Flowers. fix: harden fake-release submission routes.

Local adaptation: `c4bd6a625`.

### Interactive/automatic complete-series search, pack priority, queue batching, no overlapping grabs, fresh episode state, numbering and grab-history verification

Codex (inherited ikatun Git author; exact model not encoded).

- [743863cd4](https://github.com/ikatun/Sonarr-Pro/commit/743863cd40d55d66f70161743b8593f4a19d2df4): Codex. Add interactive complete-series torrent search to series toolbar.
- [aebe5ffea](https://github.com/ikatun/Sonarr-Pro/commit/aebe5ffea543f3a66034c97511c25875850c18f0): Codex. Fix multi-season range tracking and automatic per-file imports.
- [91612f21f](https://github.com/ikatun/Sonarr-Pro/commit/91612f21fa68185b32b0a9486ca6f476a7c514b4): Codex. Preserve validated series identity in complete-series searches.
- [904df6a6a](https://github.com/ikatun/Sonarr-Pro/commit/904df6a6a3f382d8cce48e91ddbe44e5176ef935): Codex. Retain release identity through normal complete-series matching.
- [7d97ad267](https://github.com/ikatun/Sonarr-Pro/commit/7d97ad267bb88ad32ceb917b73422ec880ad651c): Codex. Add scoped automatic complete-series search with dry-run reporting.
- [1dabf5ec9](https://github.com/ikatun/Sonarr-Pro/commit/1dabf5ec967255e34fc53d20149ec2427c3a59f3): Codex. Count multi-season queue progress by actual episode season.
- [fbe07a25d](https://github.com/ikatun/Sonarr-Pro/commit/fbe07a25d3b9e0a8d7fda27bdacbad7eb422c4f5): Codex. Prefer multi-season packs before seasons and individual episodes.
- [eb8234aa2](https://github.com/ikatun/Sonarr-Pro/commit/eb8234aa25661b8ebcaaa9e0b16855ddf6da894f): Codex. Avoid automatic overlapping grabs during multi-season downloads.
- [d7cb2a7c8](https://github.com/ikatun/Sonarr-Pro/commit/d7cb2a7c809d90cdd0f835917edafa7db1e13d39): Codex. Grab anime season packs before searching uncovered episodes.
- [07dd5364c](https://github.com/ikatun/Sonarr-Pro/commit/07dd5364ce3d3d57f1e6ed6758871d5567f88b97): Codex. Skip complete seasons during monitored series searches.
- [b9412c01f](https://github.com/ikatun/Sonarr-Pro/commit/b9412c01f3d938400bfe76c17cee98fd2c61c1c1): Codex. Batch queue episode lookups to avoid oversized request URLs.
- [f7d61012c](https://github.com/ikatun/Sonarr-Pro/commit/f7d61012ca5efb3b827df44d6775cd0904674b30): Codex. Search complete series first when multiple monitored seasons are incomplete.
- [d5ed950e5](https://github.com/ikatun/Sonarr-Pro/commit/d5ed950e51400b296366ed6020f304a0d56a29dd): Codex. Prevent automatic feed upgrades of complete seasons.
- [241936c82](https://github.com/ikatun/Sonarr-Pro/commit/241936c82cc7f8b43ada7163f4bffd163ab7eaa8): Codex. Complete downloads when preferred existing episodes are retained.
- [f653d1c87](https://github.com/ikatun/Sonarr-Pro/commit/f653d1c8729014b82fef0dfb51c619e3f707c6bc): Codex. Verify competing scene and library episode numbering before acquisition and import.
- [20cfbf14a](https://github.com/ikatun/Sonarr-Pro/commit/20cfbf14a5c5bfb962da95dcb3d49bdabfa6398b): Codex. Fix anime multi-season coverage and original grab tracking.
- [67f9cd3f3](https://github.com/ikatun/Sonarr-Pro/commit/67f9cd3f31c09c139218fff0cabadd77fe11e85d): Codex. Handle parser preprocessing and validate anime import regressions.
- [61b4fde3f](https://github.com/ikatun/Sonarr-Pro/commit/61b4fde3f9f5689d2106efd535888244668c0dfa): Codex. Recover complete-series download tracking from validated grab history.

Local adaptation: `e12e717e2`.

### Immutable container tags, pull-request image builds and SDK/workflow reproducibility

Codex (inherited ikatun Git author; exact model not encoded).

- [6cce9e1b5](https://github.com/ikatun/Sonarr-Pro/commit/6cce9e1b59a4c7a7406142bc77ad712417c0a7fd): Codex. Configure maintained fork builds and immutable container tags.
- [3dd798386](https://github.com/ikatun/Sonarr-Pro/commit/3dd798386db898ec4dbb7f61553b71e80f43e43d): Codex. Pin container SDK to match global.json.
- [91d3c8b31](https://github.com/ikatun/Sonarr-Pro/commit/91d3c8b31c8d8f367a9db95d8c7c570fdbb90550): Codex. Document reproducible container toolchain.
- [8fd91541e](https://github.com/ikatun/Sonarr-Pro/commit/8fd91541e19049d62797b371fa61ba72e825c318): Codex. Document manual build triggers and workflow permissions.

Local adaptation: `e12e717e2`.

### Targeted complete-pack queries, Docker test stages and UMASK, pack-first bulk searches, unmonitored default specials

Ivo Katunaric (ikatun); source-declared co-author Claude Opus 5.5.

- [751715c9d](https://github.com/ikatun/Sonarr-Pro/commit/751715c9d3ffd77f2edf241ae2b32bfe142126ec): Ivo Katunaric; declared co-authors: Claude Opus 5.5. Add targeted pack queries to complete-series searches.
- [23efa34d0](https://github.com/ikatun/Sonarr-Pro/commit/23efa34d0181e4fbe64bdd02b86928dc9f2b621f): Ivo Katunaric; declared co-authors: Claude Opus 5.5. Run tests inside the Docker build and drop the separate CI workflow.
- [0464b0b6c](https://github.com/ikatun/Sonarr-Pro/commit/0464b0b6cb748f2d84e57590329d19d3e3d18dd1): Ivo Katunaric; declared co-authors: Claude Opus 5.5. Honour UMASK in the Docker entrypoint, defaulting to 002.
- [501315f2f](https://github.com/ikatun/Sonarr-Pro/commit/501315f2faf799f91ecfd7ccbe78c1cb9f6ff0a6): Ivo Katunaric; declared co-authors: Claude Opus 5.5. Prefer full-series packs and fresh episode state in bulk missing searches.
- [d2e06dc05](https://github.com/ikatun/Sonarr-Pro/commit/d2e06dc0552db509db43ab347e30b4e10736680d): Ivo Katunaric; declared co-authors: Claude Opus 5.5. Leave specials unmonitored for series added without a monitor choice.

Local adaptation: `e12e717e2`.

### Current upstream rebase, compatible source adaptation, AniList identity/list lifecycle fixes and regression tests

Local Codex assistant (OpenAI Codex based on GPT-6, explicit current environment identity).


Local adaptation: `390437b59`, `c4bd6a625`, `e12e717e2`.

### Local Docker/WSL deployment, vulnerability reproductions/fixes, provider/companion/container hardening and functional/security evidence

Local Codex assistant (OpenAI Codex based on GPT-6, explicit current environment identity).


Local adaptation: `130831c3b`, `3f428c449`, `86bea3541`, `ca9143216`.

## Reviewed source work not delivered

Matt Flowers also authored the magnet metadata retrieval/redirect inspection
commits `872d7cf9716f8f0d9b543cd645093d71f6281bae` and
`14eaaebf6267791c60c50a01b6d3109e18ccb53e` in gaboy. Their behavior was not
adopted because the required security-patched MonoTorrent package was
unavailable. They are reviewed source contributions, not delivered features.
Ikatun checkpoint `029e6d42e` changes no files.

## Complete recorded Git identity register

All 748 exact author/co-author names reachable from the snapshot above are
listed alphabetically. Aliases and automated identities remain separate;
the source-only names already credited above supplement this list.

- 1
- 181aff
- 4kwins
- 6cUbi57z
- aaraujo666
- abcasada
- ablax
- addisonbabcock
- Adrian
- Adrian Cowan
- aghus
- Agneev Mukherjee
- aguillement
- Aitzol Garmendia
- Ajax
- Akashi2020
- Alan Collins
- Alan Yee
- Alanoll
- Albert
- Albert Lee
- AlbertCoolGuy
- Albrt9527
- Aleksandr
- Alessandro Sclafani
- Alex
- Alex Cortelyou
- Alex Herbig
- Alex Thomson
- Alexander
- Alexander Balya
- Alexander WB
- AlexR-sf
- alextomas955
- Altair
- amdavie
- Andrejs Ķīlis
- Andreu Punsola Soler
- Andrew Champion
- Andrew Chappell
- Andrew Ukkonen
- Andrés Reyes Monge
- angelsky11
- anne
- Ano10
- anonymous951
- Anthony Borushko
- Antoine Martin
- Anton Kesy
- Appoxo
- Ardenet
- ArLab1
- ArnoKeesmand
- ARTbird309
- Arthur Bols
- Ashley Broughton
- atomic
- Audionut
- Auggie
- Augusto Poletti
- Aviad Levy
- bai0012
- Bakerboy448
- bakerboy448
- Bao Trinh
- Baptiste Mongin
- BarbUk
- Bastián Quezada
- bdegier
- BeardedWatermelon
- bejhan
- Ben Martin
- Benjamin Staneck
- Benjamin Uzelac
- Bert Vanhaeke
- bertrand
- beyondmeat
- bianying1
- bitPhex
- Björn Dahlgren
- Björn Dahlgren
- Blair Noctis
- blankhang
- Bo Jeanes
- Bogdan
- bogdan-rgb
- bowsefather
- bparkin1283
- bpoxy
- BrandenXia
- Brayden Hulett
- BrendenCA
- Brian Hartvigsen
- Brian Wentzloff
- brn
- brokje1988
- Bruno Garcia
- BubbleRep
- byakurau
- C.J. Manca
- Carl Downing
- Carlos Gustavo Sarmiento
- carrossos
- Casey Bodley
- Casselluu
- CaveMan474
- Chad A Simmons
- Chao Man
- Chaoshuai Lü
- Charlie
- CheAle14
- ChewyGlitter
- chiral-lab
- ChopperRob
- Chris
- Chris Allen
- Chris Dyson
- Chris Heath
- Christopher
- Christopher Heath
- chrizl
- Chromo-residuum-opec
- cicomalieran
- Clairton075
- Codex
- Colin Gagnaire
- Colin Hebert
- Colin Mackie
- Collin Heist
- Connor Gallopo
- coopa11y
- Corgo
- corwin007x
- crayon3shawn
- Crocmou
- ctrl2027
- cturra
- Cuki
- Cyberlane
- Cybertinus
- d0nizam
- Daghriry
- DahJorz
- damienmillet
- danast
- Dani Talens
- Daniel Martin Gonzalez
- Daniel Smith
- Daniel Suárez Bobes
- Daniele Prevedello
- darksol736
- datahodor
- David Molero
- David Newhall
- David13467
- DavidHenryThoreau
- DavidJares
- deadsynchronicity
- Deamon1333
- deepserket
- Deleted User
- delphiactual
- Denis Gheorghescu
- Dennis Langthjem
- dependabot\[bot\]
- desimaniac
- devbrian
- Devin Buhl
- diamondpete
- diaverso
- Dimitri
- DimitriDR
- Dino
- Dion Woolley
- DivMode
- Dlgeri123
- dmmc28
- DNArjen
- dnnr
- dny238
- Dominik Krivohlavek
- Dominika Matějková
- Donato Battista
- Douglas R Andreani
- Dream
- Drew Freyling
- Dron
- dronkailp
- Dror Levin
- Dtaggart
- ECB\\rotem.shoshan
- EDUYO
- Elfshot
- elgumi
- Elias Benbourenane
- Ellis Nieuwpoort
- emacsdias
- emyarod
- EpIcNuGeTs
- epmt7w3ugk
- Eric Yen
- Evan J
- Fabricio Silva
- farebyting
- FASTAWFUL
- faush01
- felizk
- Fenester
- ferencmarkizay
- fezster
- filiphavlin
- fireph
- Fish2
- Fixer
- FloatStream
- Florian
- Florian Savouré
- flutter
- Fonkio
- fordas
- Fossil
- foXaCe
- Francisco Burgos Ramos
- Francisco Cachado
- Frank Riley
- Frank Scholl
- fritz
- FunThomasSVK
- fzr600dave
- Gabriel Markowski
- Gabriel Patzleiner
- Gabriel Sjöberg
- Gallyam Biktashev
- Garkus98
- Gaulin, Brendan
- Gauthier
- Gauthier Poulain
- Gauvino
- Gavin Mogan
- genoher
- geogolem
- Georgi Panov
- Ghworg
- Gianmarco Novelli
- Gjermund Wiggen
- Gjur0
- GkhnGRBZ
- gl3nni3
- Godwhitelight
- gr0sz
- grapexy
- Guy Porat
- Gylesie
- gzfy
- GΛVĪN
- Hadrien Patte
- Hajiroxx
- HanaO00
- hansaudun
- Harry Kim
- Harry Pollard
- hatharry
- Havok Dan
- Hayden
- hcharbonnier
- He Zhu
- Helvio Pedreschi
- Herve Lauwerier
- HeyBanditoz
- hhjuhl
- Hicabi Erdem
- hugepants
- Hugoren Martinako
- Iain Nicol
- Icer Addis
- iceypotato
- ID-86
- Igal Tabachnik
- Ihor Mudryi
- Ilbebino
- ildoc
- ilike2burnthing
- iMohmmedSA
- Indrek Ardel
- infoaitek24
- installemployee
- Ion Bazan
- issueg2k4g34j2g
- Iulian Onofrei
- Ivan Brazza
- Ivan Mazzoli
- Ivan Trubach
- Ivar Stangeby
- jab416171
- jack-mil
- JackDandy
- Jacob
- Jacopo Luca Maria Latrofa
- Jagadam Dinesh Reddy
- jaja123456789
- Jake Pusateri
- Jake Soenneker
- James White
- Jamie Bartlett
- Jamie Magee
- Jared
- Jared Ledvina
- Jason54
- Jaspils
- JAV
- Javier Parada
- Jayden
- jbstark
- jcassette
- Jef LeCompte
- Jeff Byrnes
- jeff20001204
- Jeffrey Neer
- Jendrik Weise
- Jens
- Jeremy Ryan
- Jesse Chan
- Jessica Nguyen
- JeWe37
- jianl
- JJonttuu
- Jonas
- Jonatan Nyberg
- Jonza
- Jordy
- josef
- JoseFilipeFerreira
- Josh Archer
- Josh McKinney
- José Fonseca
- jsain
- jtpavlock
- julakali
- Julian Baquero
- Jure Merhar
- Jurrendel van Delden
- Jurriaan Den Toonder
- Justin Kenyon
- justin vanderhooft
- jutoft
- kaetzgang
- kaisernet
- KakarottoCake
- karaambaa
- kaso17
- Kathrin De Cecco
- kay.one
- Kayomani
- kayone
- KecskeTech
- Keivan
- Keivan Beigi
- Ken Murphy
- kephasdev
- Kerk en IT
- Kevin Lau
- Kevin Orel Edry
- Kevin Richter
- kevin8717
- keysuck
- KHng0284
- Khánh Phan
- Kisnov
- Klein Moretti
- kmcc049
- Koch Norbert
- Kshitij Burman
- KucharczykL
- kurlik
- Kushie
- Kuzmich
- Kuzmich55
- Kyse
- lalafei524
- Languages add-on
- Lars
- Lars Johnsen
- larsjohnsen
- leaty
- LeDaFeEs
- Leliene
- leotpp
- liangyi
- liimee
- linkin931
- linxchaos
- liralflsti-wq
- liuwqq
- Lizandra Candido da Silva
- LLeny
- Lloyd Sparkes
- lodu
- Loid Nokolaevich Mishkin
- Lordomus
- Lorenz
- Louis R
- LoV432
- lps-rocks
- Lucas
- Lucas Schad
- Lucius Aezerith
- Luigi
- luz paz
- luzpaz
- Lyxid
- m4quis
- MadaxDeLuXe
- MaddionMax
- Magnus
- Magnus5405
- Magyar
- Mailme Dashite
- MakubeXoX
- ManiMatter
- maodun96
- marapavelka
- Marc Carbonell
- Marcel
- Marcelo Castagna
- Marco Ciotola
- Marco Hillger
- Marcos
- margaale
- Mario Rodriguez
- Marius Nechifor
- Mariusz
- Mark Bebbington
- Mark Martines
- Mark McDowall
- Mark Mendoza
- markus101
- Martin
- Martin Hartvig
- Marty Zalega
- martylukyy
- marudosurdo
- matalvernaz
- Mateusz Lesiak
- Mathias
- Mathis
- Matt
- matt
- Matt Evans
- Matthew Kleiman
- Matthew Strapp
- Matthieu Sontag
- Matti Meikäläinen
- MattiaPell
- Maxence Winandy
- Menno Liefstingh
- Meyn
- Michaa85
- Michael Casey
- Michael Higgins
- Michael Peleshenko
- Michael Schönenberger
- Michael Tesch
- Michael5564445
- Michaela K.
- Michel Zehnder
- Michon van Dooren
- Mickaël O
- Mickaël Thomas
- Miha Frangez
- Mika
- Mikael Engström
- Mike
- Mike Lonergan
- Minamiya Natsuki
- Mirx
- misa1515
- Mitchell Cash
- Mizuyoru_TW
- mm519897405
- momo
- monopolo11
- MoowGlax
- morpheus133
- mr cmuc
- mrchonks
- mryx007
- mugantronix
- myrad2267
- mytelegrambot
- MythJuha
- mythjuha
- NanderTGA
- Nath
- Nathan
- Nathaniel Peiffer
- ndqnnwg
- Nesego
- netpok
- nexus671
- Nial McCallister
- Nicholas Landriault
- Nick Spreen
- Nicola
- Nicolò Castagnola
- Nilo Silva
- Nir Israel Hen
- nivong
- NoEomtionY
- nopoz
- Norbi
- Nota Inutilis
- nothingmn
- nuxen
- Nyokals
- obsidianwings
- ojmaster
- ole brum
- Ole Nørby
- Oleksandr Yurov
- Oskari Lavinto
- overkill32
- owine
- oxfordllama
- p8mt47sdfm-cmyk
- Patatra
- patrickmcbean
- Paul Kozlovitch
- Pawel Jelonek
- PearsonFlyer
- penggongkui
- PerOHaugstad
- Peter Czyz
- Peter Tutervai
- Petr Vojar
- Phillip R. Jaenke
- phrusher
- Picklessesssesseeeseeeses
- Pietro
- Pietro Ribeiro
- Pika
- pixxon
- Polgonite
- Portablelle
- Powdor
- Prayag Verma
- prhtk
- primetime43
- Psychotrickser
- Qiming Chen
- qq734231785
- Qstick
- quek76
- r0bertreh
- RabSsS01
- randomllama
- Ransack6086
- Rauniik
- RaZaSB
- rbraunschweig
- realzombee
- ReDFiRe
- reloxx
- Renan da Mota Ciciliato
- resi23
- rg9400
- Rhys Braunschweig
- Ricardo Loureiro
- RicardoVelaC
- ricecrackerfiend
- Richard
- Richard de Souza Leite
- rimasx
- Robert Dailey
- Robin Dadswell
- rodeohead
- Rodion
- Rodolphe Stoclin
- Roman Hlushchak
- Ronnie
- ROSERAT Ugo
- Roy Handelsman
- Ruben Lourenco
- Rubicj
- Rubén
- Ryan
- S0me6uy
- Sadi A. Nogueira
- Sam Holmes
- Sander Ploegsma
- Sander Spies
- sascha sampieri
- savin-msk
- scampower3
- scherzo
- Scott Rice
- Scott Robertson
- Scott Schlesier
- Sean Wilson
- Sergey M
- Serhii Matrunchyk
- Shiessis
- shimmyx
- SHUAI.W
- SHUCHENGWEN
- siankatabg
- Silvio Vescovo
- Simeon-byte
- Simone Caronni
- sirloinofbeef
- Siujeon
- SKAL
- Skyler Mäntysaari
- slammingdeath
- solidDoWant
- somniumV
- Sonarr
- soup
- sparky3387
- splifter
- sschlesier
- Stanislav
- Stas Panasiuk
- Steel City Phantom
- Stefan Jandl
- Stepan Goremykin
- Stephan Renggli
- Stephan Sundermann
- Steve Hansen
- Steven
- Stevie Robinson
- StevieTV
- Stickie
- Stijn van Hees
- Stjepan
- Storm
- stormaac
- studzik5521
- Stéphane (Bakeneko) Dupont
- Stéphane Dupont
- SunStorm
- Surfoo
- sutoramon
- swidou
- ta264
- Tacit
- Taloth
- Taloth Saldono
- Tamer Wahba
- Taylan Tatlı
- TectonicEd
- ted09080037
- tenshiak
- The Dark
- thegamingcat13
- TheUltimateCoder
- Thibault HERVÉ
- Thijs Tijsma
- Thijs Waalen
- Thirrian
- Thodoris Kalatzis
- Till Krüss
- Tim Gunter
- Timm Schäuble
- Timo
- Timothy Pillow
- toeiazarothis
- Tomer Horowitz
- Tommy Au
- topnew
- Touchstone64
- Trey Turner
- Treycos
- Tro95
- TrojanHorsePower
- tsubus
- ttran913
- TwentyNine78
- Twinkle264
- Tyler Durr
- TypNull
- ube
- ugyes
- Ulna
- Ultimate
- unknown
- Uruk
- usdasus
- useradmin-ctrl
- v3DJG6GL
- vawen
- vertigo235
- vfaergestad
- Viktor Elofsson
- vintage81
- VisoTC
- Vlad Zabotinsky
- volrod64
- wangtelong777
- warkurre86
- Watashi
- wavybox
- Weblate
- wgwqd
- Whoman
- wilfriedarma
- Winter
- Wolfy The Broccoly
- Wu Haotian
- Wyall
- Xabis
- Xanardi
- Xavier Xiong
- Xetera
- Xoores
- Xupix
- xuzhihui
- xveral
- yammes08
- YangForever88
- Yaroslav Veremenko
- Yashizzle
- Yassir
- Yi Cao
- Your Name
- YSLG
- Yukine
- Yuval Yaron
- Zach Nedwich
- Zack Eckersley Pallett
- Zak Saunders
- zakary
- Zalhera
- zetas
- zichichi
- Zippy79
- zodac
- ηg
- Андрей
- Петр Шургалин
- 君禹渊
- 宿命
- 康小广
- 极染
- 無情天
- 王锋
- 米大饭
- 闫锦彪
- 饶志华

## Updating the register

Collect author names with `git log --format=%aN <snapshot>` and parse
`Co-Authored-By` trailers with Git trailer semantics. Include adopted work
from preserved source refs when consolidation removed original author
metadata. Preserve exact names unless an explicit mailmap supplies aliases.
Record the snapshot, source commits, actual model evidence and uncertainty;
do not infer identity from a fork URL or a generic AI display name.

Upstream copyright and GPL text remain in [COPYRIGHT.md](COPYRIGHT.md) and
[LICENSE.md](LICENSE.md). Third-party notices remain with their components.
