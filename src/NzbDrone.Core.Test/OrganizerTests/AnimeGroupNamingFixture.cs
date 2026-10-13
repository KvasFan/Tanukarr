using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    public class AnimeGroupNamingFixture : CoreTest<FileNameBuilder>
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

        [Test]
        public void should_render_default_root_and_season_leaves()
        {
            var season = Member(false);

            Subject.GetAnimeGroupFolder(season).Should().Be("Canonical Anime");
            Subject.GetAnimeGroupMemberFolder(season).Should().Be("Season 02");
        }

        [TestCase(AnimeGroupKind.Series, AnimeGroupMovieFolderStyle.Automatic, "Native Movie")]
        [TestCase(AnimeGroupKind.MovieSaga, AnimeGroupMovieFolderStyle.Automatic, "Film 02")]
        [TestCase(AnimeGroupKind.Series, AnimeGroupMovieFolderStyle.Numbered, "Film 02")]
        [TestCase(AnimeGroupKind.MovieSaga, AnimeGroupMovieFolderStyle.Title, "Native Movie")]
        public void should_choose_movie_title_or_number_format_from_group_kind_and_explicit_style(AnimeGroupKind kind, AnimeGroupMovieFolderStyle style, string expected)
        {
            var movie = Member(true);
            movie.AnimeGroupKind = kind;
            movie.AnimeGroupMovieFolderStyle = style;

            Subject.GetAnimeGroupMemberFolder(movie).Should().Be(expected);
        }

        [Test]
        public void should_apply_configured_templates_instead_of_hardcoded_leaves()
        {
            _config.AnimeGroupFolderFormat = "Anime - {Group Title}";
            _config.AnimeGroupSeasonFolderFormat = "Part {Group Season:000}";
            _config.AnimeGroupMovieFolderFormat = "Movie {Group Movie:00} - {Series Title}";
            _config.AnimeGroupMovieSagaFolderFormat = "Feature {Group Movie:000}";
            var season = Member(false);
            var movie = Member(true);

            Subject.GetAnimeGroupFolder(season).Should().Be("Anime - Canonical Anime");
            Subject.GetAnimeGroupMemberFolder(season).Should().Be("Part 002");
            Subject.GetAnimeGroupMemberFolder(movie).Should().Be("Movie 02 - Native Movie");
            movie.AnimeGroupKind = AnimeGroupKind.MovieSaga;
            Subject.GetAnimeGroupMemberFolder(movie).Should().Be("Feature 002");
        }

        [TestCase("CON")]
        [TestCase("nul.txt")]
        [TestCase("../Anime/Outside")]
        public void should_sanitize_catalogue_title_data_into_a_single_safe_leaf(string title)
        {
            var series = Member(false);
            series.AnimeGroupTitle = title;
            var folder = Subject.GetAnimeGroupFolder(series);

            folder.Should().NotBeNullOrWhiteSpace().And.NotBe(".").And.NotBe("..");
            folder.Should().NotContain("/").And.NotContain("\\");
            folder.Should().NotBe("CON").And.NotBe("nul.txt");
        }

        [Test]
        public void should_bound_rendered_multibyte_names_and_preserve_unicode()
        {
            var series = Member(false);
            series.AnimeGroupTitle = string.Concat(Enumerable.Repeat("猫😀", 60));
            var folder = Subject.GetAnimeGroupFolder(series);

            Encoding.UTF8.GetByteCount(folder).Should().BeLessThanOrEqualTo(180);
            Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(folder)).Should().Be(folder);
        }

        [TestCase("../{Group Title}")]
        [TestCase("{Group Title}\\child")]
        [TestCase("{Unknown Token}")]
        [TestCase("{Group Title}\nchild")]
        public void should_reject_unsafe_or_unknown_root_expressions(string expression)
        {
            _config.AnimeGroupFolderFormat = expression;
            Action build = () => Subject.GetAnimeGroupFolder(Member(false));

            build.Should().Throw<NamingFormatException>();
        }

        [Test]
        public void should_not_invent_a_zero_or_unnumbered_season_directory()
        {
            var series = Member(false);
            series.AnimeGroupSeasonNumber = null;
            Action build = () => Subject.GetAnimeGroupMemberFolder(series);

            build.Should().Throw<NamingFormatException>();
        }

        [TestCase(false, "Native Season - group 02 - S01E01")]
        [TestCase(true, "Native Movie - film 02 - S01E01")]
        public void should_expose_group_tokens_without_remapping_native_episode_numbers(bool movie, string expected)
        {
            _config.AnimeEpisodeFormat = movie
                ? "{Series Title} - film {Group Movie:00} - S{season:00}E{episode:00}"
                : "{Series Title} - group {Group Season:00} - S{season:00}E{episode:00}";
            var series = Member(movie);
            var episode = new Episode { Id = 42, SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 1 };

            Subject.BuildFileName(new List<Episode> { episode }, series, File(), customFormats: new List<CustomFormat>()).Should().Be(expected);
            episode.Id.Should().Be(42);
            episode.SeasonNumber.Should().Be(1);
            episode.EpisodeNumber.Should().Be(1);
            episode.AbsoluteEpisodeNumber.Should().Be(1);
        }

        [TestCase(false, "Native Season - 02 - S01E01")]
        [TestCase(true, "Native Movie - 02 - S01E01")]
        public void should_render_the_unavailable_member_number_as_empty(bool movie, string expected)
        {
            _config.AnimeEpisodeFormat = "{Series Title}{ - Group Season:00}{ - Group Movie:00} - S{season:00}E{episode:00}";
            var episode = new Episode { SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 1 };

            Subject.BuildFileName(new List<Episode> { episode }, Member(movie), File(), customFormats: new List<CustomFormat>()).Should().Be(expected);
        }

        [Test]
        public void should_show_grouped_folder_and_movie_filename_samples_using_unsaved_settings()
        {
            var samples = new FileNameSampleService(Subject);
            _config.AnimeEpisodeFormat = "{Series Title} - Film {Group Movie:00} - S{season:00}E{episode:00}";
            _config.AnimeGroupSeasonFolderFormat = "Part {Group Season:00}";

            samples.GetAnimeGroupFolderSample(_config).Should().Be("The Anime Group Title");
            samples.GetAnimeGroupSeasonFolderSample(_config).Should().Be("Part 02");
            samples.GetAnimeGroupMovieFolderSample(_config).Should().Be("The Movie Title");
            samples.GetAnimeGroupMovieSagaFolderSample(_config).Should().Be("Film 02");
            var movie = samples.GetAnimeGroupMovieSample(_config);
            movie.FileName.Should().Be("The Movie Title - Film 02 - S01E01");
            new FileNameValidationService().ValidateAnimeFilename(movie).Should().BeNull();
        }

        [TestCase("{Series Title} - S{Group Season:00}E{episode:00} - S{season:00}E{episode:00}")]
        [TestCase("{Series Title} - S{season:00}E{episode:00} - S{Group Season:00}E{episode:00}")]
        public void should_reject_a_group_display_number_that_disguises_the_native_season_in_a_filename(string format)
        {
            var samples = new FileNameSampleService(Subject);
            _config.AnimeEpisodeFormat = format;

            new FileNameValidationService().ValidateAnimeFilename(samples.GetAnimeSample(_config)).Should().NotBeNull();
        }

        private static EpisodeFile File()
        {
            return new EpisodeFile { Quality = new QualityModel(Quality.HDTV720p), RelativePath = "source.mkv" };
        }

        private static Series Member(bool movie)
        {
            return new Series
            {
                Title = movie ? "Native Movie" : "Native Season",
                SeriesType = SeriesTypes.Anime,
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = "manual:example",
                AnimeGroupTitle = "Canonical Anime",
                AnimeGroupMemberKind = movie ? AnimeGroupMemberKind.Movie : AnimeGroupMemberKind.Season,
                AnimeGroupKind = AnimeGroupKind.Series,
                AnimeGroupMovieFolderStyle = AnimeGroupMovieFolderStyle.Automatic,
                AnimeGroupSeasonNumber = movie ? null : 2,
                AnimeGroupMovieNumber = movie ? 2 : null
            };
        }
    }
}
