using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class AniListMembershipSnapshotFixture : CoreTest
    {
        private static string Collection(bool hasNextChunk, int[] standard, int[] custom = null)
        {
            var lists = new List<object>
            {
                new { name = "Watching", isCustomList = false, entries = standard.Select(id => new { media = new { id, type = "ANIME" } }).ToArray() }
            };
            if (custom != null)
            {
                lists.Add(new { name = "Selected", isCustomList = true, entries = custom.Select(id => new { media = new { id, type = "ANIME" } }).ToArray() });
            }

            return Json.ToJson(new
            {
                data = new
                {
                    User = new { id = 10, name = "someone", mediaListOptions = new { animeList = new { customLists = new[] { "Selected", "Empty" } } } },
                    MediaListCollection = new { user = new { id = 10, name = "someone" }, hasNextChunk, lists }
                }
            });
        }

        private static ImportListResponse Response(string content, HttpStatusCode status = HttpStatusCode.OK)
        {
            var request = new HttpRequest("https://graphql.anilist.co");
            var response = new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(content), status);
            return new ImportListResponse(new ImportListRequest(request), response);
        }

        [Test]
        public void should_derive_complete_membership_with_empty_defined_groups_and_duplicate_occurrences()
        {
            var snapshot = AniListCustomListMembershipSnapshot.Load(" SOMEONE ",
                _ => Response(Collection(false, new[] { 1, 2 }, new[] { 1 })));

            snapshot.GetMembership(1).Should().BeEquivalentTo(new Dictionary<string, bool> { { "Selected", true }, { "Empty", false } });
            snapshot.GetMembership(2).Should().BeEquivalentTo(new Dictionary<string, bool> { { "Selected", false }, { "Empty", false } });
            var copy = snapshot.GetMembership(1);
            copy["Selected"] = false;
            snapshot.GetMembership(1)["Selected"].Should().BeTrue();
        }

        [Test]
        public void should_merge_all_chunks_before_exposing_membership()
        {
            var requested = new List<int>();
            var snapshot = AniListCustomListMembershipSnapshot.Load("someone", chunk =>
            {
                requested.Add(chunk);
                return Response(Collection(chunk == 1, new[] { chunk }, chunk == 2 ? new[] { 1 } : null));
            });

            requested.Should().Equal(1, 2);
            snapshot.GetMembership(1)["Selected"].Should().BeTrue();
            snapshot.GetMembership(2)["Selected"].Should().BeFalse();
        }

        [Test]
        public void should_allow_more_group_occurrences_than_the_per_chunk_size()
        {
            var ids = Enumerable.Range(1, 412).ToArray();
            var snapshot = AniListCustomListMembershipSnapshot.Load("someone",
                _ => Response(Collection(false, ids, ids.Take(111).ToArray())));

            snapshot.GetMembership(412)["Selected"].Should().BeFalse();
            snapshot.GetMembership(111)["Selected"].Should().BeTrue();
        }

        [Test]
        public void should_refuse_a_page_id_absent_from_the_complete_collection()
        {
            var snapshot = AniListCustomListMembershipSnapshot.Load("someone", _ => Response(Collection(false, new[] { 1 })));

            Action membership = () => snapshot.GetMembership(2);

            membership.Should().Throw<ImportListException>().Which.Message.Should().Contain("previous list will be retained");
        }

        [TestCase("owner-name")]
        [TestCase("collection-owner")]
        [TestCase("type")]
        [TestCase("media-id")]
        [TestCase("custom-name")]
        [TestCase("custom-flag")]
        [TestCase("definitions")]
        [TestCase("next-chunk")]
        [TestCase("errors")]
        [TestCase("malformed")]
        public void should_reject_incomplete_or_mismatched_authoritative_membership(string defect)
        {
            var resource = JsonNode.Parse(Collection(false, new[] { 1 }, new[] { 1 }));
            var user = resource["data"]["user"];
            var collection = resource["data"]["mediaListCollection"];
            switch (defect)
            {
                case "owner-name":
                    user["name"] = "another-user";
                    break;
                case "collection-owner":
                    collection["user"]["id"] = 20;
                    break;
                case "type":
                    collection["lists"][0]["entries"][0]["media"]["type"] = "MANGA";
                    break;
                case "media-id":
                    collection["lists"][0]["entries"][0]["media"]["id"] = 0;
                    break;
                case "custom-name":
                    collection["lists"][1]["name"] = "Undefined";
                    break;
                case "custom-flag":
                    collection["lists"][1]["isCustomList"] = null;
                    break;
                case "definitions":
                    user["mediaListOptions"]["animeList"]["customLists"] = null;
                    break;
                case "next-chunk":
                    collection["hasNextChunk"] = null;
                    break;
                case "errors":
                    resource["errors"] = JsonNode.Parse("[{\"message\":\"Private\"}]");
                    break;
            }

            var json = defect == "malformed" ? "{" : resource.ToJsonString();

            Action load = () => AniListCustomListMembershipSnapshot.Load("someone", _ => Response(json));

            load.Should().Throw<ImportListException>();
        }

        [Test]
        public void should_refuse_a_collection_that_does_not_finish_before_the_chunk_limit()
        {
            var calls = 0;

            Action load = () => AniListCustomListMembershipSnapshot.Load("someone", _ =>
            {
                calls++;
                return Response(Collection(true, new[] { calls }));
            });

            load.Should().Throw<ImportListException>();
            calls.Should().Be(AniListAPI.MaxPages);
        }

        [Test]
        public void should_refuse_metadata_definitions_changed_between_chunks()
        {
            Action load = () => AniListCustomListMembershipSnapshot.Load("someone", chunk =>
                Response(chunk == 1 ? Collection(true, new[] { 1 }) : Collection(false, new[] { 2 }).Replace("\"Empty\"", "\"Changed\"")));

            load.Should().Throw<ImportListException>();
        }

        [Test]
        public void should_refuse_an_http_failure_even_with_a_complete_response_body()
        {
            Action load = () => AniListCustomListMembershipSnapshot.Load("someone",
                _ => Response(Collection(false, new[] { 1 }), HttpStatusCode.Forbidden));

            load.Should().Throw<ImportListException>();
        }
    }
}
