using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Qualities
{
    public static class CodecSizeNormalizer
    {
        private static readonly Regex CodecTokens = new Regex(
            @"(?<![\p{L}\p{N}])(?:" +
            @"(?<MPEG1>mpeg[ ._-]?1(?:video)?)|" +
            @"(?<MPEG2>x262|h[ ._-]?262|mpeg[ ._-]?2(?:video)?)|" +
            @"(?<MPEG4ASP>xvid(?:hd)?|divx|mpeg[ ._-]?4[ ._-]?asp|msmpeg4(?:v[123])?)|" +
            @"(?<H263>h[ ._-]?263)|" +
            @"(?<H264>x264|h[ ._-]?264|avc1?)|" +
            @"(?<HEVC>x265|h[ ._-]?265|hevc|hvc1|hev1)|" +
            @"(?<AV1>av1|av01|svt[ ._-]?av1|libaom[ ._-]?av1)|" +
            @"(?<VVC>x266|h[ ._-]?266|vvc)|" +
            @"(?<VC1>vc[ ._-]?1)|" +
            @"(?<WMV1>wmv1)|(?<WMV2>wmv2)|(?<WMV3>wmv3)|" +
            @"(?<VP6>vp6(?:f|a)?)|(?<VP7>vp7)|(?<VP8>vp8)|(?<VP9>vp9)|" +
            @"(?<Theora>theora)|" +
            @"(?<Unknown>ffv1|huffyuv|ffvhuff|rawvideo|utvideo|prores|dnxhd|dnxhr|jpeg[ ._-]?2000|j2k)" +
            @")(?![\p{L}\p{N}])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        private static readonly IReadOnlyDictionary<string, decimal> Multipliers =
            new ReadOnlyDictionary<string, decimal>(new Dictionary<string, decimal>
            {
                { "MPEG1", 0.40m },
                { "MPEG2", 0.50m },
                { "MPEG4ASP", 0.67m },
                { "H263", 0.40m },
                { "H264", 1.00m },
                { "HEVC", 1.67m },
                { "AV1", 2.22m },
                { "VVC", 2.50m },
                { "VC1", 0.80m },
                { "WMV1", 0.60m },
                { "WMV2", 0.60m },
                { "WMV3", 0.80m },
                { "VP6", 0.60m },
                { "VP7", 0.60m },
                { "VP8", 1.00m },
                { "VP9", 1.55m },
                { "Theora", 0.50m },
                { "Unknown", 1.00m }
            });

        public static IReadOnlyDictionary<string, decimal> MultiplierCatalog { get; } =
            new ReadOnlyDictionary<string, decimal>(new Dictionary<string, decimal>
            {
                { "MPEG-1", Multipliers["MPEG1"] },
                { "MPEG-2 / H.262 / x262", Multipliers["MPEG2"] },
                { "MPEG-4 ASP / XviD / DivX", Multipliers["MPEG4ASP"] },
                { "H.263", Multipliers["H263"] },
                { "H.264 / AVC / x264", Multipliers["H264"] },
                { "H.265 / HEVC / x265", Multipliers["HEVC"] },
                { "AV1", Multipliers["AV1"] },
                { "H.266 / VVC / x266", Multipliers["VVC"] },
                { "VC-1 / WMV3", Multipliers["VC1"] },
                { "WMV1 / WMV2", Multipliers["WMV1"] },
                { "VP6 / VP7", Multipliers["VP6"] },
                { "VP8", Multipliers["VP8"] },
                { "VP9", Multipliers["VP9"] },
                { "Theora", Multipliers["Theora"] },
                { "Unknown / mixed / lossless", Multipliers["Unknown"] }
            });

        public static IReadOnlyDictionary<string, decimal> DefaultMultipliers => Multipliers;

        public static string DetectCodec(RemoteEpisode episode)
        {
            return StructuredCodec(episode.Release.Codec) ?? TitleCodec(episode);
        }

        public static bool ValidOverrides(IDictionary<string, decimal> values)
        {
            return values != null && values.Count <= Multipliers.Count &&
                values.All(pair => Multipliers.ContainsKey(pair.Key) && pair.Value >= 0.01m && pair.Value <= 10m);
        }

        public static (string Codec, decimal Multiplier, decimal Size) Normalize(RemoteEpisode episode)
        {
            var release = episode.Release;
            var codec = DetectCodec(episode);
            var multiplier = Multipliers[codec];
            var overrides = episode.Series?.QualityProfile?.Value?.CodecMultipliers;
            if (overrides != null && overrides.TryGetValue(codec, out var configured) && configured >= 0.01m && configured <= 10m)
            {
                multiplier = configured;
            }

            // This value is used only in quality-size decisions. The release and
            // its persisted/downloaded size always retain the actual byte count.
            return (codec, multiplier, release.Size * multiplier);
        }

        public static string Describe(RemoteEpisode episode, (string Codec, decimal Multiplier, decimal Size) normalized)
        {
            var actual = episode.Release.Size.SizeSuffix();
            if (normalized.Multiplier == 1m)
            {
                return actual;
            }

            var size = normalized.Size <= long.MaxValue && normalized.Size >= long.MinValue
                ? ((long)Math.Round(normalized.Size)).SizeSuffix()
                : (normalized.Size / 1073741824m).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} actual, {1} codec-normalized ({2} ×{3:0.00})",
                actual,
                size,
                normalized.Codec,
                normalized.Multiplier);
        }

        private static string StructuredCodec(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            {
                return null;
            }

            var normalized = value.Trim().Replace(" ", string.Empty).Replace(".", string.Empty)
                .Replace("-", string.Empty).Replace("_", string.Empty).ToUpperInvariant();
            var codec = normalized switch
            {
                "MPEG1" or "MPEG1VIDEO" => "MPEG1",
                "X262" or "H262" or "MPEG2" or "MPEG2VIDEO" => "MPEG2",
                "XVID" or "XVIDHD" or "DIVX" or "DIV3" or "DX50" or "MPEG4" or "MPEG4ASP" or "MSMPEG4"
                    or "MSMPEG4V1" or "MSMPEG4V2" or "MSMPEG4V3" => "MPEG4ASP",
                "H263" => "H263",
                "X264" or "H264" or "AVC" or "AVC1" or "LIBX264" => "H264",
                "X265" or "H265" or "HEVC" or "HVC1" or "HEV1" or "LIBX265" => "HEVC",
                "AV1" or "AV01" or "SVTAV1" or "LIBSVTAV1" or "LIBAOMAV1" => "AV1",
                "X266" or "H266" or "VVC" or "LIBX266" => "VVC",
                "VC1" => "VC1",
                "WMV1" => "WMV1",
                "WMV2" => "WMV2",
                "WMV3" => "WMV3",
                "VP6" or "VP6F" or "VP6A" => "VP6",
                "VP7" => "VP7",
                "VP8" => "VP8",
                "VP9" => "VP9",
                "THEORA" => "Theora",
                "FFV1" or "HUFFYUV" or "FFVHUFF" or "RAWVIDEO" or "UTVIDEO" or "PRORES" or "DNXHD" or "DNXHR"
                    or "JPEG2000" or "J2K" => "Unknown",
                "MIXED" or "MULTIPLE" or "MULTICODEC" or "MULTICODECS" or "MULTIPLECODECS" => "Unknown",
                _ => null
            };

            if (codec != null)
            {
                return codec;
            }

            var parts = value.Split(new[] { '/', '+', '&', '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return null;
            }

            var families = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                var family = StructuredCodec(part);
                if (family == null || family == "Unknown")
                {
                    return "Unknown";
                }

                families.Add(family);
            }

            if (families.Count == 1)
            {
                foreach (var family in families)
                {
                    return family;
                }
            }

            return "Unknown";
        }

        private static string TitleCodec(RemoteEpisode episode)
        {
            var parsed = episode.ParsedEpisodeInfo;
            var tokens = parsed?.ReleaseTokens;
            if (string.IsNullOrWhiteSpace(tokens))
            {
                tokens = episode.Release.Title ?? parsed?.ReleaseTitle ?? string.Empty;
                var seriesTitle = parsed?.SeriesTitle;
                if (!string.IsNullOrWhiteSpace(seriesTitle))
                {
                    tokens = tokens.Replace(".", " ").Replace("_", " ")
                        .Replace(seriesTitle.Replace(".", " ").Replace("_", " "), string.Empty, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (tokens.Length > 16384)
            {
                return "Unknown";
            }

            if (!string.IsNullOrWhiteSpace(parsed?.ReleaseGroup))
            {
                tokens = tokens.Replace("[" + parsed.ReleaseGroup + "]", string.Empty, StringComparison.OrdinalIgnoreCase);
                var suffix = "-" + parsed.ReleaseGroup;
                var encoderTag = tokens.EndsWith("SVT-AV1", StringComparison.OrdinalIgnoreCase)
                    || tokens.EndsWith("libaom-AV1", StringComparison.OrdinalIgnoreCase);
                if (!encoderTag && tokens.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    tokens = tokens.Substring(0, tokens.Length - suffix.Length);
                }
            }

            var codecs = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (Match match in CodecTokens.Matches(tokens))
                {
                    foreach (var codec in Multipliers.Keys)
                    {
                        if (match.Groups[codec].Success)
                        {
                            codecs.Add(codec);
                        }
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return "Unknown";
            }

            // A mixed-codec pack cannot safely be modeled as one codec. Unknown
            // and lossless/mezzanine formats receive no assumed efficiency gain.
            if (codecs.Count != 1)
            {
                return "Unknown";
            }

            foreach (var codec in codecs)
            {
                return codec;
            }

            return "Unknown";
        }
    }
}
