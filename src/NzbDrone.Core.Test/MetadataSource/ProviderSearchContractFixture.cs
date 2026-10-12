using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.MetadataSource.Jikan;
using NzbDrone.Core.MetadataSource.Jikan.Resource;
using NzbDrone.Core.MetadataSource.Tmdb;
using NzbDrone.Core.MetadataSource.Tmdb.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class ProviderSearchContractFixture : CoreTest
    {
        private static HttpResponse<T> Response<T>(HttpRequest request, string json)
            where T : new()
        {
            return new HttpResponse<T>(new HttpResponse(request, new HttpHeader(), json));
        }

        [Test]
        public void should_keep_provider_ids_distinct_and_offer_an_official_season_type()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.TmdbApiKey).Returns("test-key");
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<AniListSearchResponse>(request,
                    "{\"data\":{\"Page\":{\"media\":[{\"id\":1,\"title\":{\"english\":\"Test\"},\"episodes\":12}]}}}"));
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<JikanSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<JikanSearchResponse>(request,
                    "{\"data\":[{\"mal_id\":1,\"title\":\"Test\",\"episodes\":12}]}"));
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<TmdbSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<TmdbSearchResponse>(request,
                    "{\"results\":[{\"id\":1,\"name\":\"Test\"}]}"));

            var results = new[]
            {
                Mocker.Resolve<AniListProxy>().SearchForNewSeries("Test").Single(),
                Mocker.Resolve<JikanProxy>().SearchForNewSeries("Test").Single(),
                Mocker.Resolve<TmdbProxy>().SearchForNewSeries("Test").Single()
            };

            results.Select(s => s.ForeignId).Should().OnlyContain(id => id == "1");
            results.Select(s => s.TitleSlug).Should().OnlyHaveUniqueItems("IDs belong to separate catalogs");
            foreach (var series in results)
            {
                series.SeasonType.Should().Be(SeasonType.Official);
                series.SeasonTypes.Should().ContainSingle().Which.Type.Should().Be(SeasonType.Official);
            }
        }
    }
}
