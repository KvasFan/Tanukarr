using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.AniList;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    [TestFixture]
    public class AniListProxyFixture : CoreTest<AniListProxy>
    {
        private static string Media(int id, params int[] episodeNumbers)
        {
            return Json.ToJson(new
            {
                data = new
                {
                    Media = new
                    {
                        id,
                        title = new { english = "Test anime" },
                        status = "FINISHED",
                        episodes = 52,
                        airingSchedule = new
                        {
                            nodes = episodeNumbers.Select(episode => new { episode, airingAt = 1600000000L + episode }).ToList()
                        }
                    }
                }
            });
        }

        private void GivenResponses(params string[] responses)
        {
            var pending = new Queue<string>(responses);
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), pending.Dequeue()));
        }

        private void GivenHttpError(HttpStatusCode status)
        {
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), "not-json", status));
        }

        [Test]
        public void should_fetch_and_deserialize_metadata_through_the_shared_request_service()
        {
            GivenResponses(Media(253, 1, 2));

            var result = Subject.GetSeriesInfo(253);

            result.Item1.MetadataSource.Should().Be(MetadataSourceType.AniList);
            result.Item1.ForeignId.Should().Be("253");
            result.Item1.Title.Should().Be("Test anime");
            result.Item2.Should().HaveCount(52);
            Mocker.GetMock<IAniListRequestService>()
                .Verify(service => service.Execute(It.Is<HttpRequest>(request =>
                    request.Url.ToString() == AniListQueries.BaseUrl &&
                    request.Method == HttpMethod.Post &&
                    request.Headers.ContentType == "application/json" &&
                    request.SuppressHttpError)),
                    Times.Once());
            Mocker.GetMock<IHttpClient>()
                .Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_route_every_airing_schedule_page_through_the_shared_request_service()
        {
            var requests = new List<HttpRequest>();
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(requests.Add)
                .Returns((HttpRequest request) => new HttpResponse(
                    request,
                    new HttpHeader(),
                    requests.Count == 1 ? Media(253, Enumerable.Range(1, 50).ToArray()) : Media(253, 51, 52)));

            var result = Subject.GetSeriesInfo(253);

            requests.Should().HaveCount(2);
            requests.Select(request => (int)JObject.Parse(Encoding.UTF8.GetString(request.ContentData))["variables"]["page"])
                .Should().Equal(1, 2);
            result.Item2.Where(episode => episode.AirDateUtc.HasValue).Should().HaveCount(52);
            result.Item2.Last().AbsoluteEpisodeNumber.Should().Be(52);
        }

        [Test]
        public void should_route_title_search_through_the_shared_request_service()
        {
            GivenResponses("{\"data\":{\"Page\":{\"media\":[{\"id\":253,\"title\":{\"english\":\"Test anime\"}}]}}}");

            Subject.SearchForNewSeries("Test anime").Should().ContainSingle().Which.ForeignId.Should().Be("253");

            Mocker.GetMock<IAniListRequestService>()
                .Verify(service => service.Execute(It.Is<HttpRequest>(request =>
                    Encoding.UTF8.GetString(request.ContentData).Contains("Test anime"))),
                    Times.Once());
        }

        [Test]
        public void should_keep_distinct_metadata_in_a_65_entry_import_batch()
        {
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(
                    request,
                    new HttpHeader(),
                    Media((int)JObject.Parse(Encoding.UTF8.GetString(request.ContentData))["variables"]["id"])));

            var results = Enumerable.Range(1, 65).Select(id => Subject.GetSeriesInfo(id).Item1).ToList();

            results.Select(series => series.ForeignId).Should().Equal(Enumerable.Range(1, 65).Select(id => id.ToString()));
            Mocker.GetMock<IAniListRequestService>()
                .Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Exactly(65));
        }

        [Test]
        public void should_preserve_not_found_semantics_without_deserializing_error_content()
        {
            GivenHttpError(HttpStatusCode.NotFound);

            Action act = () => Subject.GetSeriesInfo(253);

            act.Should().Throw<SeriesNotFoundException>();
            Subject.SearchForNewSeriesByAniListId(253).Should().BeEmpty();
        }

        [Test]
        public void should_propagate_exhausted_rate_limit_retries_from_the_shared_service()
        {
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => throw new TooManyRequestsException(request,
                    new HttpResponse(request, new HttpHeader { { "Retry-After", "60" } }, "{}", HttpStatusCode.TooManyRequests)));

            Action act = () => Subject.GetSeriesInfo(253);

            act.Should().Throw<TooManyRequestsException>().Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
            Mocker.GetMock<IAniListRequestService>()
                .Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void should_return_no_search_results_when_shared_rate_limit_retries_are_exhausted()
        {
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => throw new TooManyRequestsException(request,
                    new HttpResponse(request, new HttpHeader(), "{}", HttpStatusCode.TooManyRequests)));

            Subject.SearchForNewSeries("Test anime").Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_propagate_other_http_errors_without_deserializing_error_content()
        {
            GivenHttpError(HttpStatusCode.ServiceUnavailable);

            Action act = () => Subject.GetSeriesInfo(253);

            act.Should().Throw<HttpException>().Which.Response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
    }
}
