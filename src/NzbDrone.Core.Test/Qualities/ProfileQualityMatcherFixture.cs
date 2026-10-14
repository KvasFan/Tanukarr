using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Qualities
{
    [TestFixture]
    public class ProfileQualityMatcherFixture : CoreTest<ProfileQualityMatcher>
    {
        private Quality _custom;
        private QualityDefinition _definition;
        private QualityProfile _profile;

        [SetUp]
        public void Setup()
        {
            _custom = new Quality { Id = 10021, Name = "Generated BdRip4k", Source = QualitySource.Bluray, Resolution = 2160 };
            _definition = new QualityDefinition(_custom) { Id = 42, Source = QualitySource.Bluray, Resolution = 2160, Codec = "HEVC", TitlePattern = "BDRip" };
            _profile = new QualityProfile
            {
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Quality = Quality.Unknown, Allowed = false },
                    new QualityProfileQualityItem { Quality = Quality.Bluray2160p, Allowed = true },
                    new QualityProfileQualityItem { Quality = _custom, Allowed = true }
                }
            };
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.All()).Returns(new List<QualityDefinition>
            {
                new QualityDefinition(Quality.Bluray2160p), _definition
            });
        }

        [TestCase("HEVC", "Generated.Show.S01E01.2160p.BDRip.x265", true)]
        [TestCase("H264", "Generated.Show.S01E01.2160p.BDRip.x264", false)]
        [TestCase("HEVC", "Generated.Show.S01E01.2160p.Bluray.x265", false)]
        public void should_match_only_complete_configured_parameters(string codec, string title, bool expected)
        {
            Subject.Match(_profile, Quality.Bluray2160p, title, codec).Should().Be(expected ? _custom : Quality.Bluray2160p);
        }

        [Test]
        public void should_not_match_a_different_source_or_resolution()
        {
            Subject.Match(_profile, Quality.WEBDL2160p, "BDRip", "HEVC").Should().Be(Quality.WEBDL2160p);
            Subject.Match(_profile, Quality.Bluray1080p, "BDRip", "HEVC").Should().Be(Quality.Bluray1080p);
        }

        [Test]
        public void should_use_only_the_selected_profile_and_keep_a_disabled_group_disabled()
        {
            _profile.Items[2] = new QualityProfileQualityItem
            {
                Id = 1000, Allowed = false,
                Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = _custom, Allowed = true } }
            };
            Subject.Match(_profile, Quality.Bluray2160p, "BDRip", "HEVC").Should().Be(Quality.Bluray2160p);
        }

        [Test]
        public void should_never_match_a_deleted_quality()
        {
            _definition.IsRetired = true;
            Subject.Match(_profile, Quality.Bluray2160p, "BDRip", "HEVC").Should().Be(Quality.Bluray2160p);
        }

        [TestCase("[", "BDRip")]
        [TestCase("^(a+)+$", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!")]
        public void should_fail_closed_for_invalid_or_timed_out_stored_expressions(string pattern, string title)
        {
            _definition.TitlePattern = pattern;
            Subject.Match(_profile, Quality.Bluray2160p, title, "HEVC").Should().Be(Quality.Bluray2160p);
        }

        [Test]
        public void should_apply_matching_to_actual_release_and_import_models_without_changing_bytes()
        {
            var series = new Series { QualityProfile = (LazyLoaded<QualityProfile>)_profile };
            var remote = new RemoteEpisode
            {
                Series = series,
                Release = new ReleaseInfo { Title = "Generated.Show.S01E01.2160p.BDRip.x265", Size = 123456, Codec = "x265" },
                ParsedEpisodeInfo = new ParsedEpisodeInfo { Quality = new QualityModel(Quality.Bluray2160p) }
            };
            Subject.Apply(remote);
            remote.ParsedEpisodeInfo.Quality.Quality.Should().Be(_custom);
            remote.Release.Size.Should().Be(123456);

            var local = new LocalEpisode
            {
                Series = series, Path = "Generated.Show.S01E01.2160p.BDRip.mkv", Size = 123456,
                Quality = new QualityModel(Quality.Bluray2160p), MediaInfo = new MediaInfoModel { VideoCodecID = "HEVC" }
            };
            Subject.Apply(local);
            local.Quality.Quality.Should().Be(_custom);
            local.Size.Should().Be(123456);
        }

        [Test]
        public void should_not_reclassify_existing_library_files_or_existing_custom_identities()
        {
            var local = new LocalEpisode
            {
                Series = new Series { QualityProfile = (LazyLoaded<QualityProfile>)_profile }, ExistingFile = true,
                Path = "Generated.Show.S01E01.2160p.BDRip.x265.mkv", Quality = new QualityModel(Quality.Bluray2160p)
            };
            Subject.Apply(local);
            local.Quality.Quality.Should().Be(Quality.Bluray2160p);
            Subject.Match(_profile, _custom, "Changed title", "H264").Should().Be(_custom);
        }

        [Test]
        public void should_support_stored_custom_ids_without_polluting_the_builtin_parser_catalogue()
        {
            var count = Quality.All.Count;
            Quality.Register(_custom);
            Quality.FindById(_custom.Id).Should().Be(_custom);
            Quality.FindById(190001).Id.Should().Be(190001);
            new QualityModel(_custom).CompareTo(new QualityModel(Quality.Bluray1080p)).Should().BePositive();
            Quality.All.Should().HaveCount(count);
            QualityFinder.FindBySourceAndResolution(QualitySource.Bluray, 2160).Should().Be(Quality.Bluray2160p);
        }
    }
}
