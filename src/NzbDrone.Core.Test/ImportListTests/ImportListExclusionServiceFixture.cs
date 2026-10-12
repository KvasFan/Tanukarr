using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListExclusionServiceFixture : CoreTest<ImportListExclusionService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IImportListExclusionRepository>().Setup(repo => repo.All()).Returns(new List<ImportListExclusion>());
        }

        [Test]
        public void should_preserve_the_provider_identity_when_excluding_a_deleted_series()
        {
            var series = new Series { TvdbId = -7, MetadataSource = MetadataSourceType.AniList, ForeignId = "21", Title = "One Piece" };

            Subject.HandleAsync(new SeriesDeletedEvent(new List<Series> { series }, false, true));

            Mocker.GetMock<IImportListExclusionRepository>().Verify(repo => repo.InsertMany(It.Is<List<ImportListExclusion>>(items => items.Count == 1 && items[0].TvdbId == -7 && items[0].MetadataSource == MetadataSourceType.AniList && items[0].ForeignId == "21")), Times.Once());
        }

        [Test]
        public void should_keep_native_identity_when_an_older_client_edits_the_title()
        {
            Mocker.GetMock<IImportListExclusionRepository>().Setup(repo => repo.Get(5)).Returns(new ImportListExclusion
            {
                Id = 5, TvdbId = -7, MetadataSource = MetadataSourceType.AniList, ForeignId = "21", Title = "One Piece"
            });
            Mocker.GetMock<IImportListExclusionRepository>().Setup(repo => repo.Update(It.IsAny<ImportListExclusion>())).Returns((ImportListExclusion item) => item);

            var updated = Subject.Update(new ImportListExclusion { Id = 5, TvdbId = -7, Title = "Edited title" });

            updated.MetadataSource.Should().Be(MetadataSourceType.AniList);
            updated.ForeignId.Should().Be("21");
            updated.Title.Should().Be("Edited title");
        }

        [Test]
        public void should_exclude_distinct_native_series_that_reused_the_same_synthetic_id()
        {
            Mocker.GetMock<IImportListExclusionRepository>().Setup(repo => repo.All()).Returns(new List<ImportListExclusion>
            {
                new ImportListExclusion { TvdbId = -1, MetadataSource = MetadataSourceType.AniList, ForeignId = "21" }
            });
            var series = new Series { TvdbId = -1, MetadataSource = MetadataSourceType.AniList, ForeignId = "9253", Title = "Steins;Gate" };

            Subject.HandleAsync(new SeriesDeletedEvent(new List<Series> { series }, false, true));

            Mocker.GetMock<IImportListExclusionRepository>().Verify(repo => repo.InsertMany(It.Is<List<ImportListExclusion>>(items => items.Count == 1 && items[0].TvdbId == -2 && items[0].MetadataSource == MetadataSourceType.AniList && items[0].ForeignId == "9253")), Times.Once());
        }

        [Test]
        public void should_not_treat_a_legacy_negative_exclusion_as_a_reused_native_identity()
        {
            Mocker.GetMock<IImportListExclusionRepository>().Setup(repo => repo.All()).Returns(new List<ImportListExclusion>
            {
                new ImportListExclusion { TvdbId = -1 }
            });

            var series = new Series { TvdbId = -1, MetadataSource = MetadataSourceType.AniList, ForeignId = "9253", Title = "Steins;Gate" };

            Subject.HandleAsync(new SeriesDeletedEvent(new List<Series> { series }, false, true));

            Mocker.GetMock<IImportListExclusionRepository>().Verify(repo => repo.InsertMany(It.Is<List<ImportListExclusion>>(items => items.Count == 1 && items[0].TvdbId == -2 && items[0].ForeignId == "9253")), Times.Once());
        }
    }
}
