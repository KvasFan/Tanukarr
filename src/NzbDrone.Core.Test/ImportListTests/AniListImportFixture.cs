using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.AniList;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class AniListImportFixture : CoreTest
    {
        private AniListImport _subject;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAniListRequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Mocker.GetMock<IHttpClient>().Object.Execute(request));
            _subject = Mocker.Resolve<AniListImport>();
            _subject.Definition = new ImportListDefinition
            {
                Id = 10,
                Name = "Anime",
                Settings = new AniListSettings
                {
                    Username = "someone",
                    AccessToken = "test-access-token",
                    RefreshToken = "test-refresh-token",
                    Expires = DateTime.UtcNow.AddDays(30)
                }
            };
        }

        internal static string Collection(bool next, bool include, bool exclude, string status = "CURRENT", params int[] ids)
        {
            object Entry(int id) => new
            {
                id = id + 100000,
                userId = 10,
                status,
                progress = 0,
                updatedAt = 1000,
                media = new
                {
                    id,
                    idMal = id + 200000,
                    type = "ANIME",
                    format = "TV",
                    status = "FINISHED",
                    title = new { userPreferred = "Same title", romaji = "Generated title" },
                    relations = new { edges = Array.Empty<object>() }
                }
            };

            return JsonSerializer.Serialize(new
            {
                data = new
                {
                    User = new { id = 10, name = "someone", mediaListOptions = new { animeList = new { customLists = new[] { "Seasonal", "Excluded" } } } },
                    MediaListCollection = new
                    {
                        user = new { id = 10, name = "someone" },
                        hasNextChunk = next,
                        lists = new[]
                        {
                            new { name = "Status group", isCustomList = false, entries = ids.Select(Entry).ToArray() },
                            new { name = "Seasonal", isCustomList = true, entries = include ? ids.Select(Entry).ToArray() : Array.Empty<object>() },
                            new { name = "Excluded", isCustomList = true, entries = exclude ? ids.Select(Entry).ToArray() : Array.Empty<object>() }
                        }
                    }
                }
            });
        }

        private void GivenResponses(params string[] responses)
        {
            var bodies = new Queue<string>(responses);
            Mocker.GetMock<IHttpClient>().Setup(client => client.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(bodies.Dequeue())));
        }

        private void GivenCustomExclusions()
        {
            var settings = (AniListSettings)_subject.Definition.Settings;
            settings.IncludedCustomLists = new[] { "Seasonal" };
            settings.ExcludedCustomLists = new[] { "Excluded" };
        }

        private void GivenRateLimit(int seconds)
        {
            Mocker.GetMock<IAniListRequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => throw new TooManyRequestsException(request,
                    new HttpResponse(request,
                        new HttpHeader { { "Retry-After", seconds.ToString() } },
                        Encoding.UTF8.GetBytes("{\"errors\":[{\"message\":\"PRIVATE-FIXTURE\",\"status\":429}]}"),
                        HttpStatusCode.TooManyRequests)));
        }

        [Test]
        public void should_fetch_complete_account_chunks_through_the_shared_authenticated_client()
        {
            GivenResponses(Collection(true, true, false, "PLANNING", 1), Collection(false, true, false, "CURRENT", 9253));
            var result = _subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Select(item => item.AniListId).Should().Equal(1, 9253);
            result.Series.Should().OnlyContain(item => item.ImportListId == 10);
            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.Is<HttpRequest>(request =>
                request.Url.Host == "graphql.anilist.co" && request.Headers["Authorization"].ToString() == "Bearer test-access-token" &&
                request.RateLimit >= TimeSpan.FromSeconds(2) && !request.AllowAutoRedirect &&
                Encoding.UTF8.GetString(request.ContentData).Contains("MediaListCollection") &&
                !Encoding.UTF8.GetString(request.ContentData).Contains("status_in"))),
                Times.Exactly(2));
            ImportListSnapshotScope.Current.Should().BeNull();
        }

        [Test]
        public void should_keep_distinct_native_ids_with_the_same_title()
        {
            GivenResponses(Collection(false, true, false, "CURRENT", 1, 9253));
            var result = _subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Select(item => item.AniListId).Should().Equal(1, 9253);
            result.Series.Select(item => item.MalId).Should().Equal(200001, 209253);
        }

        [Test]
        public void should_apply_status_filters_locally_after_fetching_all_chunks()
        {
            ((AniListSettings)_subject.Definition.Settings).ImportCompleted = false;
            GivenResponses(Collection(true, true, false, "COMPLETED", 1), Collection(false, true, false, "CURRENT", 9253));
            _subject.Fetch().Series.Should().ContainSingle().Which.AniListId.Should().Be(9253);
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void should_keep_custom_only_entries_hidden_from_status_groups()
        {
            GivenCustomExclusions();
            var resource = JsonNode.Parse(Collection(false, true, false, "CURRENT", 1));
            resource["data"]["MediaListCollection"]["lists"][0]["entries"] = new JsonArray();
            GivenResponses(resource.ToJsonString());
            _subject.Fetch().Series.Should().ContainSingle().Which.AniListId.Should().Be(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_subtract_custom_exclusions_across_chunks_regardless_of_order(bool excludedFirst)
        {
            GivenCustomExclusions();
            GivenResponses(Collection(true, !excludedFirst, excludedFirst, "CURRENT", 1), Collection(false, excludedFirst, !excludedFirst, "CURRENT", 1));
            var result = _subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Should().BeEmpty();
        }

        [Test]
        public void should_continue_after_a_chunk_filtered_out_of_the_selected_custom_list()
        {
            ((AniListSettings)_subject.Definition.Settings).CustomList = "Seasonal";
            GivenResponses(Collection(true, false, false, "CURRENT", 1), Collection(false, true, false, "CURRENT", 9253));
            _subject.Fetch().Series.Should().ContainSingle().Which.AniListId.Should().Be(9253);
        }

        [Test]
        public void should_retain_previous_membership_when_a_later_chunk_is_incomplete()
        {
            GivenResponses(Collection(true, true, false, "CURRENT", 1), "{\"data\":null,\"errors\":[{\"message\":\"PRIVATE-FIXTURE\"}]}");
            var result = _subject.Fetch();
            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(10), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
            ImportListSnapshotScope.Current.Should().BeNull();
        }

        [Test]
        public void should_reject_a_repeated_chunk_without_looping()
        {
            GivenResponses(Collection(true, true, false, "CURRENT", 1), Collection(true, true, false, "CURRENT", 1));
            _subject.Fetch().AnyFailure.Should().BeTrue();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Exactly(2));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_stop_at_the_bounded_collection_chunk_limit()
        {
            GivenResponses(Enumerable.Range(1, AniListAPI.MaxCollectionChunks).Select(chunk => Collection(true, true, false, "CURRENT", chunk)).ToArray());
            _subject.Fetch().AnyFailure.Should().BeTrue();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Exactly(AniListAPI.MaxCollectionChunks));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_record_shared_rate_limit_cooldown_without_returning_partial_items()
        {
            GivenRateLimit(180);
            var result = _subject.Fetch();
            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordFailure(10, TimeSpan.FromSeconds(180)), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(180, "in at least 180 seconds")]
        [TestCase(0, "later")]
        public void connection_test_should_report_rate_limit_without_private_body_or_credentials(int seconds, string retryMessage)
        {
            GivenRateLimit(seconds);
            var failure = _subject.Test().Errors.Should().ContainSingle().Which;
            failure.ErrorMessage.Should().Contain("AniList request limit reached").And.Contain(retryMessage);
            failure.ErrorMessage.Should().NotContain("test-access-token").And.NotContain("PRIVATE-FIXTURE");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void connection_test_should_fetch_every_chunk_before_deciding_whether_filters_match()
        {
            GivenCustomExclusions();
            GivenResponses(Collection(true, true, false, "CURRENT", 1), Collection(false, false, true, "CURRENT", 1));
            _subject.Test().Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("no entries match");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void connection_test_should_fail_incomplete_later_chunks_even_after_an_early_match()
        {
            GivenResponses(Collection(true, true, false, "CURRENT", 1), "{\"data\":null}");
            _subject.Test().Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("previous list will be retained");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_share_one_account_snapshot_across_different_filters_in_one_sync_scope()
        {
            GivenResponses(Collection(false, true, false, "CURRENT", 1));
            using var scope = ImportListSnapshotScope.Begin();
            _subject.Fetch().Series.Should().ContainSingle();
            ((AniListSettings)_subject.Definition.Settings).ExcludedCustomLists = new[] { "Seasonal" };
            _subject.Fetch().Series.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Once());
            scope.GetCompletedValues<AniListPublicRelationSnapshot>().Should().ContainSingle();
        }

        [Test]
        public void should_isolate_changed_auth_tokens_and_new_sync_scopes()
        {
            GivenResponses(Collection(false, true, false, "CURRENT", 1), Collection(false, true, false, "CURRENT", 2), Collection(false, true, false, "CURRENT", 3));
            using (ImportListSnapshotScope.Begin())
            {
                _subject.Fetch().Series.Single().AniListId.Should().Be(1);
                ((AniListSettings)_subject.Definition.Settings).AccessToken = "different-generated-token";
                _subject.Fetch().Series.Single().AniListId.Should().Be(2);
            }

            _subject.Fetch().Series.Single().AniListId.Should().Be(3);
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Exactly(3));
        }

        [Test]
        public void every_unsaved_test_should_get_a_fresh_isolated_snapshot()
        {
            GivenResponses(Collection(false, true, false, "CURRENT", 1), Collection(false, false, true, "CURRENT", 2), Collection(false, true, false, "CURRENT", 3));
            using var scope = ImportListSnapshotScope.Begin();
            _subject.Fetch().Series.Single().AniListId.Should().Be(1);
            GivenCustomExclusions();
            _subject.Test().Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("no entries match");
            _subject.Test().IsValid.Should().BeTrue();
            scope.GetCompletedValues<AniListAccountSnapshot>().Should().ContainSingle();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Exactly(3));
        }
    }
}
