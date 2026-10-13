# Documentation and contributor review - October 12, 2026

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](README.md) and [Tanukarr 1.0.0 verification](releases/1.0.0-verification.md). Original SonarrWeeb names, versions, image references and test results below describe the recorded build and remain unchanged.

This review checks the repository files and brings maintained documentation into
English. It records existing implementation and verification evidence; it does
not repeat the runtime/security audit or rebuild the deployed image.

## Scope and inventory

The starting snapshot is `ca91432162730c25bf5d524df70204006ce7281d`. All **4,747
tracked files**, totaling 22,519,658 bytes in the working tree at the inventory
checkpoint, were read for existence, readability, inventory and conflict-marker
checks. There were no missing or unreadable files, no unresolved merge-marker
candidates and no invalid UTF-8 Markdown files. This is a complete tracked-file
integrity inventory, not a manual line-by-line code review of every function.

Runtime `config/`, `media/`, ignored build output and private `.env` data are not
part of the Git inventory. Localization resources, release-name fixtures, binary
assets and third-party legal notices retain their original content. English
documentation does not mean translating the application's supported UI languages
or rewriting the GPL text.

The review updates the existing guides and five dated integration/validation
reports, adds the [documentation index](README.md) and
[contributor register](../CONTRIBUTORS.md), and preserves this review as a separate
checkpoint. The license is already English and remains byte-for-byte unchanged.

## Documentation corrections

- CONTRIBUTING now uses .NET SDK 10.0.401, Node 24, Yarn Classic and Vite commands
  matching global.json, package.json, the solution and CI. Obsolete webpack build
  instructions were removed; test categories, platform requirements, ChromeDriver,
  compression and disposable Docker checks are explained.
- README, FORK and the index identify the included upstream history through
  `59c22e85993bb1120bfcf9fd1d6f509a510203df`. The original `698bb1dbb` integration
  checkpoint remains identified as historical. Git ancestry verified that the
  three later upstream commits were already included; no rebase or fetch was
  performed during this documentation review.
- Publishing workflow tags, local-only images and executed amd64 verification
  are distinguished from configured arm64 builds. Docker examples consistently
  publish to loopback, and local builds are rebuilt rather than treated as a
  registry pull. The WSL launcher requirements are explicit.
- AniList account-list statuses, anime release statuses and custom-list membership
  are documented as separate, combined conditions. Native source/foreign-ID
  ownership, preview/exclusion/cleanup and provider attribution are preserved.
- Companion endpoint and add-payload boundaries match both API versions.
  Compression dependencies, input restrictions, filesystem safeguards and
  unavailable hardware coverage match the worker and tests.
- Security reporting addresses the maintainer of the actual fork build, without
  promising upstream response deadlines or assuming private reporting is enabled.
- Dated reports retain their source hashes, executed test counts, image identities,
  export checksums, skipped cases and residual findings. Earlier Docker-unavailable
  statements and private October 6 receipts are explicitly historical, not
  contradictory current verification claims.

## Attribution method

The Git snapshot has 12,039 reachable commits, 393 exact author names, 380 exact
Co-Authored-By names and a union of 748 recorded identities. There is no tracked
mailmap; aliases, spelling/case variations, bots and translation accounts remain
separate. The register is not a count of unique people. No contributor email
addresses are reproduced.

Sixteen feature groups are mapped to original source commits and local adaptations.
Private Contributor, KakarottoCake, Matt Flowers and Ivo Katunaric are credited
alongside inherited Codex and all upstream Git identities. Issue/PR references
provide design context and do not establish implementation authorship. Reviewed
but excluded magnet-retrieval code is not presented as a delivered feature.

Current local work identifies **OpenAI Codex based on GPT-6**, with no more precise
model variant established. The original README's Claude Opus 5 declaration is
preserved alongside source trailers naming Claude Opus 4.8, generic Claude and
ikatun's Claude Opus 5.5. Inherited Codex commits have no exact model record.
These source declarations preserve attribution without independently verifying
historical runtime model executions.

## Verification and retained evidence

Documentation review checks include UTF-8 readability, English prose review,
relative link/anchor targets, Markdown parsing, documented command/file targets,
source ancestry, contributor names against Git evidence, unchanged license text
and `git diff --check`. No application code, package manifests, media, configuration
or deployment was changed. The executed application checks remain in the
[security and functional report](security-functional-validation-2026-10-12.md).

All 19 Markdown documents parse with CommonMark/table support; 118 local link
targets/anchors resolve, fences are balanced and the English-prose review found
no remaining Italian passages. All 748 generated contributor names exactly match
the audited Git identity list. Repository YAML configuration also parses.

Machine-readable inventory and attribution evidence are stored outside the source
tree in `%LOCALAPPDATA%/SonarrWeebTools/docs-audit-2026-10-12`, with sanitized copies
under `_artifacts/docs-audit-2026-10-12`. Those ignored host artifacts are not
promised to exist in a normal clone. This review and the contributor register are
the portable record included in Git.
