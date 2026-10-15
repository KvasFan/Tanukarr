using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using V3Mapper = Sonarr.Api.V3.Series.SeriesResourceMapper;
using V3Resource = Sonarr.Api.V3.Series.SeriesResource;

namespace NzbDrone.Api.Test.SeriesTests
{
    [TestFixture]
    public class SeriesProviderIdentityFixture : TestBase
    {
        [TestCase(MetadataSourceType.Tvdb, "tvdb")]
        [TestCase(MetadataSourceType.Tmdb, "tmdb")]
        [TestCase(MetadataSourceType.AniList, "aniList")]
        [TestCase(MetadataSourceType.MyAnimeList, "myAnimeList")]
        [TestCase(MetadataSourceType.TvMaze, "tvMaze")]
        public void should_round_trip_native_v3_provider_identity_in_both_serializers(MetadataSourceType source, string serialized)
        {
            var series = Native(source);
            var resource = V3Mapper.ToResource(series);
            foreach (var json in new[] { STJson.ToJson(resource), Json.ToJson(resource) })
            {
                var value = JObject.Parse(json);
                value["metadataSource"].Value<string>().Should().Be(serialized);
                value["foreignId"].Value<string>().Should().Be("73");
                value["orderingId"].Value<string>().Should().Be("owned-ordering");
                value["malIds"].ToObject<int[]>().Should().Equal(81);
                value["aniListIds"].ToObject<int[]>().Should().Equal(91);
            }

            var roundTrip = V3Mapper.ToModel(Json.Deserialize<V3Resource>(Json.ToJson(resource)));
            roundTrip.MetadataSource.Should().Be(source);
            roundTrip.ForeignId.Should().Be("73");
            roundTrip.OrderingId.Should().Be("owned-ordering");
            roundTrip.MalIds.Should().Equal(81);
            roundTrip.AniListIds.Should().Equal(91);
        }

        [Test]
        public void should_keep_the_owning_provider_and_cross_ids_when_an_older_v3_update_omits_new_fields()
        {
            var series = Native(MetadataSourceType.TvMaze);
            var resource = Json.Deserialize<V3Resource>("{\"id\":1,\"tvdbId\":-99,\"monitored\":false}");

            V3Mapper.ToModel(resource, series);

            series.MetadataSource.Should().Be(MetadataSourceType.TvMaze);
            series.ForeignId.Should().Be("73");
            series.OrderingId.Should().Be("owned-ordering");
            series.TvdbId.Should().Be(-7);
            series.MalIds.Should().Equal(81);
            series.AniListIds.Should().Equal(91);
        }

        [Test]
        public void should_default_an_older_v3_add_to_tvdb()
        {
            V3Mapper.ToModel(Json.Deserialize<V3Resource>("{\"tvdbId\":123}")).MetadataSource.Should().Be(MetadataSourceType.Tvdb);
        }

        private static Series Native(MetadataSourceType source)
        {
            return new Series
            {
                Id = 1, Title = "Generated native series", MetadataSource = source, ForeignId = "73", OrderingId = "owned-ordering",
                TvdbId = -7, MalIds = new HashSet<int> { 81 }, AniListIds = new HashSet<int> { 91 }
            };
        }
    }
}
