using System;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class AniListAccountSnapshotFixture : CoreTest
    {
        private static ImportListResponse Response(string content, HttpStatusCode status = HttpStatusCode.OK)
        {
            var request = new HttpRequest("https://graphql.anilist.co");
            return new ImportListResponse(new ImportListRequest(request), new HttpResponse(request, new HttpHeader(), content, status));
        }

        [Test]
        public void should_derive_membership_from_all_groups_without_per_entry_maps_or_array_aliases()
        {
            var snapshot = AniListAccountSnapshot.Load(" SOMEONE ",
                _ => Response(AniListImportFixture.Collection(false, true, false, "PLANNING", 1, 2)));
            var entries = snapshot.GetEntries().ToList();
            entries.Should().HaveCount(2);
            entries.Should().OnlyContain(entry => entry.Status == "PLANNING" && entry.CustomLists["Seasonal"] && !entry.CustomLists["Excluded"]);
            entries[0].CustomLists["Seasonal"] = false;
            snapshot.GetEntries().First().CustomLists["Seasonal"].Should().BeTrue();
        }

        [TestCase("owner")]
        [TestCase("collection-owner")]
        [TestCase("names")]
        [TestCase("continuation")]
        [TestCase("custom-flag")]
        [TestCase("unknown-custom-name")]
        [TestCase("entry-id")]
        [TestCase("entry-user")]
        [TestCase("media-id")]
        [TestCase("media-type")]
        [TestCase("entry-status")]
        [TestCase("media-status")]
        [TestCase("progress")]
        [TestCase("updated")]
        [TestCase("errors")]
        public void should_reject_missing_or_untrustworthy_filter_and_identity_fields_without_retaining_private_response(string defect)
        {
            var document = JsonNode.Parse(AniListImportFixture.Collection(false, true, false, "CURRENT", 1));
            var user = document["data"]["User"];
            var collection = document["data"]["MediaListCollection"];
            var entry = collection["lists"][0]["entries"][0];
            switch (defect)
            {
                case "owner": user["name"] = "PRIVATE-OTHER-OWNER"; break;
                case "collection-owner": collection["user"]["id"] = 11; break;
                case "names": user["mediaListOptions"]["animeList"]["customLists"] = new JsonArray("Seasonal", "Seasonal"); break;
                case "continuation": collection["hasNextChunk"] = null; break;
                case "custom-flag": collection["lists"][1]["isCustomList"] = null; break;
                case "unknown-custom-name": collection["lists"][1]["name"] = "PRIVATE-UNDECLARED-LIST"; break;
                case "entry-id": entry["id"] = 0; break;
                case "entry-user": entry["userId"] = 11; break;
                case "media-id": entry["media"]["id"] = 0; break;
                case "media-type": entry["media"]["type"] = "MANGA"; break;
                case "entry-status": entry["status"] = null; break;
                case "media-status": entry["media"]["status"] = null; break;
                case "progress": entry["progress"] = -1; break;
                case "updated": entry["updatedAt"] = -1; break;
                case "errors": document["errors"] = new JsonArray(new JsonObject { ["message"] = "PRIVATE-PROVIDER-ERROR" }); break;
            }

            Action load = () => AniListAccountSnapshot.Load("someone", _ => Response(document.ToJsonString()));
            var error = load.Should().Throw<ImportListException>().Which;
            error.Response.Should().BeNull();
            error.ToString().Should().Contain("previous list will be retained").And.NotContain("PRIVATE-");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_accept_nullable_or_omitted_non_filtering_progress_and_update_fields(bool omitted)
        {
            var document = JsonNode.Parse(AniListImportFixture.Collection(false, true, false, "CURRENT", 1));
            foreach (var group in document["data"]["MediaListCollection"]["lists"].AsArray())
            {
                foreach (var entry in group["entries"].AsArray())
                {
                    if (omitted)
                    {
                        entry.AsObject().Remove("progress");
                        entry.AsObject().Remove("updatedAt");
                    }
                    else
                    {
                        entry["progress"] = null;
                        entry["updatedAt"] = null;
                    }
                }
            }

            var snapshot = AniListAccountSnapshot.Load("someone", _ => Response(document.ToJsonString()));
            var entries = new AniListParser(new AniListSettings { IncludedCustomLists = new[] { "Seasonal" } }).ParseEntries(snapshot.GetEntries());
            entries.Should().ContainSingle().Which.AniListId.Should().Be(1);
        }

        [TestCase("customLists")]
        [TestCase("animeList")]
        [TestCase("mediaListOptions")]
        public void should_accept_nullable_options_and_unnamed_non_custom_groups_for_accounts_without_custom_lists(string nullableField)
        {
            var document = JsonNode.Parse(AniListImportFixture.Collection(false, false, false, "CURRENT", 1));
            var user = document["data"]["User"];
            var parent = nullableField == "customLists" ? user["mediaListOptions"]["animeList"] :
                nullableField == "animeList" ? user["mediaListOptions"] : user;
            parent[nullableField] = null;
            var groups = document["data"]["MediaListCollection"]["lists"].AsArray();
            groups[0]["name"] = null;
            groups.RemoveAt(2);
            groups.RemoveAt(1);

            var snapshot = AniListAccountSnapshot.Load("someone", _ => Response(document.ToJsonString()));
            var entry = snapshot.GetEntries().Should().ContainSingle().Which;
            entry.Media.Id.Should().Be(1);
            entry.CustomLists.Should().BeEmpty();
            new AniListParser(new AniListSettings { ExcludedCustomLists = new[] { "Not defined" } })
                .ParseEntries(snapshot.GetEntries()).Should().ContainSingle();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void should_allow_unused_non_custom_group_names_without_losing_custom_membership(string name)
        {
            var document = JsonNode.Parse(AniListImportFixture.Collection(false, true, false, "CURRENT", 1));
            document["data"]["MediaListCollection"]["lists"][0]["name"] = name;
            var snapshot = AniListAccountSnapshot.Load("someone", _ => Response(document.ToJsonString()));
            snapshot.GetEntries().Should().ContainSingle().Which.CustomLists["Seasonal"].Should().BeTrue();
        }

        [Test]
        public void should_derive_all_nullable_option_names_before_filtering_custom_only_entries_across_chunks()
        {
            var first = JsonNode.Parse(AniListImportFixture.Collection(true, true, false, "CURRENT", 1));
            var second = JsonNode.Parse(AniListImportFixture.Collection(false, false, true, "CURRENT", 2));
            first["data"]["User"]["mediaListOptions"]["animeList"]["customLists"] = null;
            second["data"]["User"]["mediaListOptions"]["animeList"]["customLists"] = null;
            var firstGroups = first["data"]["MediaListCollection"]["lists"].AsArray();
            firstGroups.RemoveAt(2);
            var secondGroups = second["data"]["MediaListCollection"]["lists"].AsArray();
            secondGroups.RemoveAt(1);
            secondGroups[0]["entries"] = new JsonArray();
            secondGroups[1]["name"] = "Later custom-only group";

            var snapshot = AniListAccountSnapshot.Load("someone", chunk => Response((chunk == 1 ? first : second).ToJsonString()));
            var entries = snapshot.GetEntries().ToList();
            entries.Should().HaveCount(2);
            entries[0].CustomLists.Should().Contain("Seasonal", true).And.Contain("Later custom-only group", false);
            entries[1].CustomLists.Should().Contain("Seasonal", false).And.Contain("Later custom-only group", true);
            new AniListParser(new AniListSettings
            {
                IncludedCustomLists = new[] { "Seasonal", "Later custom-only group" },
                ExcludedCustomLists = new[] { "Later custom-only group" }
            }).ParseEntries(entries).Should().ContainSingle().Which.AniListId.Should().Be(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_refuse_changed_nullable_options_mode_between_chunks(bool firstHasNames)
        {
            var first = JsonNode.Parse(AniListImportFixture.Collection(true, true, false, "CURRENT", 1));
            var second = JsonNode.Parse(AniListImportFixture.Collection(false, true, false, "CURRENT", 2));
            (firstHasNames ? second : first)["data"]["User"]["mediaListOptions"]["animeList"]["customLists"] = null;
            Action load = () => AniListAccountSnapshot.Load("someone", chunk => Response((chunk == 1 ? first : second).ToJsonString()));
            load.Should().Throw<ImportListException>().Which.Response.Should().BeNull();
        }

        [Test]
        public void should_refuse_changed_explicit_custom_list_definitions_between_chunks()
        {
            var first = JsonNode.Parse(AniListImportFixture.Collection(true, true, false, "CURRENT", 1));
            var second = JsonNode.Parse(AniListImportFixture.Collection(false, true, false, "CURRENT", 2));
            second["data"]["User"]["mediaListOptions"]["animeList"]["customLists"] = new JsonArray("Seasonal", "Changed");
            Action load = () => AniListAccountSnapshot.Load("someone", chunk => Response((chunk == 1 ? first : second).ToJsonString()));
            load.Should().Throw<ImportListException>().Which.Response.Should().BeNull();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void should_refuse_unnamed_custom_groups_even_when_options_are_nullable(string name)
        {
            var document = JsonNode.Parse(AniListImportFixture.Collection(false, true, false, "CURRENT", 1));
            document["data"]["User"]["mediaListOptions"]["animeList"]["customLists"] = null;
            document["data"]["MediaListCollection"]["lists"][1]["name"] = name;
            Action load = () => AniListAccountSnapshot.Load("someone", _ => Response(document.ToJsonString()));
            load.Should().Throw<ImportListException>().Which.Response.Should().BeNull();
        }

        [Test]
        public void should_bound_custom_names_derived_from_nullable_options()
        {
            var document = JsonNode.Parse(AniListImportFixture.Collection(false, false, false, "CURRENT", 1));
            document["data"]["User"]["mediaListOptions"]["animeList"]["customLists"] = null;
            var groups = document["data"]["MediaListCollection"]["lists"].AsArray();
            groups.RemoveAt(2);
            groups.RemoveAt(1);
            for (var index = 0; index < 129; index++)
            {
                groups.Add(new JsonObject { ["name"] = "Derived " + index, ["isCustomList"] = true, ["entries"] = new JsonArray() });
            }

            Action load = () => AniListAccountSnapshot.Load("someone", _ => Response(document.ToJsonString()));
            load.Should().Throw<ImportListException>().Which.Response.Should().BeNull();
        }

        [TestCase("progress")]
        [TestCase("updatedAt")]
        public void should_preserve_missing_optional_values_when_comparing_duplicate_entries(string field)
        {
            var document = JsonNode.Parse(AniListImportFixture.Collection(false, true, false, "CURRENT", 1));
            document["data"]["MediaListCollection"]["lists"][0]["entries"][0].AsObject().Remove(field);
            document["data"]["MediaListCollection"]["lists"][1]["entries"][0][field] = 0;
            Action load = () => AniListAccountSnapshot.Load("someone", _ => Response(document.ToJsonString()));
            load.Should().Throw<ImportListException>().Which.Message.Should().Contain("changed an entry");
        }

        [TestCase("status")]
        [TestCase("progress")]
        [TestCase("updatedAt")]
        public void should_refuse_a_duplicate_entry_that_changed_between_collection_groups(string changedField)
        {
            var document = JsonNode.Parse(AniListImportFixture.Collection(false, true, false, "CURRENT", 1));
            var duplicate = document["data"]["MediaListCollection"]["lists"][1]["entries"][0];
            if (changedField == "status")
            {
                duplicate[changedField] = "COMPLETED";
            }
            else
            {
                duplicate[changedField] = 1001;
            }

            Action load = () => AniListAccountSnapshot.Load("someone", _ => Response(document.ToJsonString()));
            load.Should().Throw<ImportListException>().Which.Message.Should().Contain("changed an entry");
        }

        [Test]
        public void should_refuse_the_provider_hard_cap_even_if_it_reports_no_next_chunk()
        {
            var data = AniListImportFixture.Collection(false, false, false, "CURRENT", Enumerable.Range(1, AniListAPI.MaxCollectionEntries).ToArray());
            Action load = () => AniListAccountSnapshot.Load("someone", _ => Response(data));
            load.Should().Throw<ImportListException>().Which.Message.Should().Contain("11,000");
        }

        [Test]
        public void should_reject_an_oversized_response_before_deserializing_or_retaining_its_body()
        {
            var content = "{\"PRIVATE-DATA\":\"" + new string('x', 8 * 1024 * 1024) + "\"}";
            Action load = () => AniListAccountSnapshot.Load("someone", _ => Response(content));
            var error = load.Should().Throw<ImportListException>().Which;
            error.Message.Should().Contain("memory limit").And.NotContain("PRIVATE-DATA");
            error.Response.Should().BeNull();
        }

        [Test]
        public void should_refuse_empty_continuing_chunks_and_http_errors()
        {
            Action empty = () => AniListAccountSnapshot.Load("someone", _ => Response(AniListImportFixture.Collection(true, false, false)));
            empty.Should().Throw<ImportListException>().Which.Message.Should().Contain("empty collection chunk");
            Action denied = () => AniListAccountSnapshot.Load("someone",
                _ => Response(AniListImportFixture.Collection(false, false, false), HttpStatusCode.Unauthorized));
            denied.Should().Throw<ImportListException>().Which.Response.Should().BeNull();
        }
    }
}
