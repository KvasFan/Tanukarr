using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]
    public class AnimeGroupArcNamingFixture : CoreTest<FileNameBuilder>
    {
        private NamingConfig _config;

        [SetUp]
        public void Setup()
        {
            _config = NamingConfig.Default;
            _config.RenameEpisodes = true;
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(_config);
            Mocker.GetMock<IQualityDefinitionService>().Setup(s => s.Get(It.IsAny<Quality>()))
                .Returns<Quality>(quality => Quality.DefaultQualityDefinitions.First(d => d.Quality == quality));
        }

        [TestCase("Tokyo Revengers", "Tokyo Revengers: War of Three Titans Arc", "War of Three Titans Arc")]
        [TestCase("Tokyo Revengers", "Tokyo Revengers - War of Three Titans Arc", "War of Three Titans Arc")]
        [TestCase("Tokyo Revengers", "Tokyo Revengers: Three Titans-hen", "Three Titans-hen")]
        [TestCase("Tokyo Revengers", "Tokyo Revengers: Three Titans Chapter", "Three Titans Chapter")]
        [TestCase("The Apothecary Diaries", "The Apothecary Diaries Season 3", "Season 03")]
        [TestCase("Tokyo Revengers", "Tokyo Revengers: Second Season", "Season 03")]
        [TestCase("Tokyo Revengers", "A Different Anime: War of Three Titans Arc", "Season 03")]
        [TestCase("Tokyo Revengers", "Tokyo Revengers Extra: War of Three Titans Arc", "Season 03")]
        public void should_only_choose_an_automatic_arc_leaf_from_a_strict_canonical_prefix_and_supported_suffix(string canonical, string title, string expected)
        {
            var series = Season(canonical, title);

            Subject.GetAnimeGroupFolder(series).Should().Be(canonical);
            Subject.GetAnimeGroupMemberFolder(series).Should().Be(expected);
            series.AnimeGroupSeasonNumber.Should().Be(3);
        }

        [TestCase(AnimeGroupMovieFolderStyle.Numbered, "Tokyo Revengers: War of Three Titans Arc", "Season 03")]
        [TestCase(AnimeGroupMovieFolderStyle.Title, "Tokyo Revengers: War of Three Titans Arc", "War of Three Titans Arc")]
        [TestCase(AnimeGroupMovieFolderStyle.Title, "Tokyo Revengers Season 3", "Tokyo Revengers Season 3")]
        public void should_honor_explicit_season_folder_style_without_guessing_an_identity(AnimeGroupMovieFolderStyle style, string title, string expected)
        {
            var series = Season("Tokyo Revengers", title);
            series.AnimeGroupMovieFolderStyle = style;

            Subject.GetAnimeGroupMemberFolder(series).Should().Be(expected);
        }

        [Test]
        public void should_apply_the_configured_arc_template_and_three_word_filename_token()
        {
            var series = Season("Tokyo Revengers", "Tokyo Revengers: War of Three Titans Arc");
            _config.AnimeGroupArcFolderFormat = "Story - {Group Arc Title}";
            _config.AnimeEpisodeFormat = "{Group Title} - {Group Arc Title} - S{season:00}E{episode:00}";
            var episode = new Episode { Id = 40, SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 1 };

            Subject.GetAnimeGroupMemberFolder(series).Should().Be("Story - War of Three Titans Arc");
            Subject.BuildFileName(new List<Episode> { episode }, series, new EpisodeFile { Quality = new QualityModel(Quality.HDTV720p) }, customFormats: new List<CustomFormat>())
                .Should().Be("Tokyo Revengers - War of Three Titans Arc - S01E01");
            episode.SeasonNumber.Should().Be(1);
            episode.Id.Should().Be(40);
        }

        [Test]
        public void should_show_the_actual_arc_expression_in_naming_samples()
        {
            _config.AnimeGroupArcFolderFormat = "Story - {Group Arc Title}";

            new FileNameSampleService(Subject).GetAnimeGroupArcFolderSample(_config).Should().Be("Story - War of Three Titans Arc");
        }

        [TestCase("{Group Arc Title}", true)]
        [TestCase("{Group Arc Title:30}", true)]
        [TestCase("{Group Title} - {Group Arc Title}", true)]
        [TestCase("{Group Arc Title:0}", false)]
        [TestCase("{Group Arc Title:181}", false)]
        [TestCase("../{Group Arc Title}", false)]
        [TestCase("{Group Season:00}", false)]
        public void should_validate_arc_folder_tokens_and_required_title(string format, bool expected)
        {
            AnimeGroupNamingFormat.IsValid(format, AnimeGroupFolderFormatKind.Arc).Should().Be(expected);
        }

        [TestCase("{Group Arc Title}", true)]
        [TestCase("{ - Group Arc Title:40}", true)]
        [TestCase("{Group Arc Title:0}", false)]
        [TestCase("{Group Arc Title:181}", false)]
        public void should_validate_three_word_group_tokens_in_existing_filename_expressions(string format, bool expected)
        {
            AnimeGroupNamingFormat.HasValidGroupTokens(format + " - S{season:00}E{episode:00}").Should().Be(expected);
        }

        [Test]
        public void should_keep_numbered_seasons_when_arc_expression_is_not_needed()
        {
            _config.AnimeGroupArcFolderFormat = "../{Group Arc Title}";
            var series = Season("The Apothecary Diaries", "The Apothecary Diaries Season 3");

            Subject.GetAnimeGroupMemberFolder(series).Should().Be("Season 03");
            series.AnimeGroupMovieFolderStyle = AnimeGroupMovieFolderStyle.Title;
            Action build = () => Subject.GetAnimeGroupMemberFolder(series);
            build.Should().Throw<NamingFormatException>();
        }

        private static Series Season(string canonical, string title)
        {
            return new Series
            {
                Title = title,
                SeriesType = SeriesTypes.Anime,
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = "manual:arc-fixture",
                AnimeGroupTitle = canonical,
                AnimeGroupMemberKind = AnimeGroupMemberKind.Season,
                AnimeGroupKind = AnimeGroupKind.Series,
                AnimeGroupSeasonNumber = 3,
                AnimeGroupMovieFolderStyle = AnimeGroupMovieFolderStyle.Automatic
            };
        }
    }
}
