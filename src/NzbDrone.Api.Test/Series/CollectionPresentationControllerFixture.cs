using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using CoreSeries = NzbDrone.Core.Tv.Series;

namespace NzbDrone.Api.Test.SeriesTests
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class CollectionPresentationControllerFixture : TestBase
    {
        private readonly bool _useV3;
        private List<CoreSeries> _series;

        public CollectionPresentationControllerFixture(bool useV3)
        {
            _useV3 = useV3;
        }

        [SetUp]
        public void Setup()
        {
            _series = new List<CoreSeries>
            {
                new CoreSeries { Id = 42, AnimeGroupId = "anilist:100", AnimeGroupTitle = "Collection", AnimeGroupSeasonNumber = 2 }
            };
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetAllSeries()).Returns(() => _series);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("../anilist:100")]
        [TestCase("anilist:100/child")]
        [TestCase("anilist:100\n")]
        public void should_reject_malformed_groups_before_reading_the_library_or_contacting_a_provider(string groupId)
        {
            var result = Get(groupId);
            if (_useV3)
            {
                result.Should().BeOfType<BadRequestResult>();
            }
            else
            {
                result.Should().BeOfType<BadRequest>();
            }

            Mocker.GetMock<ISeriesService>().Verify(service => service.GetAllSeries(), Times.Never());
            VerifyNoProviderOrMapper();
        }

        [Test]
        public void should_return_not_found_for_an_unsaved_group_without_contacting_a_provider()
        {
            AssertNotFound(Get("anilist:999"));
            VerifyNoProviderOrMapper();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_not_treat_disabled_or_incomplete_members_as_a_saved_collection(bool disabled)
        {
            if (disabled)
            {
                _series[0].AnimeGroupingMode = AnimeGroupingMode.Disabled;
            }
            else
            {
                _series[0].AnimeGroupSeasonNumber = null;
            }

            AssertNotFound(Get("anilist:100"));
            VerifyNoProviderOrMapper();
        }

        [Test]
        public void should_fetch_the_saved_canonical_group_and_map_covers_to_the_authenticated_local_proxy()
        {
            var presentation = new SeriesPresentation
            {
                Overview = "Canonical collection overview",
                Images = new List<MediaCover>
                {
                    new MediaCover(MediaCoverTypes.Poster, "https://s4.anilist.co/generated-poster.jpg")
                }
            };
            Mocker.GetMock<ISeriesPresentationService>().Setup(service => service.GetCollectionPresentation("anilist:100")).Returns(presentation);
            Mocker.GetMock<IMapCoversToLocal>().Setup(mapper => mapper.ConvertToLocalUrls(0, presentation.Images, null))
                .Callback<int, IEnumerable<MediaCover>, DateTime?>((id, images, added) =>
                {
                    foreach (var image in images)
                    {
                        image.Url = "/generated-base/MediaCoverProxy/generated-cache.jpg";
                    }
                });
            var actual = SuccessfulValue(Get("anilist:100"));
            actual.Should().BeSameAs(presentation);
            actual.Images.Should().ContainSingle(image => image.Url == "/generated-base/MediaCoverProxy/generated-cache.jpg");
            actual.Images[0].RemoteUrl.Should().Be("https://s4.anilist.co/generated-poster.jpg");
            Mocker.GetMock<ISeriesPresentationService>().Verify(service => service.GetCollectionPresentation("anilist:100"), Times.Once());
            Mocker.GetMock<IMapCoversToLocal>().Verify(mapper => mapper.ConvertToLocalUrls(0, presentation.Images, null), Times.Once());
            _series[0].Overview.Should().BeNull();
            _series[0].Images.Should().BeEmpty();
        }

        [Test]
        public void should_return_an_empty_presentation_when_optional_root_metadata_is_unavailable()
        {
            var actual = SuccessfulValue(Get("anilist:100"));
            actual.Images.Should().BeEmpty();
            actual.Overview.Should().BeNull();
            Mocker.GetMock<IMapCoversToLocal>().Verify(mapper => mapper.ConvertToLocalUrls(0, It.IsAny<IEnumerable<MediaCover>>(), null), Times.Once());
        }

        private object Get(string groupId)
        {
            return _useV3
                ? Mocker.Resolve<Sonarr.Api.V3.Series.CollectionPresentationController>().Get(groupId).Result
                : Mocker.Resolve<Sonarr.Api.V5.Series.CollectionPresentationController>().Get(groupId).Result;
        }

        private SeriesPresentation SuccessfulValue(object result)
        {
            return _useV3
                ? (SeriesPresentation)result.Should().BeOfType<OkObjectResult>().Subject.Value
                : result.Should().BeOfType<Ok<SeriesPresentation>>().Subject.Value;
        }

        private void AssertNotFound(object result)
        {
            if (_useV3)
            {
                result.Should().BeOfType<NotFoundResult>();
            }
            else
            {
                result.Should().BeOfType<NotFound>();
            }
        }

        private void VerifyNoProviderOrMapper()
        {
            Mocker.GetMock<ISeriesPresentationService>().Verify(service => service.GetCollectionPresentation(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMapCoversToLocal>().Verify(mapper => mapper.ConvertToLocalUrls(It.IsAny<int>(), It.IsAny<IEnumerable<MediaCover>>(), It.IsAny<DateTime?>()), Times.Never());
        }
    }
}
