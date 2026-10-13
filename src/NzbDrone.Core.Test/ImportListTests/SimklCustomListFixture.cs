using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.ImportLists.Simkl;
using NzbDrone.Core.ImportLists.Simkl.User;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class SimklCustomListFixture : CoreTest<SimklCustomListReader>
    {
        private readonly List<HttpRequest> _requests = new();
        private SimklUserSettings _settings;
        private SimklV2WatchlistSnapshot _snapshot;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _settings = new SimklUserSettings { ClientIdV2 = "generated-client", AccessToken = "generated-access", AuthUser = "91", ShowType = (int)SimklUserShowType.Anime };
            _snapshot = new SimklV2WatchlistSnapshot
            {
                Rows = new List<SimklSeriesResource> { Row(1), Row(2), Row(3), Row(4, "completed"), Row(5, "watching", SimklAnimeType.Movie) }
            };
            Mocker.GetMock<ISimklV2WatchlistService>().Setup(service => service.Read(_settings)).Returns(_snapshot);
        }

        private static SimklSeriesResource Row(int id, string status = "watching", SimklAnimeType type = SimklAnimeType.Tv)
        {
            return new SimklSeriesResource { Status = status, AnimeType = type, Show = new SimklSeriesPropsResource { Title = "Shared title", Ids = new SimklSeriesIdsResource { Simkl = id, Mal = (100 + id).ToString() } } };
        }

        private static JObject Page(int id, IEnumerable<int> members, int page = 1, int? total = null)
        {
            var values = members.ToArray();
            var count = total ?? values.Length;
            return new JObject
            {
                ["id"] = id,
                ["name"] = "List, with literal spaces",
                ["media_type"] = "anime",
                ["type"] = "private",
                ["user"] = new JObject { ["id"] = 91 },
                ["counts"] = new JObject { ["items"] = count },
                ["pagination"] = new JObject { ["page"] = page, ["limit"] = 500, ["total_items"] = count, ["total_pages"] = (count + 499) / 500 },
                ["items"] = new JArray(values.Select(value => new JObject { ["type"] = "anime", ["ids"] = new JObject { ["simkl_id"] = value } }))
            };
        }

        private void GivenResponses(params JObject[] bodies)
        {
            var queue = new Queue<JObject>(bodies);
            Mocker.GetMock<ISimklV2RequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);
                    return new HttpResponse(request, new HttpHeader(), queue.Dequeue().ToString(), HttpStatusCode.OK);
                });
        }

        [Test]
        public void should_intersect_the_account_bucket_with_union_inclusions_then_subtract_all_exclusions()
        {
            _settings.IncludedCustomListIds = new[] { "71", "72" };
            _settings.ExcludedCustomListIds = new[] { "73" };
            GivenResponses(Page(71, new[] { 1, 3, 4, 5, 99 }), Page(72, new[] { 2 }), Page(73, new[] { 3 }));
            var result = Subject.Read(_settings);
            result.Items.Select(item => item.MalId).Should().Equal(101, 102);
            result.WatchlistSnapshot.Should().BeSameAs(_snapshot);
            _requests.Should().HaveCount(3);
            _requests.Should().OnlyContain(request => request.Url.Query.Contains("client_id=generated-client") && request.Headers["Authorization"] == "Bearer generated-access");
            Mocker.GetMock<ISimklV2WatchlistService>().Verify(service => service.Commit(It.IsAny<SimklUserSettings>(), It.IsAny<SimklV2WatchlistSnapshot>()), Times.Never());
            Subject.Commit(_settings, result);
            Mocker.GetMock<ISimklV2WatchlistService>().Verify(service => service.Commit(_settings, _snapshot), Times.Once());
        }

        [Test]
        public void should_treat_empty_inclusions_as_the_whole_bucket_and_shared_include_exclude_as_veto()
        {
            _settings.ExcludedCustomListIds = new[] { "71" };
            GivenResponses(Page(71, new[] { 2 }));
            Subject.Read(_settings).Items.Select(item => item.MalId).Should().Equal(101, 103);
            _settings.IncludedCustomListIds = new[] { "71" };
            GivenResponses(Page(71, new[] { 1, 2 }));
            Subject.Read(_settings).Items.Should().BeEmpty();
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_read_all_pages_even_when_the_first_page_has_no_account_members()
        {
            _settings.IncludedCustomListIds = new[] { "71" };
            GivenResponses(Page(71, Enumerable.Range(1000, 500), 1, 501), Page(71, new[] { 2 }, 2, 501));
            Subject.Read(_settings).Items.Should().ContainSingle().Which.MalId.Should().Be(102);
            _requests.Last().Url.Query.Should().Contain("page=2");
        }

        [TestCase(0)]
        [TestCase(1)]
        public void should_accept_an_authoritative_empty_selected_custom_list(int totalPages)
        {
            _settings.IncludedCustomListIds = new[] { "71" };
            var page = Page(71, Array.Empty<int>());
            page["pagination"]["total_pages"] = totalPages;
            GivenResponses(page);
            Subject.Read(_settings).Items.Should().BeEmpty();
        }

        [TestCase("identity")]
        [TestCase("media")]
        [TestCase("owner")]
        [TestCase("count")]
        [TestCase("page")]
        [TestCase("missing-total")]
        [TestCase("null-items")]
        [TestCase("duplicate")]
        [TestCase("wrong-member-type")]
        [TestCase("missing-native-id")]
        [TestCase("premium")]
        [TestCase("unsafe-total")]
        public void should_reject_unknown_or_incomplete_membership_without_committing_inventory(string failure)
        {
            _settings.IncludedCustomListIds = new[] { "71" };
            var page = Page(71, new[] { 1 });
            switch (failure)
            {
                case "identity":
                    page["id"] = 72;
                    break;
                case "media":
                    page["media_type"] = "tv";
                    break;
                case "owner":
                    page["user"] = null;
                    break;
                case "count":
                    page["counts"]["items"] = 2;
                    break;
                case "page":
                    page["pagination"]["page"] = 2;
                    break;
                case "missing-total":
                    ((JObject)page["pagination"]).Remove("total_items");
                    break;
                case "null-items":
                    page["items"] = null;
                    break;
                case "duplicate":
                    page = Page(71, new[] { 1, 1 });
                    break;
                case "wrong-member-type":
                    page["items"][0]["type"] = "movie";
                    break;
                case "missing-native-id":
                    page["items"][0]["ids"] = new JObject { ["simkl"] = 1 };
                    break;
                case "premium":
                    page = new JObject { ["error"] = "premium_only", ["item"] = new JObject { ["id"] = 1 } };
                    break;
                case "unsafe-total":
                    page["pagination"]["total_items"] = 10001;
                    break;
            }

            GivenResponses(page);
            Action act = () => Subject.Read(_settings);
            act.Should().Throw<ImportListException>().WithMessage("*previous complete list*");
            Mocker.GetMock<ISimklV2WatchlistService>().Verify(service => service.Commit(It.IsAny<SimklUserSettings>(), It.IsAny<SimklV2WatchlistSnapshot>()), Times.Never());
        }

        [TestCase("owner")]
        [TestCase("name")]
        [TestCase("total")]
        [TestCase("duplicate")]
        public void should_reject_membership_changed_between_pages(string change)
        {
            _settings.IncludedCustomListIds = new[] { "71" };
            var second = Page(71, new[] { 2 }, 2, 501);
            switch (change)
            {
                case "owner":
                    second["user"]["id"] = 92;
                    break;
                case "name":
                    second["name"] = "Changed";
                    break;
                case "total":
                    second["pagination"]["total_items"] = 502;
                    break;
                case "duplicate":
                    second["items"][0]["ids"]["simkl_id"] = 1000;
                    break;
            }

            GivenResponses(Page(71, Enumerable.Range(1000, 500), 1, 501), second);
            Action act = () => Subject.Read(_settings);
            act.Should().Throw<ImportListException>();
        }

        [Test]
        public void should_keep_list_choice_names_literal_and_filter_the_correct_media_namespace()
        {
            var anime = Page(71, Array.Empty<int>());
            var movie = Page(72, Array.Empty<int>());
            movie["media_type"] = "movies";
            GivenResponses(new JObject
            {
                ["pagination"] = new JObject { ["page"] = 1, ["limit"] = 500, ["total_items"] = 2, ["total_pages"] = 1 },
                ["lists"] = new JArray(anime, movie)
            });
            var result = Subject.GetOptions(_settings);
            result.Should().ContainSingle();
            result.Single().Value.Should().Be("71");
            result.Single().Name.Should().Be("List, with literal spaces");
            Mocker.GetMock<ISimklV2WatchlistService>().Verify(service => service.Read(It.IsAny<SimklUserSettings>()), Times.Never());
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("71,72")]
        [TestCase(" 71")]
        public void should_validate_positive_literal_list_ids(string value)
        {
            _settings.IncludedCustomListIds = new[] { value };
            _settings.Validate().IsValid.Should().BeFalse();
        }
    }
}
