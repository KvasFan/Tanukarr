using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using Sonarr.Http.REST;

namespace NzbDrone.Api.Test.SeriesTests
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class AnimeGroupingPreviewFixture : TestBase
    {
        private readonly bool _useV3;

        public AnimeGroupingPreviewFixture(bool useV3)
        {
            _useV3 = useV3;
        }

        [Test]
        public void should_preview_only_the_requested_catalogue_identity_without_adding_a_series()
        {
            var expected = new AnimeGroupingResult { Status = AnimeGroupingStatus.Resolved, AnimeGroupId = "anilist:1" };
            Mocker.GetMock<IAnimeGroupingService>()
                .Setup(s => s.Resolve(It.Is<NzbDrone.Core.Tv.Series>(series =>
                    series.MetadataSource == MetadataSourceType.AniList && series.ForeignId == "2" && series.Id == 0)))
                .Returns(expected);
            Preview(MetadataSourceType.AniList, "0002").Should().BeSameAs(expected);
            Mocker.GetMock<IAddSeriesService>().Verify(s => s.AddSeries(It.IsAny<NzbDrone.Core.Tv.Series>()), Times.Never());
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("1/../../other")]
        [TestCase("2147483648")]
        public void should_reject_invalid_catalogue_ids_before_lookup(string foreignId)
        {
            Action preview = () => Preview(MetadataSourceType.AniList, foreignId);
            preview.Should().Throw<BadRequestException>();
            Mocker.GetMock<IAnimeGroupingService>().Verify(s => s.Resolve(It.IsAny<NzbDrone.Core.Tv.Series>()), Times.Never());
        }

        [Test]
        public void should_reject_unknown_sources_before_lookup()
        {
            Action preview = () => Preview((MetadataSourceType)999, "1");
            preview.Should().Throw<BadRequestException>();
            Mocker.GetMock<IAnimeGroupingService>().Verify(s => s.Resolve(It.IsAny<NzbDrone.Core.Tv.Series>()), Times.Never());
        }

        private AnimeGroupingResult Preview(MetadataSourceType source, string id)
        {
            return _useV3
                ? Mocker.Resolve<Sonarr.Api.V3.Series.AnimeGroupingController>().Preview(source, id).Value
                : Mocker.Resolve<Sonarr.Api.V5.Series.AnimeGroupingController>().Preview(source, id).Value;
        }
    }
}
