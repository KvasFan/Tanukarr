# Quality sizes and video codecs

Tanukarr 0.1.0 retains the size editors introduced in 1.0.3, showing limits in **MiB per hour** in
**Settings > Quality**. Open a profile there for its quality choices, order,
groups, sizes and codec factors. Settings > Profiles edits profile policy and
custom formats. A **GiB per hour** preview makes
the same value easier to read. Minimum rejects undersized releases, maximum
rejects oversized releases, and preferred size contributes to release ranking.
Quality/source/resolution order and custom-format scores still apply separately.

## Edit global or profile limits

The global Quality Definitions page summarizes the values stored in all quality
profiles. Saving a changed global value applies that value to every profile
containing the quality. Use a profile's own size controls when profiles should
have different limits.

**Mixed** means profiles currently disagree. It is not zero or an unlimited
limit. Leaving a mixed field untouched preserves those differences. Editing and
saving that field replaces its value across the affected profiles, while other
untouched fields remain unchanged. **Unlimited** describes an unset preferred
or maximum value when the profiles agree. A legacy maximum of zero also renders
as Unlimited without rewriting that stored value. An unset minimum has no lower limit.

Save feedback reports a failed update and keeps the pending edits available for
correction or retry. Minimum, preferred and maximum must remain consistent in
each resulting profile. A global update that would make one of those profiles
invalid is rejected before any of its size changes are written.

For the same limit, `6,000 MiB/hour` is approximately `5.86 GiB/hour`. A
30-minute episode has half that hourly allowance, before codec normalization.
Actual acceptance also depends on the application's native runtime, episode
count and existing special-episode rules.

## Create and manage quality presets

In **Settings > Quality**, add a preset or duplicate an existing one. Set its
name, source, resolution, optional codec family and optional release-title
regular expression. For example, a **BdRip4k** preset can require Blu-ray,
2160p and HEVC. Enable the preset in the appropriate quality profiles and set
its order and hourly size limits there. A new preset is disabled until selected
in a profile; copying one preserves the corresponding profile settings.

Standard presets can also be renamed, edited and duplicated. Their original
numeric identities are retained. Custom presets receive persistent identities
starting at 10000, including after restarting the application. Matching is
applied to release decisions and imported-file evaluation; a manual quality
selection remains explicit. Title expressions have bounded length and execution
time and fail closed on invalid stored expressions or timeouts.

Before deleting a preset, disable it and change any upgrade cutoff that refers
to it in the profiles named by the validation message. Deletion retires its
identity so existing files and history remain readable. Retired presets are
excluded from new matching and new manual selections; a file's existing retired
quality remains available when editing that file. Restore a retired preset to
use it again. The Unknown fallback cannot be deleted.

## Codec-aware size comparisons

Different codecs can need different bitrates, so a single physical-size cutoff
can reject a compact modern encode while accepting a larger older encode.
Tanukarr uses this decision value:

```text
normalized size = actual release bytes × codec multiplier
```

It applies the multiplier once to quality-profile minimum/maximum checks and
preferred-size ranking. Download sizes, disk usage, imported-file sizes and an
indexer's physical maximum-size limit continue to use actual bytes. This does
not change the quality/source/resolution label, native runtimes, catalogue IDs
or episode numbering.

The multipliers are a **heuristic acquisition policy**. They are not a visual
quality score, a guaranteed bitrate ratio or a benchmark for a particular
encoder preset. Source quality, animation, grain, audio, subtitles, bit depth
and encode settings can all change the size needed by a real release. Review
the policy alongside your own quality profiles and preferred formats.

| Recognized video codec | Size multiplier |
| --- | ---: |
| MPEG-1 | 0.40 |
| MPEG-2 / H.262 / x262 | 0.50 |
| MPEG-4 ASP / XviD / DivX | 0.67 |
| H.263 | 0.40 |
| H.264 / AVC / x264 | 1.00 |
| H.265 / HEVC / x265 | 1.67 |
| AV1 | 2.22 |
| H.266 / VVC / x266 | 2.50 |
| VC-1 / WMV3 | 0.80 |
| WMV1 / WMV2 | 0.60 |
| VP6 / VP7 | 0.60 |
| VP8 | 1.00 |
| VP9 | 1.55 |
| Theora | 0.50 |
| Unknown, mixed or lossless | 1.00 |

The four MPEG-2/AVC/HEVC/AV1 factors are the project owner's requested baseline.
The other values are conservative project policy estimates. The live
read-only catalog is available at `/api/v3/qualitydefinition/codecs` and
`/api/v5/qualitydefinition/codecs`; the table displayed in Settings uses that
catalog. In 1.1.0 these are the defaults: each standard or custom **quality
profile** has editable codec factors, independent of its quality presets. The
profile editor accepts factors from 0.01 through 10.00 for all supported codec
families, including Unknown. Resetting the profile factors restores the defaults;
changing one profile leaves the others unchanged.

For example, a 30-minute, 1,000 MiB HEVC release compares as 1,670 MiB for these
decisions, or 3,340 normalized MiB/hour. Its actual download remains 1,000 MiB.
A 1,000 MiB AVC release compares as 1,000 MiB. A release is not automatically
accepted solely because its codec has a larger multiplier.

The application first uses recognized structured codec metadata. If that is
unavailable or unrecognized, it considers bounded codec tokens in the release
title. Unknown codecs, multiple recognized codecs and lossless/mezzanine
formats use the Unknown family: its default is the neutral 1.00 factor, and
an explicit profile override changes that policy.
This is release-metadata classification; it does not inspect the encoded
picture to measure perceptual quality. The optional
[compression worker](../docker/compression/README.md) remains a separate feature.

The codec names refer to the standards described by
[ITU-T H.264](https://www.itu.int/rec/T-REC-H.264),
[ITU-T H.265](https://www.itu.int/rec/T-REC-H.265),
[ITU-T H.266](https://www.itu.int/rec/T-REC-H.266) and the
[Alliance for Open Media AV1 specification index](https://aomedia.org/specifications/av1/).
Those references define codecs; they do not establish Tanukarr's exact policy
factors or guarantee equal quality at the calculated sizes.

## API compatibility

The UI conversion does not change the API or database unit. Both supported API
versions keep `minSize`, `preferredSize` and `maxSize` in **MiB per minute**:

```text
UI MiB/hour = API MiB/minute × 60
UI GiB/hour = API MiB/minute × 60 ÷ 1024
```

`GET /api/v3/qualitydefinition` and `/api/v5/qualitydefinition` return values
summarized from actual profile entries. `minSizeIsMixed`,
`preferredSizeIsMixed` and `maxSizeIsMixed` indicate disagreement; a null value
with its mixed flag true must not be rendered as an agreed unlimited value.
Single and bulk quality-definition updates distinguish omitted properties from
explicit JSON null. Omitted size properties preserve stored profile values;
explicit null clears the selected limit. The merged batch is validated before
writing. Client integrations should send only the fields they intend to change
and should keep their existing MiB/minute conversion.

Both APIs support creating and retiring quality definitions. Quality definitions
expose `source`, `resolution`, `codec`, `titlePattern` and `isRetired`; `quality.id`
remains a durable identity. Quality profiles expose `codecMultipliers` as an
object keyed by canonical codec family, such as `HEVC` or `AV1`. Omitting that
object in an update preserves saved overrides; sending an empty object resets
the overrides to the default policy. Do not lowercase its keys.

The inherited API serializer can omit null size properties in a GET response.
Treat a missing read value as null and use the mixed flag to distinguish different
profile values from an agreed unlimited limit. This read behavior is separate
from omission in an update request, which preserves the stored value.

A client that echoes a GET response retains a null field marked `IsMixed=true`;
that null represents different stored values. A numeric update still applies
when its old mixed flag is echoed. To clear a limit, send null with its mixed
flag absent or false. This protects older clients that change only a title while
round-tripping the complete response.

The numeric quality/source ranking, independent per-profile editing, supported
v3/v5 route names and inherited internal Sonarr version are retained. There is
no v4 API route. See [compatibility and backups](compatibility-and-backups.md)
before changing an existing deployment, and the
[1.0.3 preparation ledger](releases/1.0.3-verification.md) and
[publication follow-up](releases/1.0.3-publication.md) for executed checks and
verified artifact identities.
