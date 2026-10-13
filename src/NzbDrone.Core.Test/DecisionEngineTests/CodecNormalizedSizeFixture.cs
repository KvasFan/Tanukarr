using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class CodecNormalizedSizeFixture : CoreTest<AcceptableSizeSpecification>
    {
        private RemoteEpisode _remote;
        private QualityProfileQualityItem _limits;

        [SetUp]
        public void Setup()
        {
            _limits = new QualityProfileQualityItem { Quality = Quality.WEBDL1080p, MinSize = 8, MaxSize = 9 };
            _remote = new RemoteEpisode
            {
                Release = new ReleaseInfo
                {
                    Title = "Generated.Show.S01E01.1080p.WEB-DL.x265",
                    Size = 150.Megabytes(),
                    PublishDate = DateTime.UtcNow,
                    DownloadProtocol = DownloadProtocol.Torrent
                },
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    Quality = new QualityModel(Quality.WEBDL1080p),
                    SeasonNumber = 1,
                    EpisodeNumbers = new[] { 1 },
                    SeriesTitle = "Generated Show",
                    ReleaseTokens = ".1080p.WEB-DL.x265"
                },
                Episodes = new List<Episode> { new Episode { Id = 1, SeasonNumber = 1, EpisodeNumber = 1, Runtime = 30 } },
                Series = new Series
                {
                    Id = 1,
                    Runtime = 30,
                    MetadataSource = MetadataSourceType.Tvdb,
                    ForeignId = "123456",
                    QualityProfile = (LazyLoaded<QualityProfile>)new QualityProfile
                    {
                        Items = new List<QualityProfileQualityItem> { _limits }
                    },
                    Seasons = new List<Season> { new Season { SeasonNumber = 1 } }
                }
            };
        }

        [TestCase("x262", 0.50)]
        [TestCase("H.262", 0.50)]
        [TestCase("mpeg2video", 0.50)]
        [TestCase("MPEG-2", 0.50)]
        [TestCase("x264", 1.00)]
        [TestCase("H.264", 1.00)]
        [TestCase("AVC", 1.00)]
        [TestCase("avc1", 1.00)]
        [TestCase("libx264", 1.00)]
        [TestCase("x265", 1.67)]
        [TestCase("H.265", 1.67)]
        [TestCase("HEVC", 1.67)]
        [TestCase("hvc1", 1.67)]
        [TestCase("hev1", 1.67)]
        [TestCase("libx265", 1.67)]
        [TestCase("AV1", 2.22)]
        [TestCase("av01", 2.22)]
        [TestCase("libaom-av1", 2.22)]
        [TestCase("SVT-AV1", 2.22)]
        public void should_normalize_a_one_gib_release_from_standardized_codec_metadata(string codec, double expected)
        {
            _remote.Release.Size = 1.Gigabytes();
            _remote.Release.Codec = codec;
            _remote.ParsedEpisodeInfo.ReleaseTokens = ".1080p.WEB-DL";

            var normalized = CodecSizeNormalizer.Normalize(_remote);

            normalized.Multiplier.Should().Be((decimal)expected);
            normalized.Size.Should().Be(1.Gigabytes() * (decimal)expected);
            _remote.Release.Size.Should().Be(1.Gigabytes());
        }

        [TestCase("mpeg1video", 0.40)]
        [TestCase("H.263", 0.40)]
        [TestCase("XviD", 0.67)]
        [TestCase("DivX", 0.67)]
        [TestCase("msmpeg4v3", 0.67)]
        [TestCase("vc1", 0.80)]
        [TestCase("wmv3", 0.80)]
        [TestCase("wmv1", 0.60)]
        [TestCase("wmv2", 0.60)]
        [TestCase("vp6f", 0.60)]
        [TestCase("vp7", 0.60)]
        [TestCase("vp8", 1.00)]
        [TestCase("vp9", 1.55)]
        [TestCase("h266", 2.50)]
        [TestCase("Theora", 0.50)]
        public void should_apply_approved_policy_weights_to_other_recognized_codecs(string codec, double expected)
        {
            _remote.Release.Codec = codec;

            CodecSizeNormalizer.Normalize(_remote).Multiplier.Should().Be((decimal)expected);
        }

        [TestCase("h264", "H264", 1.00)]
        [TestCase("hevc", "HEVC", 1.67)]
        [TestCase("av1", "AV1", 2.22)]
        [TestCase("vvc", "VVC", 2.50)]
        [TestCase("mpeg2video", "MPEG2", 0.50)]
        [TestCase("vp9", "VP9", 1.55)]
        public void should_accept_codec_names_produced_by_native_media_information(string format, string family, double expected)
        {
            _remote.Release.Codec = MediaInfoFormatter.FormatVideoCodec(new MediaInfoModel { VideoFormat = format }, null);

            var normalized = CodecSizeNormalizer.Normalize(_remote);

            normalized.Codec.Should().Be(family);
            normalized.Multiplier.Should().Be((decimal)expected);
        }

        [TestCase("x262", 0.50)]
        [TestCase("x264", 1.00)]
        [TestCase("x265", 1.67)]
        [TestCase("AV1", 2.22)]
        [TestCase("H.265", 1.67)]
        [TestCase("HEVC", 1.67)]
        [TestCase("SVT-AV1", 2.22)]
        public void should_use_actual_parser_technical_tokens_when_the_indexer_omits_codec_metadata(string codec, double expected)
        {
            _remote.Release.Codec = null;
            _remote.Release.Title = "Generated.Show.S01E01.1080p.WEB-DL." + codec;
            _remote.ParsedEpisodeInfo = NzbDrone.Core.Parser.Parser.ParseTitle(_remote.Release.Title);

            CodecSizeNormalizer.Normalize(_remote).Multiplier.Should().Be((decimal)expected);
        }

        [TestCase("AV1.S01E01.1080p.WEB-DL.AAC")]
        [TestCase("Generated.Show.S01E01.1080p.WEB-DL.AV10")]
        [TestCase("Generated.Show.S01E01.1080p.WEB-DL.AACAV1")]
        [TestCase("Generated.Show.S01E01.1080p.WEB-DL.x2650")]
        [TestCase("Generated.Show.S01E01.1080p.WEB-DL.x265+AV1")]
        [TestCase("Generated.Show.S01E01.1080p.WEB-DL.x264+x265")]
        public void should_not_assume_efficiency_from_series_titles_partial_words_or_mixed_packs(string title)
        {
            _remote.Release.Codec = null;
            _remote.Release.Title = title;
            _remote.ParsedEpisodeInfo = NzbDrone.Core.Parser.Parser.ParseTitle(title);

            CodecSizeNormalizer.Normalize(_remote).Multiplier.Should().Be(1.00m);
        }

        [Test]
        public void should_ignore_a_codec_named_release_group()
        {
            _remote.Release.Codec = null;
            _remote.ParsedEpisodeInfo.ReleaseTokens = ".1080p.WEB-DL-AV1";
            _remote.ParsedEpisodeInfo.ReleaseGroup = "AV1";

            CodecSizeNormalizer.Normalize(_remote).Multiplier.Should().Be(1.00m);
        }

        [Test]
        public void should_remove_known_series_title_before_using_the_raw_title_fallback()
        {
            _remote.Release.Codec = null;
            _remote.Release.Title = "AV1.S01E01.1080p.WEB-DL.AAC";
            _remote.ParsedEpisodeInfo.SeriesTitle = "AV1";
            _remote.ParsedEpisodeInfo.ReleaseTokens = null;

            CodecSizeNormalizer.Normalize(_remote).Multiplier.Should().Be(1.00m);
        }

        [Test]
        public void should_prefer_recognized_indexer_codec_over_conflicting_title_tokens()
        {
            _remote.Release.Codec = "H.264";
            _remote.ParsedEpisodeInfo.ReleaseTokens = ".1080p.WEB-DL.x265.AV1";

            var normalized = CodecSizeNormalizer.Normalize(_remote);

            normalized.Codec.Should().Be("H264");
            normalized.Multiplier.Should().Be(1.00m);
        }

        [TestCase("mixed")]
        [TestCase("multiple codecs")]
        [TestCase("x264 / x265")]
        [TestCase("HEVC + AV1")]
        [TestCase("AV1 / Unknown")]
        [TestCase("ffv1")]
        public void should_keep_explicit_mixed_or_lossless_metadata_neutral_despite_a_single_codec_title(string codec)
        {
            _remote.Release.Codec = codec;
            _remote.ParsedEpisodeInfo.ReleaseTokens = ".1080p.WEB-DL.x265";

            CodecSizeNormalizer.Normalize(_remote).Multiplier.Should().Be(1.00m);
        }

        [TestCase("ffv1")]
        [TestCase("huffyuv")]
        [TestCase("rawvideo")]
        [TestCase("prores")]
        [TestCase("Unknown")]
        [TestCase("AAC (AV1 audio description)")]
        [TestCase("x264 / x265")]
        public void should_keep_unrecognized_ambiguous_and_lossless_metadata_neutral(string codec)
        {
            _remote.Release.Codec = codec;
            _remote.ParsedEpisodeInfo.ReleaseTokens = ".1080p.WEB-DL.AAC";

            CodecSizeNormalizer.Normalize(_remote).Multiplier.Should().Be(1.00m);
        }

        [Test]
        public void should_bound_codec_token_parsing_and_avoid_long_overflow()
        {
            _remote.Release.Codec = null;
            _remote.Release.Size = long.MaxValue;
            _remote.ParsedEpisodeInfo.ReleaseTokens = new string('x', 17000) + ".AV1";
            CodecSizeNormalizer.Normalize(_remote).Size.Should().Be((decimal)long.MaxValue);

            _remote.Release.Codec = "AV1";
            CodecSizeNormalizer.Normalize(_remote).Size.Should().Be(long.MaxValue * 2.22m);
            _remote.Release.Size.Should().Be(long.MaxValue);
        }

        [TestCase(1, false, false)]
        [TestCase(2, false, false)]
        [TestCase(10, true, false)]
        [TestCase(20, true, true)]
        public void should_apply_normalization_once_to_single_episode_season_and_multiseason_totals(int episodeCount, bool fullSeason, bool multiSeason)
        {
            _remote.Episodes = Enumerable.Range(1, episodeCount)
                .Select(number => new Episode
                {
                    Id = number,
                    SeasonNumber = multiSeason && number > episodeCount / 2 ? 2 : 1,
                    EpisodeNumber = number,
                    Runtime = 30
                }).ToList();
            _remote.ParsedEpisodeInfo.FullSeason = fullSeason;
            _remote.ParsedEpisodeInfo.SeasonNumbers = multiSeason ? new List<int> { 1, 2 } : new List<int> { 1 };
            _remote.Release.Size = episodeCount * 150.Megabytes();
            var actual = _remote.Release.Size;

            Subject.IsSatisfiedBy(_remote, new()).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(_remote, new()).Accepted.Should().BeTrue();

            _remote.Release.Size.Should().Be(actual);
            _remote.Episodes.Sum(episode => episode.Runtime).Should().Be(30 * episodeCount);
            _remote.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(multiSeason ? new[] { 1, 2 } : new[] { 1 });
        }

        [TestCase(-1, DownloadRejectionReason.BelowMinimumSize)]
        [TestCase(0, DownloadRejectionReason.Unknown)]
        [TestCase(1, DownloadRejectionReason.AboveMaximumSize)]
        public void should_compare_exact_virtual_bytes_at_both_size_boundaries(int byteOffset, DownloadRejectionReason reason)
        {
            _limits.MinSize = 167;
            _limits.MaxSize = 167;
            _remote.Release.Size = 100.Megabytes() + byteOffset;
            _remote.Release.Codec = "x265";
            _remote.Episodes[0].Runtime = 1;

            var result = Subject.IsSatisfiedBy(_remote, new());

            result.Accepted.Should().Be(byteOffset == 0);
            if (byteOffset != 0)
            {
                result.Reason.Should().Be(reason);
                result.Message.Should().Contain("codec-normalized").And.Contain("HEVC ×1.67");
            }

            _remote.Release.Size.Should().Be(100.Megabytes() + byteOffset);
        }

        [TestCase(MetadataSourceType.Tvdb)]
        [TestCase(MetadataSourceType.Tmdb)]
        [TestCase(MetadataSourceType.AniList)]
        [TestCase(MetadataSourceType.MyAnimeList)]
        public void should_preserve_metadata_owner_native_numbering_and_languages(MetadataSourceType source)
        {
            _remote.Series.MetadataSource = source;
            _remote.Series.SeriesType = SeriesTypes.Anime;
            _remote.Series.AnimeGroupSeasonNumber = 3;
            _remote.Release.Languages = new List<Language> { Language.Japanese };
            _remote.ParsedEpisodeInfo.Languages = new List<Language> { Language.Japanese, Language.English };

            Subject.IsSatisfiedBy(_remote, new()).Accepted.Should().BeTrue();

            _remote.Series.MetadataSource.Should().Be(source);
            _remote.Series.ForeignId.Should().Be("123456");
            _remote.Series.AnimeGroupSeasonNumber.Should().Be(3);
            _remote.Episodes.Single().SeasonNumber.Should().Be(1);
            _remote.Episodes.Single().EpisodeNumber.Should().Be(1);
            _remote.Release.Languages.Should().Equal(Language.Japanese);
            _remote.ParsedEpisodeInfo.Languages.Should().Equal(Language.Japanese, Language.English);
        }

        [Test]
        public void should_sum_episode_runtime_with_series_fallback_before_applying_codec_policy()
        {
            _limits.MinSize = 30;
            _limits.MaxSize = 35;
            _remote.Series.Runtime = 60;
            _remote.Episodes.Add(new Episode { SeasonNumber = 2, EpisodeNumber = 1, Runtime = 0 });
            _remote.Release.Size = 1700.Megabytes();

            Subject.IsSatisfiedBy(_remote, new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_preserve_special_and_unknown_size_exemptions()
        {
            _limits.MinSize = 1000;
            _limits.MaxSize = 2000;
            _remote.Release.Codec = "AV1";
            _remote.Release.Size = 1;
            _remote.ParsedEpisodeInfo.Special = true;
            Subject.IsSatisfiedBy(_remote, new()).Accepted.Should().BeTrue();

            _remote.ParsedEpisodeInfo.Special = false;
            _remote.Release.Size = 0;
            Subject.IsSatisfiedBy(_remote, new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_keep_the_physical_indexer_size_cap_independent_of_codec_normalization()
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.MaximumSize).Returns(200);
            _remote.Release.Size = 150.Megabytes();
            _remote.Release.Codec = "AV1";
            var physical = Mocker.Resolve<MaximumSizeSpecification>();

            physical.IsSatisfiedBy(_remote, new()).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(_remote, new()).Reason.Should().Be(DownloadRejectionReason.AboveMaximumSize);

            _remote.Release.Size = 300.Megabytes();
            _remote.Release.Codec = "x262";
            physical.IsSatisfiedBy(_remote, new()).Reason.Should().Be(DownloadRejectionReason.MaximumSizeExceeded);
            _remote.Release.Size.Should().Be(300.Megabytes());
        }

        [Test]
        public void should_use_virtual_size_for_preferred_release_ranking_without_mutating_either_release()
        {
            _limits.PreferredSize = 50;
            _remote.Release.Size = 900.Megabytes();
            _remote.Release.Codec = "x265";
            var h264 = OtherRelease(1200.Megabytes(), "x264");
            var comparer = Comparer();

            comparer.Compare(new DownloadDecision(_remote), new DownloadDecision(h264)).Should().BePositive();
            _remote.Release.Size.Should().Be(900.Megabytes());
            h264.Release.Size.Should().Be(1200.Megabytes());
        }

        [Test]
        public void should_preserve_200_mebibyte_truncation_buckets_for_neutral_codec_ranking()
        {
            _limits.PreferredSize = null;
            _remote.Release.Size = 300.Megabytes();
            _remote.Release.Codec = "x264";
            var h264 = OtherRelease(399.Megabytes(), "x264");

            Comparer().Compare(new DownloadDecision(_remote), new DownloadDecision(h264)).Should().Be(0);
        }

        [Test]
        public void should_rank_unlimited_preferred_sizes_using_the_same_virtual_metric()
        {
            _limits.PreferredSize = null;
            _remote.Release.Size = 900.Megabytes();
            _remote.Release.Codec = "x265";
            var h264 = OtherRelease(1200.Megabytes(), "x264");

            Comparer().Compare(new DownloadDecision(_remote), new DownloadDecision(h264)).Should().BePositive();
        }

        [Test]
        public void should_expose_the_same_required_weights_in_an_immutable_canonical_catalog()
        {
            var catalog = CodecSizeNormalizer.MultiplierCatalog;
            catalog["MPEG-2 / H.262 / x262"].Should().Be(0.50m);
            catalog["H.264 / AVC / x264"].Should().Be(1.00m);
            catalog["H.265 / HEVC / x265"].Should().Be(1.67m);
            catalog["AV1"].Should().Be(2.22m);
            catalog["Unknown / mixed / lossless"].Should().Be(1.00m);
            catalog.Should().HaveCount(15);
            Action mutate = () => ((IDictionary<string, decimal>)catalog).Clear();
            mutate.Should().Throw<NotSupportedException>();
        }

        private RemoteEpisode OtherRelease(long size, string codec)
        {
            return new RemoteEpisode
            {
                Series = _remote.Series,
                Episodes = _remote.Episodes,
                ParsedEpisodeInfo = _remote.ParsedEpisodeInfo,
                Release = new ReleaseInfo
                {
                    Size = size,
                    Codec = codec,
                    PublishDate = _remote.Release.PublishDate,
                    DownloadProtocol = _remote.Release.DownloadProtocol
                }
            };
        }

        private DownloadDecisionComparer Comparer()
        {
            var delay = new Mock<IDelayProfileService>();
            delay.Setup(service => service.BestForTags(It.IsAny<HashSet<int>>()))
                .Returns(new DelayProfile { PreferredProtocol = DownloadProtocol.Torrent });
            return new DownloadDecisionComparer(Mocker.GetMock<IConfigService>().Object, delay.Object);
        }
    }
}
