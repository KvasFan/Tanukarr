using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.AniList;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.MetadataSource.Jikan;
using NzbDrone.Core.MetadataSource.Jikan.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.Grouping
{
    [TestFixture]
    public class AnimeRelationProviderFixture : CoreTest
    {
        private const string ValidRelations = @"{
            ""data"": { ""Media"": {
                ""id"": 1, ""type"": ""ANIME"", ""format"": ""TV"", ""title"": { ""english"": ""Root"" },
                ""relations"": { ""edges"": [
                    { ""relationType"": ""SEQUEL"", ""node"": { ""id"": 2, ""type"": ""ANIME"", ""format"": ""TV"" } }
                ] }
            } }
        }";

        private void GivenRelationResponse(string json)
        {
            Mocker.GetMock<IAniListRequestService>().Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), json));
        }

        [TestCase("195516", "anilist:161645", "The Apothecary Diaries", 3)]
        [TestCase("178083", "anilist:120120", "Tokyo Revengers", 4)]
        public void should_resolve_real_catalogue_sequels_under_the_canonical_series_title(string foreignId, string groupId, string title, int season)
        {
            // Public AniList response recorded on 2026-10-13. Unrelated manga,
            // side-story and spinoff links remain present in the serialized fixture.
            var catalogue = JObject.Parse(ReadAllText("Files/Metadata/AniList/official-group-relations.json"))["data"]["Page"]["media"]
                .ToDictionary(media => media["id"].Value<string>());
            var requests = new List<string>();
            Mocker.GetMock<IAniListRequestService>().Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    var requested = JObject.Parse(Encoding.UTF8.GetString(request.ContentData))["variables"]["id"].Value<string>();
                    requests.Add(requested);
                    return new HttpResponse(request, new HttpHeader(), new JObject
                    {
                        ["data"] = new JObject { ["Media"] = catalogue[requested].DeepClone() }
                    }.ToString());
                });

            var result = AnimeGroupingResolver.Resolve(MetadataSourceType.AniList, foreignId, Mocker.Resolve<AniListProxy>().GetAnimeRelations, Array.Empty<Series>());
            result.Status.Should().Be(AnimeGroupingStatus.Resolved);
            result.AnimeGroupId.Should().Be(groupId);
            result.AnimeGroupTitle.Should().Be(title);
            result.AnimeGroupFolder.Should().Be(title);
            result.AnimeGroupSeasonNumber.Should().Be(season);
            result.AnimeGroupMemberKind.Should().Be(AnimeGroupMemberKind.Season);
            requests.Should().OnlyHaveUniqueItems();
            requests.Count.Should().BeLessThanOrEqualTo(AnimeGroupingResolver.MaxLookups);
            requests.Should().NotContain(new[] { "170508", "186148", "132467", "178084", "99026", "102988" });
        }

        [Test]
        public void should_reject_http_200_graphql_errors_even_when_partial_data_looks_valid()
        {
            var json = JObject.Parse(ValidRelations);
            json["errors"] = JArray.Parse("[{\"message\":\"Relationship field unavailable\",\"path\":[\"Media\",\"relations\"]}]");
            GivenRelationResponse(json.ToString());

            var result = AnimeGroupingResolver.Resolve(MetadataSourceType.AniList, "1", Mocker.Resolve<AniListProxy>().GetAnimeRelations, Array.Empty<Series>());
            result.Status.Should().Be(AnimeGroupingStatus.Unavailable);
            result.AnimeGroupId.Should().BeNull();
        }

        [TestCase("relations")]
        [TestCase("edges")]
        [TestCase("relationType")]
        [TestCase("node")]
        [TestCase("nodeType")]
        [TestCase("nodeFormat")]
        [TestCase("rootFormat")]
        public void should_not_resolve_a_chain_from_partial_relation_data(string missingField)
        {
            var json = JObject.Parse(ValidRelations);
            var media = json["data"]["Media"];
            var edge = media["relations"]["edges"][0];
            switch (missingField)
            {
                case "relations": media["relations"] = null; break;
                case "edges": media["relations"]["edges"] = null; break;
                case "relationType": edge["relationType"] = null; break;
                case "node": edge["node"] = null; break;
                case "nodeType": edge["node"]["type"] = null; break;
                case "nodeFormat": edge["node"]["format"] = null; break;
                case "rootFormat": media["format"] = null; break;
            }

            GivenRelationResponse(json.ToString());
            var result = AnimeGroupingResolver.Resolve(MetadataSourceType.AniList, "1", Mocker.Resolve<AniListProxy>().GetAnimeRelations, Array.Empty<Series>());
            result.Status.Should().Be(AnimeGroupingStatus.Unavailable);
            result.AnimeGroupId.Should().BeNull();
            Mocker.GetMock<IAniListRequestService>().Verify(s => s.Execute(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void should_distinguish_an_explicit_empty_relation_list_from_missing_relations()
        {
            var json = JObject.Parse(ValidRelations);
            json["data"]["Media"]["relations"]["edges"] = new JArray();
            GivenRelationResponse(json.ToString());
            var result = AnimeGroupingResolver.Resolve(MetadataSourceType.AniList, "1", Mocker.Resolve<AniListProxy>().GetAnimeRelations, Array.Empty<Series>());
            result.Status.Should().Be(AnimeGroupingStatus.Unrelated);
        }

        [Test]
        public void should_not_hide_a_missing_ancestor_relation_list_in_a_valid_child_chain()
        {
            var child = JObject.Parse(ValidRelations);
            child["data"]["Media"]["id"] = 2;
            child["data"]["Media"]["relations"]["edges"][0]["relationType"] = "PREQUEL";
            child["data"]["Media"]["relations"]["edges"][0]["node"]["id"] = 1;
            var root = JObject.Parse(ValidRelations);
            root["data"]["Media"]["relations"] = null;
            var calls = 0;
            Mocker.GetMock<IAniListRequestService>().Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), ++calls == 1 ? child.ToString() : root.ToString()));

            var result = AnimeGroupingResolver.Resolve(MetadataSourceType.AniList, "2", Mocker.Resolve<AniListProxy>().GetAnimeRelations, Array.Empty<Series>());
            result.Status.Should().Be(AnimeGroupingStatus.Unavailable);
            result.AnimeGroupId.Should().BeNull();
            calls.Should().Be(2);
        }

        [Test]
        public void should_deserialize_anilist_relations_using_a_lightweight_paced_query()
        {
            Mocker.GetMock<IAniListRequestService>()
                .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), @"{
                    ""data"": { ""Media"": {
                        ""id"": 2, ""type"": ""ANIME"", ""format"": ""TV"",
                        ""title"": { ""english"": ""Part Two"" },
                        ""relations"": { ""edges"": [
                            { ""relationType"": ""PREQUEL"", ""node"": { ""id"": 1, ""type"": ""ANIME"", ""format"": ""TV"" } },
                            { ""relationType"": ""SEQUEL"", ""node"": { ""id"": 3, ""type"": ""ANIME"", ""format"": ""SPECIAL"" } },
                            { ""relationType"": ""ADAPTATION"", ""node"": { ""id"": 4, ""type"": ""MANGA"", ""format"": ""MANGA"" } },
                            { ""relationType"": ""SIDE_STORY"", ""node"": { ""id"": 5, ""type"": ""ANIME"", ""format"": ""TV"" } }
                        ] }
                    } }
                }"));

            var result = Mocker.Resolve<AniListProxy>().GetAnimeRelations("2");
            result.ForeignId.Should().Be("2");
            result.Title.Should().Be("Part Two");
            result.PrequelIds.Should().Equal("1");
            result.SequelIds.Should().Equal("3");
            result.RelationFormats["3"].Should().Be("SPECIAL");
            Mocker.GetMock<IAniListRequestService>().Verify(
                s => s.Execute(It.Is<HttpRequest>(request =>
                    request.Url.ToString() == AniListQueries.BaseUrl && request.Method == HttpMethod.Post &&
                    Encoding.UTF8.GetString(request.ContentData).Contains("relationType(version: 2)") &&
                    !Encoding.UTF8.GetString(request.ContentData).Contains("airingSchedule"))),
                Times.Once());
        }

        [Test]
        public void should_deserialize_jikan_relations_without_reading_episode_pages()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JikanAnimeResponse>(new HttpResponse(request, new HttpHeader(), @"{
                    ""data"": { ""mal_id"": 2, ""title"": ""Part Two"", ""type"": ""TV Short"",
                        ""relations"": [
                            { ""relation"": ""Prequel"", ""entry"": [{ ""mal_id"": 1, ""type"": ""anime"" }] },
                            { ""relation"": ""Sequel"", ""entry"": [{ ""mal_id"": 3, ""type"": ""anime"" }] },
                            { ""relation"": ""Adaptation"", ""entry"": [{ ""mal_id"": 4, ""type"": ""manga"" }] },
                            { ""relation"": ""Side story"", ""entry"": [{ ""mal_id"": 5, ""type"": ""anime"" }] }
                        ]
                    }
                }")));

            var result = Mocker.Resolve<JikanProxy>().GetAnimeRelations("2");
            result.ForeignId.Should().Be("2");
            result.Format.Should().Be("TV_SHORT");
            result.PrequelIds.Should().Equal("1");
            result.SequelIds.Should().Equal("3");
            Mocker.GetMock<IJikanRateLimiter>().Verify(l => l.WaitForSlot(), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(
                c => c.Get<JikanAnimeResponse>(It.Is<HttpRequest>(request => request.Url.ToString() == "https://api.jikan.moe/v4/anime/2/full")),
                Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get<JikanEpisodeListResponse>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("not-an-id")]
        [TestCase("1/../../other")]
        [TestCase("2147483648")]
        public void should_not_build_requests_from_invalid_foreign_ids(string id)
        {
            Mocker.Resolve<AniListProxy>().GetAnimeRelations(id).Should().BeNull();
            Mocker.Resolve<JikanProxy>().GetAnimeRelations(id).Should().BeNull();
            Mocker.GetMock<IAniListRequestService>().Verify(s => s.Execute(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IJikanRateLimiter>().Verify(l => l.WaitForSlot(), Times.Never());
        }
    }
}
