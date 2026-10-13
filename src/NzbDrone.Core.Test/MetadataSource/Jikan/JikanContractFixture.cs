using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Jikan;
using NzbDrone.Core.MetadataSource.Jikan.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Jikan
{
    [TestFixture]
    public class JikanContractFixture : CoreTest<JikanProxy>
    {
        private void GivenCatalogue(string json)
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JikanAnimeResponse>(new HttpResponse(request, new HttpHeader(), json)));
        }

        private void GivenEpisodes(params string[] pages)
        {
            var queue = new Queue<string>(pages);
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JikanEpisodeListResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JikanEpisodeListResponse>(new HttpResponse(request, new HttpHeader(), queue.Dequeue())));
        }

        [TestCase("TV", AnimeGroupMemberKind.Season)]
        [TestCase("Movie", AnimeGroupMemberKind.Movie)]
        public void should_deserialize_public_catalogue_json_without_losing_native_ids_titles_posters_or_relations(string type, AnimeGroupMemberKind kind)
        {
            GivenCatalogue("{\"data\":{\"mal_id\":21,\"title\":\"Original title\",\"title_english\":\"English title\",\"type\":\"" + type + "\",\"episodes\":1,\"status\":\"Finished Airing\",\"duration\":\"24 min\",\"scored_by\":12,\"score\":8,\"images\":{\"jpg\":{\"large_image_url\":\"https://fixture.invalid/poster.jpg\"}},\"relations\":[{\"relation\":\"Sequel\",\"entry\":[{\"mal_id\":22,\"type\":\"anime\"},{\"mal_id\":99,\"type\":\"manga\"}]}]}}");
            GivenEpisodes("{\"data\":[{\"mal_id\":1,\"title\":\"Episode title\",\"aired\":\"2020-01-02T00:00:00Z\"}],\"pagination\":{\"has_next_page\":false}}");
            var result = Subject.GetSeriesInfo(21);
            result.Item1.MetadataSource.Should().Be(MetadataSourceType.MyAnimeList);
            result.Item1.ForeignId.Should().Be("21");
            result.Item1.MalIds.Should().Equal(21);
            result.Item1.AniListIds.Should().BeEmpty();
            result.Item1.TvdbId.Should().Be(0);
            result.Item1.Title.Should().Be("English title");
            result.Item1.AnimeGroupMemberKind.Should().Be(kind);
            result.Item1.Images.Single().RemoteUrl.Should().Be("https://fixture.invalid/poster.jpg");
            result.Item1.Ratings.Votes.Should().Be(12);
            result.Item2.Single().AirDate.Should().Be("2020-01-02");
            Subject.GetAnimeRelations("21").SequelIds.Should().Equal("22");
        }

        [Test]
        public void should_fill_missing_native_episode_numbers_without_duplicating_or_renumbering_real_titles_across_pages()
        {
            GivenCatalogue("{\"data\":{\"mal_id\":21,\"title\":\"TV title\",\"type\":\"TV\",\"episodes\":3}}");
            GivenEpisodes("{\"data\":[{\"mal_id\":1,\"title\":\"First\"},{\"mal_id\":3,\"title\":\"Third\"}],\"pagination\":{\"has_next_page\":true}}",
                "{\"data\":[{\"mal_id\":3,\"title\":\"Repeated third\"}],\"pagination\":{\"has_next_page\":false}}");
            var episodes = Subject.GetSeriesInfo(21).Item2;
            episodes.Select(episode => episode.EpisodeNumber).Should().Equal(1, 2, 3);
            episodes[1].ForeignId.Should().Be("21:2");
            episodes[2].Title.Should().Be("Third");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<JikanEpisodeListResponse>(It.Is<HttpRequest>(request => request.Url.FullUri.EndsWith("page=2"))), Times.Once());
        }

        [TestCase(-1)]
        [TestCase(0)]
        public void should_reject_nonpositive_lookup_ids_without_contacting_jikan(int id)
        {
            Action act = () => Subject.GetSeriesInfo(id);
            act.Should().Throw<SeriesNotFoundException>();
            Subject.GetAnimeRelations(id.ToString()).Should().BeNull();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase(0)]
        [TestCase(22)]
        public void should_reject_mismatched_catalogue_identity_instead_of_attaching_another_anime(int returnedId)
        {
            GivenCatalogue("{\"data\":{\"mal_id\":" + returnedId + ",\"title\":\"TV title\",\"type\":\"TV\"}}");
            Action act = () => Subject.GetSeriesInfo(21);
            act.Should().Throw<SeriesNotFoundException>();
            Subject.GetAnimeRelations("21").Should().BeNull();
        }

        [Test]
        public void should_retry_a_transient_429_after_shared_limiter_backoff_without_falling_back_to_other_catalogues()
        {
            var calls = 0;
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JikanAnimeResponse>(new HttpResponse(request,
                    new HttpHeader { ["Retry-After"] = "1" },
                    ++calls == 1 ? "{}" : "{\"data\":{\"mal_id\":21,\"title\":\"TV title\",\"type\":\"TV\",\"episodes\":1}}",
                    calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)));
            GivenEpisodes("{\"data\":[],\"pagination\":{\"has_next_page\":false}}");
            Subject.GetSeriesInfo(21).Item1.ForeignId.Should().Be("21");
            calls.Should().Be(2);
            Mocker.GetMock<IJikanRateLimiter>().Verify(limiter => limiter.BackOff(TimeSpan.FromSeconds(1)), Times.Once());
        }

        [Test]
        public void should_filter_invalid_search_ids_and_duplicate_catalogue_rows()
        {
            const string json = "{\"data\":[{\"mal_id\":0,\"title\":\"Invalid\"},{\"mal_id\":21,\"title\":\"Valid\"},{\"mal_id\":21,\"title\":\"Repeated\"},{\"mal_id\":22,\"title\":null}]}";
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JikanSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JikanSearchResponse>(new HttpResponse(request, new HttpHeader(), json)));
            Subject.SearchForNewSeries("Valid").Should().ContainSingle().Which.ForeignId.Should().Be("21");
        }

        [Test]
        public void should_stop_terminal_episode_429_retries_and_preserve_native_declared_numbering()
        {
            GivenCatalogue("{\"data\":{\"mal_id\":21,\"title\":\"TV title\",\"type\":\"TV\",\"episodes\":3}}");
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JikanEpisodeListResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JikanEpisodeListResponse>(new HttpResponse(request,
                    new HttpHeader { ["Retry-After"] = "1" },
                    "{}",
                    HttpStatusCode.TooManyRequests)));
            Subject.GetSeriesInfo(21).Item2.Select(episode => episode.EpisodeNumber).Should().Equal(1, 2, 3);
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<JikanEpisodeListResponse>(It.IsAny<HttpRequest>()), Times.Exactly(3));
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
