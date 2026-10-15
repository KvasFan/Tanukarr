using System;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Tmdb;
using NzbDrone.Core.MetadataSource.Tmdb.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class TmdbMovieProxyFixture : CoreTest<TmdbMovieProxy>
    {
        private const string MovieJson = "{\"id\":970001,\"title\":\"Generated Feature\",\"release_date\":\"2019-01-01\",\"runtime\":95,\"imdb_id\":\"tt9700001\",\"original_title\":\"Original Feature\",\"original_language\":\"ja\",\"overview\":\"Feature overview\",\"vote_average\":8.1,\"vote_count\":42,\"poster_path\":\"/owned.jpg\",\"belongs_to_collection\":{\"id\":970000,\"name\":\"Generated Collection\"}}";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.TmdbApiKey).Returns("generated-unit-only-key");
            Given<TmdbMovieResource>(MovieJson);
        }

        private void Given<T>(string json)
            where T : new()
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<T>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<T>(new HttpResponse(request, new HttpHeader(), json, HttpStatusCode.OK)));
        }

        [Test]
        public void should_read_real_json_and_map_one_native_feature_without_tv_identity()
        {
            var result = Subject.GetSeriesInfo(new Series { ForeignId = "970001" });
            result.Item1.MediaType.Should().Be(MediaType.Movie);
            result.Item1.MetadataSource.Should().Be(MetadataSourceType.TmdbMovie);
            result.Item1.TmdbId.Should().Be(970001);
            result.Item1.ForeignId.Should().Be("970001");
            result.Item1.TvdbId.Should().Be(0);
            result.Item1.ImdbId.Should().Be("tt9700001");
            result.Item1.Year.Should().Be(2019);
            result.Item1.Images.Single().RemoteUrl.Should().Be("https://image.tmdb.org/t/p/original/owned.jpg");
            result.Item2.Should().ContainSingle().Which.ForeignId.Should().Be("970001");
            result.Item2.Single().Runtime.Should().Be(95);
            result.Item2.Single().SeasonNumber.Should().Be(1);
            result.Item2.Single().EpisodeNumber.Should().Be(1);
        }

        [Test]
        public void should_resolve_explicit_imdb_through_the_supported_movie_find_endpoint()
        {
            Given<TmdbMovieSearchResource>("{\"movie_results\":[" + MovieJson + "],\"tv_results\":[{\"id\":970001,\"name\":\"Different TV show\"}]}");
            Subject.SearchForNewSeries("imdb:tt9700001").Single().MetadataSource.Should().Be(MetadataSourceType.TmdbMovie);
            Mocker.GetMock<IHttpClient>().Verify(
                client => client.Get<TmdbMovieSearchResource>(It.Is<HttpRequest>(request =>
                request.Url.FullUri.Contains("/find/tt9700001?") && request.Url.FullUri.Contains("external_source=imdb_id"))),
                Times.Once());
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("../tv/1")]
        [TestCase("tt9700001")]
        public void should_reject_invalid_native_movie_ids_before_http(string id)
        {
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = id });
            action.Should().Throw<MovieMetadataException>();
            Action collection = () => Subject.GetMovieCollection(new Series { ForeignId = id });
            collection.Should().Throw<MovieMetadataException>();
            Mocker.GetMock<IHttpClient>().Verify(
                client => client.Get<TmdbMovieResource>(It.IsAny<HttpRequest>()),
                Times.Never());
        }

        [Test]
        public void should_reject_conflicting_refresh_identity()
        {
            Given<TmdbMovieResource>("{\"id\":970002,\"title\":\"Wrong movie\"}");
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = "970001" });
            action.Should().Throw<MovieMetadataException>();
        }

        [Test]
        public void should_resolve_catalogue_collection_in_date_then_native_id_order()
        {
            Given<TmdbMovieCollectionResource>("{\"id\":970000,\"name\":\"Generated Collection\",\"parts\":[{\"id\":970002,\"title\":\"Second\",\"release_date\":\"2021-01-01\"},{\"id\":970001,\"title\":\"First\",\"release_date\":\"2019-01-01\"}]}");
            var result = Subject.GetMovieCollection(new Series { ForeignId = "970001" });
            result.AnimeGroupId.Should().Be("tmdb-movie:970000");
            result.AnimeGroupMovieNumber.Should().Be(1);
            result.AnimeGroupSeasonNumber.Should().BeNull();
        }

        [Test]
        public void should_refuse_incomplete_collection_instead_of_guessing_movie_order()
        {
            Given<TmdbMovieCollectionResource>("{\"id\":970000,\"name\":\"Generated Collection\",\"parts\":[{\"id\":970001,\"title\":\"First\"}]}");
            Action action = () => Subject.GetMovieCollection(new Series { ForeignId = "970001" });
            action.Should().Throw<MovieMetadataException>();
        }

        [TestCase("{\"raw-secret-marker\":broken}")]
        [TestCase("null")]
        public void should_hide_malformed_response_data_from_exceptions(string response)
        {
            Given<TmdbMovieResource>(response);
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = "970001" });
            var exception = action.Should().Throw<MovieMetadataException>().Which;
            exception.ToString().Should().NotContain("raw-secret-marker");
            exception.InnerException.Should().BeNull();
        }

        [Test]
        public void should_reject_oversized_response_before_decoding()
        {
            Given<TmdbMovieResource>(new string('x', 4194305));
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = "970001" });
            action.Should().Throw<MovieMetadataException>();
        }

        [Test]
        public void should_reject_implausible_runtime_before_pack_arithmetic()
        {
            Given<TmdbMovieResource>(MovieJson.Replace("\"runtime\":95", "\"runtime\":2147483647"));
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = "970001" });
            action.Should().Throw<MovieMetadataException>();
        }
    }
}
