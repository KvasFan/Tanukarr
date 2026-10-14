# Upstream review for Tanukarr 1.1.0

This checkpoint records the branch references fetched on October 14, 2026. Git
was used to fetch the source repositories and inspect their actual commits;
web pages with older cached branch listings were not used to decide currency.

| Repository and branch | Reviewed head | Change since the last publication audit |
| --- | --- | --- |
| Sonarr `develop` | `57816c149d02f95543973fefb4780c4495f8121c` | None |
| KakarottoCake `main` | `c104bf96ffd2ebaa18999db29196160e4a7210b0` | None |
| gaboy498-byte `main` | `f33ce343adf005ef3b29eef31710b597598d6c06` | None; the local tracking reference had lagged behind the already reviewed head |
| ikatun `main` | `d2e06dc0552db509db43ab347e30b4e10736680d` | None |

The gaboy reference advanced locally by eleven commits during this fetch. That
does not represent eleven new missing features: the [late 1.0.3 review](releases/1.0.3-publication.md#late-upstream-recheck-and-dependency-decision)
already examined this head. The multi-season range, import, membership,
per-episode progress, size regression and fake-submission changes were adapted
and credited in [the gaboy integration report](gaboy-integration.md). They are
retained, rather than applied a second time.

The two magnet-inspection commits remain the [recorded omissions](../CONTRIBUTORS.md#reviewed-source-work-not-delivered):
their required `3.0.2-sonarrsecurity.1.rev0002` dependency is still absent from
the [public NuGet version index](https://api.nuget.org/v3-flatcontainer/monotorrent/index.json),
checked again for this release. They are not advertised as implemented. The source's
manual-only Docker publication would replace this project's required main/tag
verification and publication gates, so that workflow is not adopted. Its
provenance documentation belongs to that source's own build and is linked as
evidence instead of being copied as Tanukarr verification.

The ImageSharp upgrade remains a separate dependency decision. The previous
isolated Release build of 4.1.3 failed because no applicable Six Labors license
key was configured. The current [Six Labors security and license announcement](https://sixlabors.com/posts/security-support-and-license-enforcement/)
also describes license enforcement for the 3.2 security backport. No license
check is bypassed. Tanukarr's thumbnail processing is restricted to JPEG, PNG
and WebP, skips metadata, limits frames, and now checks encoded size, dimensions
and input/output pixel budgets before full decoding. These safeguards do not
change the installed dependency version or establish that its reported CVEs
are patched; the release's fresh security report records the actual findings.

The new collection presentation, alternate-source artwork and custom quality
work in 1.1.0 are local contributions by OpenAI Codex based on GPT-6. Their
verification is recorded separately in the release ledger. Existing source
authors and inherited model declarations remain in [CONTRIBUTORS.md](../CONTRIBUTORS.md).

The fresh hosted restore also identified October 13 Microsoft Pkcs/SignalR
security advisories in the inherited dependency graph. The local release aligns
the SDK/runtime and Microsoft package patch versions to .NET 10.0.13/SDK
10.0.402 and explicitly overrides the vulnerable Pkcs transitive minimum.
The [release ledger](releases/1.1.0-verification.md#fresh-hosted-security-checkpoint)
links Microsoft's advisories and records the required fresh verification.
This is a dependency security update, rather than a new commit from the four
source repositories above.
