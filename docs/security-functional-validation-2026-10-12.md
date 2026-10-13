# Functional and security validation — October 12, 2026

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](README.md) and [Tanukarr 1.0.0 verification](releases/1.0.0-verification.md). Current product terminology is used in this archive. Numeric results, hashes, tags and original published assets retain their recorded identities; historical commands use placeholders for their original local names. This wording update does not repeat verification.

All 11 available .NET test suites were run on Windows, along with the Linux
platform suites, frontend checks, compression tests using generated videos,
API/browser checks, and code, dependency, secret and image scans. Reproducible
issues found during this work were corrected and verified. Skipped cases and
residual findings are documented separately; these results do not rule out
every possible vulnerability.

Verified code: `3f428c4492e0dc691cc8974af4fe8f15515ec37c`, followed only by
packaging permission hardening at
`86bea3541fd78b581b4e62ded194b323e9cfc072`, on
`integration/sonarr-latest-anilist`. Previous deployment base: `130831c3b`.
The report was recorded at `ca91432162730c25bf5d524df70204006ce7281d`.
These are historical results, not a claim that later documentation edits reran
the application tests.

OpenAI Codex (GPT-6) performed the local security review, corrections and
validation. Original feature credit remains with the upstream Sonarr
contributors, KakarottoCake, Matt Flowers / gaboy498-byte, Ivo Katunaric / ikatun,
and the recorded AI authors/coauthors of those source commits. See
[CONTRIBUTORS.md](../CONTRIBUTORS.md) for feature provenance and historical model
attribution, and the [documentation index](README.md) for the other reports.

## Confirmed issues corrected

| Confirmed issue | Correction and evidence |
| --- | --- |
| TMDB `api_key` visible in HTTP logs | Sanitizer corrected; four regressions and a compiled-DLL reproduction show that the secret is no longer present |
| DASH playlist disguised as `.mkv` opened URLs in the optional worker | Probe, encode and validation restricted to file protocols and supported demuxers; a local-server regression verifies zero requests |
| Companion `add-series` token could choose a path and start searches | Shared V3/V5 pre-add policy requires a registered root, generated folder and no search flags; 20 unit cases and 12 negative requests leave series/commands unchanged |
| DrvFs permissions left startup script and UI assets writable | Explicit COPY permissions and removal of group/other write bits from `/app`; tested as UID 1000 |
| ArchivePathGuard stalled at the filesystem root | Root preserved, containment corrected and traversal terminates; before/after watchdog and eight Linux cases, including real symlinks |
| AniList lookup displayed TVDB attribution/links for negative IDs | Provider-specific links/attribution and existing-series recognition using `(metadataSource, foreignId)`; five regressions and a real browser |

Files unchanged from upstream were compared before attributing issues to the
fork. Inherited tests that required the disabled Sonarr cloud, old frontend
selectors or outdated integration assumptions were also updated. Fixed ports
9010/9012 collided with local WebSocket++ services; the runner now chooses free
ports and verifies its own `appData` directory. The SignalR test ignores Sync
notifications without a resource while retaining assertions on persisted move
state.

## Tests executed

| Windows suite | Passed | Skipped in runner console summary |
| --- | ---: | ---: |
| Core, including integration categories | 6,175 | 20 |
| Common, including integration categories | 773 | 20 |
| API | 56 | 0 |
| HTTP | 57 | 0 |
| Host | 21 | 0 |
| Update | 17 | 0 |
| Libraries | 2 | 0 |
| Windows | 28 | 2 |
| Mono | 12 | 0 |
| Integration, temporary instances and databases | 92 | 12 |
| Automation, Chrome 154 | 6 | 0 |
| **Total** | **7,239** | **54** |

No final failures. TRX files contain additional cases excluded by platform beyond
the console skipped summary. Windows did not permit creation of the fixture's
two symlinks; both were executed and passed on Linux.

Linux Release/Posix backend results: Core 6,122, API 56, HTTP 57, full Common 753,
Host 17, Update 17, Libraries 2 and Mono 51 passed, totaling **7,075**. The
Windows suite is excluded by platform in the Linux container. The updated
ArchivePathGuard fixture passes 8/8; the focused cloud/sanitizer subset in the
Docker build passes 89/89. TRX files preserve original ignores, platform
requirements and tests requiring credentials or external infrastructure. Mono
needed an ApplicationData directory: the initial empty-SDK run is preserved,
and the rerun with temporary XDG configuration passes.

Frontend Node 24: 16/16 tests, ESLint, Stylelint, typecheck and Vite build passed.
Worker: 29/29 with FFmpeg 6.1.1, generated Matroska/MP4 videos, audio tracks,
subtitles, chapters and hardlinks. The Windows build has no warnings/errors;
the Linux build emitted MSB3026 copy-retry warnings between parallel projects
and completed successfully.

The isolated Docker instances verify required authentication, API keys, cookies,
lockout, invalid reset tokens, redirects, AllowedHosts, CORS, static paths,
invalid input, companion scope, TMDB masking, SQLite and restart. Public
AniList checks cover native addition, 26 episodes, refresh, duplicate rejection,
persisted identity and complete-search dry run against an empty Torznab fixture,
without acquisition. The real Chromium browser checks login, navigation,
AniList detail/lookup, attribution/links, search modal, and the distinction
between user-list status, anime release status and custom lists. There were no
unhandled JavaScript errors or requests to the disabled Sonarr cloud services.

The final image passed **19 runtime checks** and **five Chromium scenarios**.
These include the provider/list distinctions; private-account list
synchronization was not run.

## Scans and residual findings

Trivy 0.75.0 reports **15 unique CVEs in the image**, none critical: ImageSharp
has three high and two medium findings; Ubuntu has seven medium and three low.
No secrets were detected in the filtered source, verified Git snapshot or
image. Private configuration, media and `.env` files are excluded from scans
and reports.

- `postcss-selector-parser` was updated to 7.1.6, followed by a rerun of all
  frontend checks. [`braces` has a high advisory without a fixed version](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
  in the build toolchain. It is absent from the runtime image, which contains
  no Node installation.
- ImageSharp 3.1.12 remains a vulnerable component. The five advisories
  ([TIFF T4](https://github.com/advisories/GHSA-j9gm-c75j-xc9q),
  [TIFF T6](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w),
  [Float TIFF](https://github.com/advisories/GHSA-j3p4-wp97-rph4),
  [BigTIFF](https://github.com/advisories/GHSA-wmxv-xphr-5c9g),
  [ICC](https://github.com/advisories/GHSA-gwg2-r3hj-4w44)) involve paths excluded
  from Sonarr's resizer: only JPEG/PNG/WebP decoders, `SkipMetadata`, and no TIFF
  encoder, histogram equalization or ICC Entries access. Four proofs against
  the compiled DLL verify normal resizing, TIFF/BigTIFF rejection and malformed
  ICC removal with 370,400 bytes allocated. They do not patch the library.
  Version 4.1.2 requires a SixLabors key for a Release build, unavailable in this
  environment. Pre-existing NuGet suppressions were not used to declare the
  audit clean. NuGet auditing covered 28 projects; the exclusive dependency of
  the unbuilt Windows GUI project was checked separately.
- The ten Ubuntu CVEs had no fixed version in the distribution feed at scan
  time. The full inventory is retained in the Trivy/SBOM artifacts.
- Semgrep Community 1.180.0: 259 rules, 4,073 files, approximately 99.9% of lines
  parsed, and 87 reviewed findings. Fifty-three authorization findings do not
  recognize inherited or middleware checks; 32 concern SQL. Three old upstream
  migrations (029/030/033) interpolate text already in the database without
  escaping. They remain hardening opportunities for upgrades from very old
  databases; no current API exploit was demonstrated. Other SQL uses constants,
  typed values or parameters. Worker XML parsing reads local administrative
  configuration; socket mode 0660 is intentional. Six partial parses and one
  fixpoint timeout limit automated coverage. The separate Dockerfile scan after
  packaging hardening has zero matches but only 33.8% parsing; image assessment
  also uses the actual build, Trivy and runtime permission checks.
- Static Docker default-root/no-HEALTHCHECK findings remain in the inventory.
  The entrypoint initializes volumes and uses gosu; the application runs as UID
  1000 with no effective capabilities and with seccomp. The packaged ffprobe
  binary was built with networking disabled and only the file protocol.

## Artifacts and limits

JSON/TRX reports, logs, the CycloneDX SBOM, screenshots and proofs are stored in
`<local-tool-directory>/security-2026-10-12`. Sanitized final results are
copied to `_artifacts/security-2026-10-12`; these local artifacts are not part of
the committed source. Reusable tests and commands are documented in
[docker/tests/README.md](../docker/tests/README.md).

No real library, download client, private AniList/OAuth credentials or VAAPI
encoder was used. Native binaries were not comprehensively fuzzed, and secrets
were not scanned across all Git history. Fake-release filtering remains
heuristic: unreadable magnet/torrent metadata and archive contents have the
documented inspection limits. These scans are not a certification that the
software is free of vulnerabilities.

DNS-rebinding controls were tested on a temporary instance with explicit
AllowedHosts. The local deployment keeps its existing configuration, Forms
authentication and `AuthenticationRequired=Enabled`, and publishes only on
`127.0.0.1:6970`. Its AllowedHosts value was empty in the recorded deployment;
the explicit AllowedHosts test configuration was not applied to it.

## Final image and deployment

Image `<recorded-local-image-reference>`, Linux amd64:
`sha256:94555df18b98fd6f7b76dc0c88b2708538c9617f4f5d08e8a3cb93a20edceb48`.

Runtime `10.0.0.32728`; smoke verification, 19 runtime checks and five final
Chromium scenarios passed on that same image. Existing deployment configuration
and media were preserved.

Export: `<historical-image-export>` (original filename in the export receipt), 128,857,088 bytes.
SHA-256: `f5098bfff5761f3cc661679cfec05310c5efbcf2e3b6202f4f1706b2cb722c8f`.

Final deployment verification, image identity and export checksum are recorded
in `_artifacts/security-2026-10-12/deployment-final.json`. The local deployment
was verified at [http://localhost:6970](http://localhost:6970). Test configuration
and media did not replace the existing deployment data.
