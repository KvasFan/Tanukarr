using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SeriesCollectionEditorFixture : CoreTest
    {
        private List<Series> _members;

        [SetUp]
        public void Setup()
        {
            _members = new List<Series> { Member(10, 1), Member(11, 2) };
        }

        [Test]
        public void should_apply_complete_collection_settings_without_changing_native_identity_or_existing_paths()
        {
            SeriesCollectionEditor.Apply(_members, _members, "  Corrected collection  ", AnimeGroupKind.Series, AnimeGroupMovieFolderStyle.Title, null);

            _members.Should().OnlyContain(s => s.AnimeGroupTitle == "Corrected collection" &&
                s.AnimeGroupingMode == AnimeGroupingMode.Manual && s.AnimeGroupMovieFolderStyle == AnimeGroupMovieFolderStyle.Title);
            foreach (var series in _members)
            {
                series.Title.Should().Be("Native entry " + series.Id);
                series.ForeignId.Should().Be(series.Id.ToString());
                series.TvdbId.Should().Be(-series.Id);
                series.MetadataSource.Should().Be(MetadataSourceType.AniList);
                series.Path.Should().Be("/generated/Original/Season " + series.AnimeGroupSeasonNumber);
                series.AnimeGroupId.Should().Be("anilist:100");
                series.AnimeGroupFolder.Should().Be("Original");
                series.Seasons.Should().ContainSingle(s => s.SeasonNumber == 1);
            }

            _members.Select(s => s.AnimeGroupSeasonNumber).Should().Equal(1, 2);
        }

        [Test]
        public void should_reject_a_partial_collection_before_mutating_any_member()
        {
            Action edit = () => SeriesCollectionEditor.Apply(new[] { _members[0] }, _members, "Changed", null, null, null);
            edit.Should().Throw<ValidationException>();
            AssertOriginalMembers();
        }

        [Test]
        public void should_reject_members_from_different_groups_before_mutation()
        {
            _members[1].AnimeGroupId = "anilist:200";
            Action edit = () => SeriesCollectionEditor.Apply(_members, _members, "Changed", null, null, null);
            edit.Should().Throw<ValidationException>();
            AssertOriginalMembers();
        }

        [Test]
        public void should_reject_duplicate_selected_ids_before_mutation()
        {
            Action edit = () => SeriesCollectionEditor.Apply(new[] { _members[0], _members[0] }, _members, "Changed", null, null, null);
            edit.Should().Throw<ValidationException>();
            AssertOriginalMembers();
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("Invalid\nName")]
        public void should_reject_invalid_collection_names_before_mutation(string title)
        {
            Action edit = () => SeriesCollectionEditor.Apply(_members, _members, title, null, null, null);
            edit.Should().Throw<ValidationException>();
            AssertOriginalMembers();
        }

        [Test]
        public void should_reject_overlong_names_before_mutation()
        {
            Action edit = () => SeriesCollectionEditor.Apply(_members, _members, new string('x', 181), null, null, null);
            edit.Should().Throw<ValidationException>();
            AssertOriginalMembers();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_reject_unknown_type_and_folder_style_before_mutation(bool kind)
        {
            Action edit = () => SeriesCollectionEditor.Apply(_members, _members, "Changed", kind ? (AnimeGroupKind)99 : null, kind ? null : (AnimeGroupMovieFolderStyle)99, null);
            edit.Should().Throw<ValidationException>();
            AssertOriginalMembers();
        }

        [Test]
        public void should_reject_a_movie_saga_containing_a_season_before_mutation()
        {
            Action edit = () => SeriesCollectionEditor.Apply(_members, _members, "Changed", AnimeGroupKind.MovieSaga, null, null);
            edit.Should().Throw<ValidationException>();
            AssertOriginalMembers();
        }

        [Test]
        public void should_accept_a_movie_only_saga_while_preserving_movie_numbers()
        {
            foreach (var series in _members)
            {
                series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
                series.AnimeGroupMovieNumber = series.AnimeGroupSeasonNumber;
                series.AnimeGroupSeasonNumber = null;
            }

            SeriesCollectionEditor.Apply(_members, _members, null, AnimeGroupKind.MovieSaga, AnimeGroupMovieFolderStyle.Numbered, null);
            _members.Should().OnlyContain(s => s.AnimeGroupKind == AnimeGroupKind.MovieSaga &&
                s.AnimeGroupMovieFolderStyle == AnimeGroupMovieFolderStyle.Numbered && s.AnimeGroupingMode == AnimeGroupingMode.Manual);
            _members.Select(s => s.AnimeGroupMovieNumber).Should().Equal(1, 2);
        }

        [Test]
        public void should_trim_a_native_collection_name_without_fabricating_grouping_or_changing_native_seasons()
        {
            var native = NativeSeries();
            SeriesCollectionEditor.Apply(new[] { native }, new[] { native }, null, null, null, "  Display collection  ");
            native.CollectionTitle.Should().Be("Display collection");
            native.Title.Should().Be("Native TV title");
            native.Path.Should().Be("/generated/Native TV title");
            native.AnimeGroupId.Should().BeNull();
            native.Seasons.Select(s => s.SeasonNumber).Should().Equal(1, 2);
        }

        [Test]
        public void should_clear_an_explicitly_empty_native_collection_name_and_preserve_an_omitted_one()
        {
            var native = NativeSeries();
            native.CollectionTitle = "Saved display name";
            SeriesCollectionEditor.Apply(new[] { native }, Array.Empty<Series>(), null, null, null, null);
            native.CollectionTitle.Should().Be("Saved display name");
            SeriesCollectionEditor.Apply(new[] { native }, new[] { native }, null, null, null, "  ");
            native.CollectionTitle.Should().BeNull();
        }

        [Test]
        public void should_reject_native_name_changes_for_linked_members_before_mutation()
        {
            Action edit = () => SeriesCollectionEditor.Apply(_members, _members, null, null, null, "Native override");
            edit.Should().Throw<ValidationException>();
            _members.Should().OnlyContain(s => s.CollectionTitle == null);
            AssertOriginalMembers();
        }

        private void AssertOriginalMembers()
        {
            _members.Should().OnlyContain(s => s.AnimeGroupTitle == "Original" &&
                s.AnimeGroupingMode == AnimeGroupingMode.Automatic && s.AnimeGroupMovieFolderStyle == AnimeGroupMovieFolderStyle.Automatic);
        }

        private static Series NativeSeries()
        {
            return new Series { Id = 20, Title = "Native TV title", Path = "/generated/Native TV title", Seasons = new List<Season> { new Season { SeasonNumber = 1 }, new Season { SeasonNumber = 2 } } };
        }

        private static Series Member(int id, int season)
        {
            return new Series
            {
                Id = id, Title = "Native entry " + id, ForeignId = id.ToString(), TvdbId = -id,
                MetadataSource = MetadataSourceType.AniList, AnimeGroupingMode = AnimeGroupingMode.Automatic,
                AnimeGroupId = "anilist:100", AnimeGroupTitle = "Original", AnimeGroupFolder = "Original",
                AnimeGroupSeasonNumber = season, Path = "/generated/Original/Season " + season,
                Seasons = new List<Season> { new Season { SeasonNumber = 1 } }
            };
        }
    }
}
