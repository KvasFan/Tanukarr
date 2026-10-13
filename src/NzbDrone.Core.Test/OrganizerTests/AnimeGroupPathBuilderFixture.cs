using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]
    public class AnimeGroupPathBuilderFixture : CoreTest<AnimeGroupPathBuilder>
    {
        private Series _series;
        private Dictionary<int, string> _paths;

        [SetUp]
        public void Setup()
        {
            _series = new Series
            {
                Id = 10,
                RootFolderPath = Path.Combine(Path.GetTempPath(), "tanukarr-group-path-fixture"),
                MetadataSource = MetadataSourceType.AniList,
                ForeignId = "102",
                AnimeGroupingMode = AnimeGroupingMode.Automatic,
                AnimeGroupId = "anilist:100",
                AnimeGroupTitle = "Canonical Anime",
                AnimeGroupFolder = "Canonical Anime",
                AnimeGroupSeasonNumber = 2
            };
            _paths = new Dictionary<int, string>();

            Mocker.GetMock<ISeriesRepository>()
                .Setup(s => s.AllSeriesPaths())
                .Returns(_paths);
        }

        [TestCase("Movie title")]
        [TestCase("Film 01")]
        [TestCase("Feature 003")]
        public void should_use_a_server_derived_movie_leaf_without_adding_a_season_folder(string leaf)
        {
            _series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            _series.AnimeGroupSeasonNumber = null;
            _series.AnimeGroupMovieNumber = 1;
            _series.AnimeGroupMemberFolder = leaf;

            var path = Subject.BuildPath(_series);

            path.Should().Be(Path.Combine(_series.RootFolderPath, "Canonical Anime", leaf));
            _series.Path = path;
            AnimeGroupPathBuilder.IsGroupedMemberPath(_series).Should().BeTrue();
        }

        [TestCase(null)]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("../outside")]
        [TestCase("child/folder")]
        [TestCase("child\\folder")]
        [TestCase("Movie\nTitle")]
        public void should_reject_a_movie_leaf_without_a_safe_single_folder(string leaf)
        {
            _series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            _series.AnimeGroupSeasonNumber = null;
            _series.AnimeGroupMovieNumber = 1;
            _series.AnimeGroupMemberFolder = leaf;

            Action build = () => Subject.BuildPath(_series);

            build.Should().Throw<ValidationException>().Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupMemberFolder");
        }

        [Test]
        public void should_preserve_a_stored_custom_season_folder_when_matching_its_registered_path()
        {
            _series.AnimeGroupMemberFolder = "Cour 002";
            _series.Path = Path.Combine(_series.RootFolderPath, "Canonical Anime", "Cour 002");

            AnimeGroupPathBuilder.IsGroupedMemberPath(_series).Should().BeTrue();
            Subject.BuildPath(_series).Should().Be(_series.Path);
        }

        [TestCase(1, "Season 01")]
        [TestCase(2, "Season 02")]
        [TestCase(100, "Season 100")]
        [TestCase(999, "Season 999")]
        public void should_generate_a_distinct_group_season_leaf_inside_the_root(int seasonNumber, string leaf)
        {
            _series.AnimeGroupSeasonNumber = seasonNumber;

            Subject.BuildPath(_series).Should().Be(Path.Combine(_series.RootFolderPath, "Canonical Anime", leaf));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("../outside")]
        [TestCase("..\\outside")]
        [TestCase("child/folder")]
        [TestCase("child\\folder")]
        public void should_reject_a_group_folder_that_can_escape_or_add_path_components(string folder)
        {
            _series.AnimeGroupFolder = folder;

            Action plan = () => Subject.BuildPath(_series);

            plan.Should().Throw<ValidationException>()
                .Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupFolder");
        }

        [Test]
        public void should_reject_an_absolute_group_folder()
        {
            _series.AnimeGroupFolder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "outside-group-root"));

            Action plan = () => Subject.BuildPath(_series);

            plan.Should().Throw<ValidationException>();
        }

        [TestCase("same")]
        [TestCase("parent")]
        [TestCase("child")]
        public void should_reject_an_overlap_with_a_different_registered_series(string overlap)
        {
            var destination = Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 02");
            _paths[20] = overlap == "parent"
                ? Path.GetDirectoryName(destination)
                : overlap == "child" ? Path.Combine(destination, "child") : destination;

            Action plan = () => Subject.BuildPath(_series);

            plan.Should().Throw<ValidationException>()
                .Which.Errors.Should().Contain(e => e.PropertyName == "Path");
        }

        [Test]
        public void should_allow_sibling_season_paths()
        {
            _paths[20] = Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 01");
            _paths[30] = Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 03");

            Subject.BuildPath(_series).Should().Be(Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 02"));
        }

        [Test]
        public void should_reject_a_different_group_that_sanitizes_to_the_same_parent_folder()
        {
            Mocker.GetMock<ISeriesRepository>().Setup(s => s.All()).Returns(new List<Series>
            {
                new Series
                {
                    Id = 20,
                    AnimeGroupId = "anilist:999",
                    Path = Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 01")
                }
            });

            Action plan = () => Subject.BuildPath(_series);

            plan.Should().Throw<ValidationException>()
                .Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupTitle");
        }

        [Test]
        public void should_allow_the_same_group_to_share_its_parent_folder()
        {
            Mocker.GetMock<ISeriesRepository>().Setup(s => s.All()).Returns(new List<Series>
            {
                new Series
                {
                    Id = 20,
                    AnimeGroupId = _series.AnimeGroupId,
                    Path = Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 01")
                }
            });

            Subject.BuildPath(_series).Should().Be(Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 02"));
        }

        [Test]
        public void should_allow_the_existing_path_of_the_current_persisted_series()
        {
            var destination = Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 02");
            _paths[_series.Id] = destination;

            Subject.BuildPath(_series).Should().Be(destination);
        }

        [TestCase("Season 02", true)]
        [TestCase("Season 01", false)]
        [TestCase("Season 2", false)]
        public void should_identify_only_the_member_group_season_leaf(string leaf, bool expected)
        {
            _series.Path = Path.Combine(_series.RootFolderPath, "Canonical Anime", leaf);

            AnimeGroupPathBuilder.IsGroupedSeasonPath(_series).Should().Be(expected);
        }

        [Test]
        public void should_not_treat_an_explicit_different_parent_as_the_group_leaf()
        {
            _series.Path = Path.Combine(_series.RootFolderPath, "Explicit folder", "Season 02");

            AnimeGroupPathBuilder.IsGroupedSeasonPath(_series).Should().BeFalse();
        }

        [Test]
        public void should_not_treat_a_disabled_member_as_a_group_leaf()
        {
            _series.Path = Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 02");
            _series.AnimeGroupingMode = AnimeGroupingMode.Disabled;

            AnimeGroupPathBuilder.IsGroupedSeasonPath(_series).Should().BeFalse();
        }

        [Test]
        public void should_recognize_a_group_leaf_with_a_trailing_directory_separator()
        {
            _series.Path = Path.Combine(_series.RootFolderPath, "Canonical Anime", "Season 02") + Path.DirectorySeparatorChar;

            AnimeGroupPathBuilder.IsGroupedSeasonPath(_series).Should().BeTrue();
        }
    }
}
