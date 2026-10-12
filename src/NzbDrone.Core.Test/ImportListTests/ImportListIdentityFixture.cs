using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListIdentityFixture : CoreTest
    {
        [Test]
        public void should_not_match_ids_from_different_catalogs()
        {
            var series = new Series { TvdbId = 21, MetadataSource = MetadataSourceType.Tvdb, ForeignId = "21" };

            ImportListIdentity.Matches(series, new ImportListItemInfo { AniListId = 21 }).Should().BeFalse();
            ImportListIdentity.Matches(series, new ImportListItemInfo { MalId = 21 }).Should().BeFalse();
            ImportListIdentity.Matches(series, new ImportListItemInfo { TmdbId = 21 }).Should().BeFalse();
        }

        [Test]
        public void should_not_match_empty_ids_or_titles()
        {
            var series = new Series { Title = "Same title" };

            ImportListIdentity.Matches(series, new ImportListItemInfo { Title = "Same title" }).Should().BeFalse();
        }

        [Test]
        public void should_use_the_native_anilist_id_before_secondary_ids()
        {
            var series = new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "21", TmdbId = 99 };
            series.MalIds.Add(42);

            ImportListIdentity.Matches(series, new ImportListItemInfo { AniListId = 22, MalId = 42, TmdbId = 99 }).Should().BeFalse();
            ImportListIdentity.Matches(series, new ImportListItemInfo { AniListId = 21 }).Should().BeTrue();
        }

        [Test]
        public void should_create_a_native_anilist_series_without_a_tvdb_lookup()
        {
            var item = new ImportListItemInfo { AniListId = 21, MalId = 42, TmdbId = 99 };
            var series = ImportListIdentity.CreateSeries(item, Mocker.GetMock<ISearchForNewSeries>().Object, Language.Italian);

            series.MetadataSource.Should().Be(MetadataSourceType.AniList);
            series.ForeignId.Should().Be("21");
            series.AniListIds.Should().Contain(21);
            series.MalIds.Should().Contain(42);
            series.TvdbId.Should().Be(0);
            series.SeriesType.Should().Be(SeriesTypes.Anime);
            series.Language.Should().Be(Language.Italian);
        }

        [Test]
        public void should_preserve_an_explicit_tvdb_identity()
        {
            var series = ImportListIdentity.CreateSeries(new ImportListItemInfo { TvdbId = 123, AniListId = 21 }, Mocker.GetMock<ISearchForNewSeries>().Object, Language.English);

            series.MetadataSource.Should().Be(MetadataSourceType.Tvdb);
            series.ForeignId.Should().Be("123");
            series.TvdbId.Should().Be(123);
        }

        [Test]
        public void should_exclude_the_native_identity_after_its_synthetic_id_changes()
        {
            var exclusion = new ImportListExclusion { TvdbId = -7, MetadataSource = MetadataSourceType.AniList, ForeignId = "21" };

            ImportListIdentity.IsExcluded(exclusion, new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "21" }).Should().BeTrue();
            ImportListIdentity.IsExcluded(exclusion, new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "22" }).Should().BeFalse();
            ImportListIdentity.IsExcluded(exclusion, new Series { MetadataSource = MetadataSourceType.MyAnimeList, ForeignId = "21" }).Should().BeFalse();
        }

        [Test]
        public void should_preserve_legacy_tvdb_exclusions_without_matching_zero_ids()
        {
            ImportListIdentity.IsExcluded(new ImportListExclusion { TvdbId = 123 }, new Series { TvdbId = 123 }).Should().BeTrue();
            ImportListIdentity.IsExcluded(new ImportListExclusion(), new Series()).Should().BeFalse();
            ImportListIdentity.IsExcluded(new ImportListExclusion { TvdbId = -1 }, new Series { TvdbId = -1, MetadataSource = MetadataSourceType.AniList, ForeignId = "9253" }).Should().BeFalse();
        }
    }
}
