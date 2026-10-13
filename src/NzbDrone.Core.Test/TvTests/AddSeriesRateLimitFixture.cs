using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class AddSeriesRateLimitFixture : CoreTest<AddSeriesService>
    {
        private Mock<IMetadataProvider> _provider;

        [SetUp]
        public void Setup()
        {
            _provider = new Mock<IMetadataProvider>();
            Mocker.GetMock<IMetadataProviderFactory>()
                  .Setup(factory => factory.GetProvider(It.IsAny<Series>()))
                  .Returns(_provider.Object);
            Mocker.GetMock<ISeriesService>()
                  .Setup(service => service.AllSeriesTvdbIds())
                  .Returns(new Dictionary<int, int>());
            Mocker.GetMock<ISeriesService>()
                  .Setup(service => service.GetAllSeries())
                  .Returns(new List<Series>());
            Mocker.GetMock<ISeriesService>()
                  .Setup(service => service.AddSeries(It.IsAny<List<Series>>()))
                  .Returns((List<Series> values) => values);
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(builder => builder.GetSeriesFolder(It.IsAny<Series>(), null))
                  .Returns((Series series, NamingConfig _) => series.Title);
            Mocker.GetMock<IAddSeriesValidator>()
                  .Setup(validator => validator.Validate(It.IsAny<Series>()))
                  .Returns(new ValidationResult());
            _provider.Setup(provider => provider.GetSeriesInfo(It.IsAny<Series>()))
                     .Returns((Series request) => Tuple.Create(new Series
                     {
                         MetadataSource = request.MetadataSource,
                         ForeignId = request.ForeignId,
                         TvdbId = request.TvdbId,
                         Title = "Metadata entry " + request.ForeignId,
                         TitleSlug = "metadata-entry-" + request.ForeignId,
                         Seasons = new List<Season> { new Season { SeasonNumber = 1 } }
                     },
                     new List<Episode>()));
        }

        private static Series Candidate(int id, MetadataSourceType source = MetadataSourceType.Tvdb)
        {
            return new Series
            {
                MetadataSource = source,
                ForeignId = id.ToString(),
                TvdbId = id,
                RootFolderPath = @"C:\Test\TV",
                Language = Language.English
            };
        }

        private static HttpException ProviderError(HttpStatusCode status)
        {
            var request = new HttpRequest("https://graphql.anilist.co");
            var response = new HttpResponse(request, new HttpHeader { { "Retry-After", "60" } }, "Provider unavailable", status);
            return status == HttpStatusCode.TooManyRequests
                ? new TooManyRequestsException(request, response)
                : new HttpException(request, response);
        }

        [Test]
        public void should_commit_prepared_series_and_continue_other_providers_after_throttling()
        {
            _provider.Setup(provider => provider.GetSeriesInfo(It.Is<Series>(series => series.ForeignId == "21")))
                     .Throws(ProviderError(HttpStatusCode.TooManyRequests));
            var result = Subject.AddSeries(new List<Series>
            {
                Candidate(1), Candidate(21, MetadataSourceType.AniList),
                Candidate(22, MetadataSourceType.AniList), Candidate(2)
            },
            ignoreErrors: true);

            result.Select(series => series.ForeignId).Should().Equal("1", "2");
            _provider.Verify(provider => provider.GetSeriesInfo(It.Is<Series>(series => series.ForeignId == "22")), Times.Never());
            Mocker.GetMock<ISeriesService>().Verify(service => service.AddSeries(It.Is<List<Series>>(series => series.Count == 2)), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_retry_a_throttled_provider_in_a_later_batch()
        {
            _provider.Setup(provider => provider.GetSeriesInfo(It.Is<Series>(series => series.ForeignId == "21")))
                     .Throws(ProviderError(HttpStatusCode.TooManyRequests));
            Subject.AddSeries(new List<Series> { Candidate(21, MetadataSourceType.AniList) }, ignoreErrors: true).Should().BeEmpty();
            _provider.Setup(provider => provider.GetSeriesInfo(It.Is<Series>(series => series.ForeignId == "21")))
                     .Returns(Tuple.Create(new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "21", TvdbId = 21, Title = "Recovered entry", TitleSlug = "recovered-entry" }, new List<Episode>()));

            Subject.AddSeries(new List<Series> { Candidate(21, MetadataSourceType.AniList) }, ignoreErrors: true)
                   .Should().ContainSingle(series => series.ForeignId == "21");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_keep_successes_when_one_provider_entry_returns_http_error()
        {
            _provider.Setup(provider => provider.GetSeriesInfo(It.Is<Series>(series => series.ForeignId == "2")))
                     .Throws(ProviderError(HttpStatusCode.ServiceUnavailable));

            Subject.AddSeries(new List<Series> { Candidate(1), Candidate(2), Candidate(3) }, ignoreErrors: true)
                   .Select(series => series.ForeignId).Should().Equal("1", "3");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_propagate_throttling_when_errors_are_not_ignored()
        {
            _provider.Setup(provider => provider.GetSeriesInfo(It.IsAny<Series>()))
                     .Throws(ProviderError(HttpStatusCode.TooManyRequests));

            Assert.Throws<TooManyRequestsException>(() => Subject.AddSeries(new List<Series> { Candidate(1) }));
            Mocker.GetMock<ISeriesService>().Verify(service => service.AddSeries(It.IsAny<List<Series>>()), Times.Never());
        }

        [Test]
        public void should_not_hide_unexpected_programming_errors()
        {
            _provider.Setup(provider => provider.GetSeriesInfo(It.IsAny<Series>()))
                     .Throws(new InvalidOperationException("Unexpected mapping failure"));

            Assert.Throws<InvalidOperationException>(() => Subject.AddSeries(new List<Series> { Candidate(1) }, ignoreErrors: true));
            Mocker.GetMock<ISeriesService>().Verify(service => service.AddSeries(It.IsAny<List<Series>>()), Times.Never());
        }
    }
}
