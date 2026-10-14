using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.AniList;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.Jikan;
using NzbDrone.Core.MetadataSource.Jikan.Resource;
using NzbDrone.Core.MetadataSource.Tmdb;
using NzbDrone.Core.MetadataSource.Tmdb.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class SeriesPresentationProviderFixture : CoreTest
    {
        private void GivenAniList(string json)
        {
            Mocker.GetMock<IAniListRequestService>().Setup(service => service.ExecuteOptional(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), json));
        }

        private void GivenJikan(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IJikanRateLimiter>().Setup(limiter => limiter.TryWaitForSlot(It.IsAny<TimeSpan>())).Returns(true);
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JikanAnimeResponse>(new HttpResponse(request, new HttpHeader { ["Retry-After"] = "90" }, json, status)));
        }

        [Test]
        public void should_lookup_anilist_presentation_by_a_verified_mal_id_without_search_or_episode_schedule_requests()
        {
            GivenAniList("{\"data\":{\"Media\":{\"id\":100,\"idMal\":200,\"type\":\"ANIME\",\"description\":\"<b>Public description</b>\",\"coverImage\":{\"extraLarge\":\"\",\"large\":\"https://s4.anilist.co/cover.jpg\"},\"bannerImage\":\"https://s4.anilist.co/banner.jpg\"}}}");
            var result = Mocker.Resolve<AniListProxy>().GetPresentation(new Series { MalIds = new HashSet<int> { 200 } });
            result.Overview.Should().Be("Public description");
            result.Images.Select(image => image.RemoteUrl).Should().Equal("https://s4.anilist.co/cover.jpg", "https://s4.anilist.co/banner.jpg");
            Mocker.GetMock<IAniListRequestService>().Verify(service => service.ExecuteOptional(It.Is<HttpRequest>(request =>
                JObject.Parse(Encoding.UTF8.GetString(request.ContentData))["variables"]["idMal"].Value<int>() == 200 &&
                !JObject.Parse(Encoding.UTF8.GetString(request.ContentData))["query"].Value<string>().Contains("airingSchedule") &&
                request.RequestTimeout == TimeSpan.FromSeconds(5))),
                Times.Once());
            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase(201, "ANIME")]
        [TestCase(200, "MANGA")]
        public void should_reject_an_anilist_mapping_that_does_not_confirm_the_requested_mal_identity(int malId, string type)
        {
            GivenAniList("{\"data\":{\"Media\":{\"id\":100,\"idMal\":" + malId + ",\"type\":\"" + type + "\",\"description\":\"Wrong entry\"}}}");
            Mocker.Resolve<AniListProxy>().GetPresentation(new Series { MalIds = new HashSet<int> { 200 } }).Should().BeNull();
        }

        [Test]
        public void should_reject_partial_graphql_presentation_even_when_http_is_successful()
        {
            GivenAniList("{\"errors\":[{\"message\":\"partial\"}],\"data\":{\"Media\":{\"id\":100,\"type\":\"ANIME\",\"description\":\"Partial description\"}}}");
            Mocker.Resolve<AniListProxy>().GetPresentation(new Series { AniListIds = new HashSet<int> { 100 } }).Should().BeNull();
        }

        [Test]
        public void should_reject_a_mismatched_native_anilist_identity()
        {
            GivenAniList("{\"data\":{\"Media\":{\"id\":101,\"type\":\"ANIME\",\"description\":\"Wrong anime\"}}}");
            Mocker.Resolve<AniListProxy>().GetPresentation(new Series { AniListIds = new HashSet<int> { 100 } }).Should().BeNull();
        }

        [Test]
        public void should_deserialize_jikan_snake_case_artwork_and_use_webp_when_jpg_is_missing_without_reading_episodes()
        {
            GivenJikan("{\"data\":{\"mal_id\":200,\"synopsis\":\"Jikan description\",\"images\":{\"jpg\":{\"large_image_url\":\"\",\"image_url\":null},\"webp\":{\"large_image_url\":\"https://cdn.myanimelist.net/poster.webp\"}}}}");
            var result = Mocker.Resolve<JikanProxy>().GetPresentation(new Series { MalIds = new HashSet<int> { 200 } });
            result.Overview.Should().Be("Jikan description");
            result.Images.Single().RemoteUrl.Should().Be("https://cdn.myanimelist.net/poster.webp");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<JikanAnimeResponse>(It.Is<HttpRequest>(request =>
                request.Url.FullUri == "https://api.jikan.moe/v4/anime/200/full" && request.RequestTimeout == TimeSpan.FromSeconds(5))),
                Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<JikanEpisodeListResponse>(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IJikanRateLimiter>().Verify(limiter => limiter.TryWaitForSlot(TimeSpan.FromSeconds(5)), Times.Once());
        }

        [Test]
        public void should_skip_jikan_optional_lookup_without_network_when_shared_quota_is_not_available()
        {
            Mocker.GetMock<IJikanRateLimiter>().Setup(limiter => limiter.TryWaitForSlot(It.IsAny<TimeSpan>())).Returns(false);
            Action lookup = () => Mocker.Resolve<JikanProxy>().GetPresentation(new Series { MalIds = new HashSet<int> { 200 } });
            lookup.Should().Throw<Exception>();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_back_off_shared_jikan_quota_after_one_optional_429_without_retrying()
        {
            GivenJikan("{}", HttpStatusCode.TooManyRequests);
            Action lookup = () => Mocker.Resolve<JikanProxy>().GetPresentation(new Series { MalIds = new HashSet<int> { 200 } });
            lookup.Should().Throw<HttpException>();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()), Times.Once());
            Mocker.GetMock<IJikanRateLimiter>().Verify(limiter => limiter.BackOff(TimeSpan.FromSeconds(90)), Times.Once());
        }

        [Test]
        public void should_reject_mismatched_jikan_artwork()
        {
            GivenJikan("{\"data\":{\"mal_id\":201,\"synopsis\":\"Wrong description\"}}");
            Mocker.Resolve<JikanProxy>().GetPresentation(new Series { MalIds = new HashSet<int> { 200 } }).Should().BeNull();
        }

        [TestCase(20, true)]
        [TestCase(21, false)]
        public void should_read_tmdb_poster_backdrop_and_overview_only_for_the_exact_known_tv_id(int returnedId, bool matches)
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.TmdbApiKey).Returns("fixture-key");
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<TmdbSeriesResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<TmdbSeriesResource>(new HttpResponse(request, new HttpHeader(), "{\"id\":" + returnedId + ",\"overview\":\"TV description\",\"poster_path\":\"/poster.jpg\",\"backdrop_path\":\"/backdrop.jpg\"}")));
            var result = Mocker.Resolve<TmdbProxy>().GetPresentation(new Series { TmdbId = 20 });
            if (matches)
            {
                result.Overview.Should().Be("TV description");
                result.Images.Select(image => image.RemoteUrl).Should().Equal("https://image.tmdb.org/t/p/original/poster.jpg", "https://image.tmdb.org/t/p/original/backdrop.jpg");
            }
            else
            {
                result.Should().BeNull();
            }

            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<TmdbSeriesResource>(It.Is<HttpRequest>(request =>
                request.Url.FullUri.StartsWith("https://api.themoviedb.org/3/tv/20?", StringComparison.Ordinal) &&
                request.RequestTimeout == TimeSpan.FromSeconds(5))),
                Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<TmdbSeasonDetailResource>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_make_no_presentation_request_without_a_positive_verified_identifier()
        {
            var identity = new Series { TvdbId = -37, TmdbId = -1, MalIds = new HashSet<int> { 0, -1 }, AniListIds = new HashSet<int> { 0, -1 } };
            Mocker.Resolve<AniListProxy>().GetPresentation(identity).Should().BeNull();
            Mocker.Resolve<JikanProxy>().GetPresentation(identity).Should().BeNull();
            Mocker.Resolve<TmdbProxy>().GetPresentation(identity).Should().BeNull();
            Mocker.GetMock<IAniListRequestService>().Verify(service => service.ExecuteOptional(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<TmdbSeriesResource>(It.IsAny<HttpRequest>()), Times.Never());
        }
    }
}
