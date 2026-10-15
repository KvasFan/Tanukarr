using System;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Omdb;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class OmdbProxyFixture : CoreTest<OmdbProxy>
    {
        private const string Json = "{\"Title\":\"Generated Feature\",\"Year\":\"2020\",\"Released\":\"01 Jan 2020\",\"Type\":\"movie\",\"Runtime\":\"103 min\",\"Plot\":\"Generated full plot\",\"Genre\":\"Drama, Adventure\",\"imdbID\":\"tt9700101\",\"imdbRating\":\"8.2\",\"imdbVotes\":\"12,345\",\"Response\":\"True\",\"Poster\":\"https://m.media-amazon.com/images/owned.jpg\"}";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.OmdbApiKey).Returns("generated-unit-only-key");
            Given(Json);
        }

        private void Given(string json)
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<OmdbResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<OmdbResource>(new HttpResponse(request, new HttpHeader(), json, HttpStatusCode.OK)));
        }

        [Test]
        public void should_deserialize_imdb_native_identity_and_complete_feature_metadata()
        {
            var result = Subject.GetSeriesInfo(new Series { ForeignId = "tt9700101" });
            result.Item1.MetadataSource.Should().Be(MetadataSourceType.Omdb);
            result.Item1.MediaType.Should().Be(MediaType.Movie);
            result.Item1.ForeignId.Should().Be("tt9700101");
            result.Item1.ImdbId.Should().Be("tt9700101");
            result.Item1.TvdbId.Should().Be(0);
            result.Item1.TmdbId.Should().Be(0);
            result.Item1.Ratings.Votes.Should().Be(12345);
            result.Item1.Ratings.Value.Should().Be(8.2m);
            result.Item2.Single().Runtime.Should().Be(103);
            result.Item2.Single().AirDate.Should().Be("2020-01-01");
            result.Item2.Single().ForeignId.Should().Be("tt9700101");
            result.Item1.Images.Single().RemoteUrl.Should().Be("https://m.media-amazon.com/images/owned.jpg");
        }

        [TestCase("series")]
        [TestCase("episode")]
        public void should_never_turn_an_imdb_tv_result_into_a_movie(string type)
        {
            Given(Json.Replace("\"movie\"", "\"" + type + "\""));
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = "tt9700101" });
            action.Should().Throw<MovieMetadataException>();
        }

        [TestCase("tt9700102")]
        [TestCase(null)]
        public void should_retain_existing_metadata_when_the_provider_identity_is_wrong(string id)
        {
            Given(Json.Replace("tt9700101", id ?? string.Empty));
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = "tt9700101" });
            action.Should().Throw<MovieMetadataException>();
        }

        [TestCase("9700101")]
        [TestCase("tt1")]
        [TestCase("tt9700101/../")]
        public void should_reject_invalid_imdb_identifiers_before_http(string id)
        {
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = id });
            action.Should().Throw<MovieMetadataException>();
            Mocker.GetMock<IHttpClient>().Verify(
                client => client.Get<OmdbResource>(It.IsAny<HttpRequest>()),
                Times.Never());
        }

        [Test]
        public void should_filter_series_and_duplicate_search_results_without_native_id_substitution()
        {
            Given("{\"Response\":\"True\",\"Search\":[" + Json + "," + Json + "," + Json.Replace("\"movie\"", "\"series\"") + "]}");
            var results = Subject.SearchForNewSeries("Generated");
            results.Should().ContainSingle().Which.ForeignId.Should().Be("tt9700101");
        }

        [TestCase("https://127.0.0.1/private")]
        [TestCase("https://m.media-amazon.com.evil.invalid/poster.jpg")]
        [TestCase("https://user:password@m.media-amazon.com/poster.jpg")]
        [TestCase("https://m.media-amazon.com:444/poster.jpg")]
        [TestCase("http://m.media-amazon.com/poster.jpg")]
        public void should_ignore_untrusted_poster_urls(string url)
        {
            Given(Json.Replace("https://m.media-amazon.com/images/owned.jpg", url));
            Subject.GetSeriesInfo(new Series { ForeignId = "tt9700101" }).Item1.Images.Should().BeEmpty();
        }

        [TestCase("{\"raw-secret-marker\":broken}")]
        [TestCase("null")]
        public void should_hide_malformed_response_data_from_exceptions(string response)
        {
            Given(response);
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = "tt9700101" });
            var exception = action.Should().Throw<MovieMetadataException>().Which;
            exception.ToString().Should().NotContain("raw-secret-marker");
            exception.InnerException.Should().BeNull();
        }

        [Test]
        public void should_reject_oversized_response_before_decoding()
        {
            Given(new string('x', 4194305));
            Action action = () => Subject.GetSeriesInfo(new Series { ForeignId = "tt9700101" });
            action.Should().Throw<MovieMetadataException>();
        }

        [Test]
        public void should_bound_transport_and_suppress_sensitive_http_details()
        {
            Subject.GetSeriesInfo(new Series { ForeignId = "tt9700101" });
            Mocker.GetMock<IHttpClient>().Verify(
                client => client.Get<OmdbResource>(It.Is<HttpRequest>(request =>
                request.RequestTimeout == TimeSpan.FromSeconds(15) && request.RateLimit == TimeSpan.FromSeconds(1) &&
                !request.AllowAutoRedirect && !request.LogHttpError && !request.LogResponseContent)),
                Times.Once());
        }
    }
}
