using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
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

        [TestCase(ImportListMetadataSource.Automatic, MetadataSourceType.Tvdb, "123")]
        [TestCase(ImportListMetadataSource.Tvdb, MetadataSourceType.Tvdb, "123")]
        [TestCase(ImportListMetadataSource.Tmdb, MetadataSourceType.Tmdb, "99")]
        [TestCase(ImportListMetadataSource.AniList, MetadataSourceType.AniList, "21")]
        [TestCase(ImportListMetadataSource.MyAnimeList, MetadataSourceType.MyAnimeList, "42")]
        public void should_select_the_requested_catalog_only_from_its_supplied_positive_id(ImportListMetadataSource preference, MetadataSourceType expectedSource, string expectedId)
        {
            var item = new ImportListItemInfo { TvdbId = 123, TmdbId = 99, AniListId = 21, MalId = 42 };

            var series = ImportListIdentity.CreateSeries(item, Mocker.GetMock<ISearchForNewSeries>().Object, Language.English, preference);

            series.MetadataSource.Should().Be(expectedSource);
            series.ForeignId.Should().Be(expectedId);
            series.TvdbId.Should().Be(123);
            Mocker.GetMock<ISearchForNewSeries>().VerifyNoOtherCalls();
        }

        [TestCase(ImportListMetadataSource.Tvdb)]
        [TestCase(ImportListMetadataSource.Tmdb)]
        [TestCase(ImportListMetadataSource.MyAnimeList)]
        public void should_fall_back_to_native_anilist_without_reusing_its_id_in_another_namespace(ImportListMetadataSource preference)
        {
            var item = new ImportListItemInfo { TvdbId = -21, TmdbId = -21, AniListId = 21, MalId = -21 };

            var series = ImportListIdentity.CreateSeries(item, Mocker.GetMock<ISearchForNewSeries>().Object, Language.English, preference);

            series.MetadataSource.Should().Be(MetadataSourceType.AniList);
            series.ForeignId.Should().Be("21");
            series.TvdbId.Should().Be(0);
            series.TmdbId.Should().Be(0);
            series.MalIds.Should().BeEmpty();
            Mocker.GetMock<ISearchForNewSeries>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_use_anilist_verified_mal_mapping_when_myanimelist_is_preferred()
        {
            var series = ImportListIdentity.CreateSeries(new ImportListItemInfo { AniListId = 21, MalId = 42 },
                Mocker.GetMock<ISearchForNewSeries>().Object,
                Language.English,
                ImportListMetadataSource.MyAnimeList);

            series.MetadataSource.Should().Be(MetadataSourceType.MyAnimeList);
            series.ForeignId.Should().Be("42");
            series.TvdbId.Should().Be(0);
            series.AniListIds.Should().Contain(21);
            series.MalIds.Should().Contain(42);
        }

        [Test]
        public void should_keep_the_existing_imdb_fallback_when_no_selected_catalog_id_is_available()
        {
            var resolved = new Series { MetadataSource = MetadataSourceType.Tvdb, TvdbId = 123, ForeignId = "123" };
            Mocker.GetMock<ISearchForNewSeries>().Setup(service => service.SearchForNewSeriesByImdbId("tt1234567", Language.English))
                .Returns(new List<Series> { resolved });

            var series = ImportListIdentity.CreateSeries(new ImportListItemInfo { ImdbId = "tt1234567" },
                Mocker.GetMock<ISearchForNewSeries>().Object,
                Language.English,
                ImportListMetadataSource.AniList);

            series.Should().BeSameAs(resolved);
            Mocker.GetMock<ISearchForNewSeries>().Verify(service => service.SearchForNewSeriesByImdbId("tt1234567", Language.English), Times.Once());
        }

        [TestCase(-1)]
        [TestCase(99)]
        public void should_reject_an_unknown_preference_without_changing_catalogs(int preference)
        {
            Action create = () => ImportListIdentity.CreateSeries(new ImportListItemInfo { TvdbId = 123, AniListId = 21 },
                Mocker.GetMock<ISearchForNewSeries>().Object,
                Language.English,
                (ImportListMetadataSource)preference);

            create.Should().Throw<InvalidOperationException>();
            Mocker.GetMock<ISearchForNewSeries>().VerifyNoOtherCalls();
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

        [Test]
        public void should_honor_native_exclusions_from_the_supplied_item_when_another_provider_is_preferred()
        {
            var item = new ImportListItemInfo { AniListId = 21, MalId = 42 };

            ImportListIdentity.IsExcluded(new ImportListExclusion { MetadataSource = MetadataSourceType.AniList, ForeignId = "21" }, item).Should().BeTrue();
            ImportListIdentity.IsExcluded(new ImportListExclusion { MetadataSource = MetadataSourceType.MyAnimeList, ForeignId = "42" }, item).Should().BeTrue();
            ImportListIdentity.IsExcluded(new ImportListExclusion { MetadataSource = MetadataSourceType.Tmdb, ForeignId = "21" }, item).Should().BeFalse();
            ImportListIdentity.IsExcluded(new ImportListExclusion { TvdbId = 21 }, item).Should().BeFalse();
        }

        [TestCase("-21")]
        [TestCase("invalid")]
        public void should_not_treat_an_invalid_native_exclusion_as_a_supplied_positive_id(string id)
        {
            ImportListIdentity.IsExcluded(new ImportListExclusion { MetadataSource = MetadataSourceType.AniList, ForeignId = id },
                new ImportListItemInfo { AniListId = 21 }).Should().BeFalse();
        }
    }
}
