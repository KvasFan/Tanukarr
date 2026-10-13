using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.ImportLists.MDBList;
using NzbDrone.Core.ImportLists.Tmdb.List;
using NzbDrone.Core.ImportLists.Trakt.List;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ProviderListSetFixture : CoreTest
    {
        private readonly List<HttpRequest> _requests = new();

        [SetUp]
        public void Setup() => _requests.Clear();

        private IImportList Subject(string provider, bool multiple = true)
        {
            IImportList subject;
            IImportListSettings settings;
            if (provider == "TMDB")
            {
                subject = Mocker.Resolve<TmdbListImport>();
                settings = new TmdbListSettings
                {
                    AuthToken = "fixture-token",
                    AccountId = "fixture-account",
                    IncludedListIds = multiple ? new[] { "1", "2" } : new[] { "1" },
                    ExcludedListIds = multiple ? new[] { "3" } : Array.Empty<string>()
                };
            }
            else if (provider == "Trakt")
            {
                subject = Mocker.Resolve<TraktListImport>();
                settings = new TraktListSettings
                {
                    Username = "fixture-user",
                    AccessToken = "fixture-token",
                    RefreshToken = "fixture-refresh",
                    Expires = DateTime.UtcNow.AddDays(30),
                    IncludedLists = multiple ? new[] { "1", "2" } : new[] { "1" },
                    ExcludedLists = multiple ? new[] { "3" } : Array.Empty<string>()
                };
            }
            else
            {
                subject = Mocker.Resolve<MDBListImport>();
                settings = new MDBListSettings
                {
                    ApiKey = "fixture-key",
                    IncludedListUrls = multiple ? new[] { Url("1"), Url("2") } : new[] { Url("1") },
                    ExcludedListUrls = multiple ? new[] { Url("3") } : Array.Empty<string>()
                };
            }

            subject.Definition = new ImportListDefinition { Id = 42, Name = "Fixture", Settings = settings };
            return subject;
        }

        private static string Url(string source) => "https://mdblist.com/lists/fixture/" + source;

        private static JObject Item(string provider, int id, bool movie = false)
        {
            if (provider == "TMDB")
            {
                return new JObject { ["id"] = id, ["name"] = "Same title", ["media_type"] = movie ? "movie" : "tv" };
            }

            if (provider == "Trakt")
            {
                return new JObject
                {
                    ["type"] = movie ? "movie" : "show",
                    [movie ? "movie" : "show"] = new JObject { ["title"] = "Same title", ["ids"] = new JObject { ["tmdb"] = id } }
                };
            }

            return new JObject { ["id"] = id, ["title"] = "Same title", ["mediatype"] = movie ? "movie" : "show" };
        }

        private static HttpResponse Page(HttpRequest request, string provider, JArray items, int page = 1, int pages = 1, int? total = null)
        {
            var headers = new HttpHeader();
            JToken body;
            if (provider == "TMDB")
            {
                body = new JObject { ["results"] = items, ["page"] = page, ["total_pages"] = pages, ["total_results"] = total ?? items.Count };
            }
            else if (provider == "Trakt")
            {
                headers["X-Pagination-Page"] = page.ToString();
                headers["X-Pagination-Page-Count"] = pages.ToString();
                headers["X-Pagination-Item-Count"] = (total ?? items.Count).ToString();
                headers["X-Pagination-Limit"] = "1";
                body = items;
            }
            else
            {
                body = new JObject
                {
                    ["shows"] = items,
                    ["pagination"] = new JObject { ["has_more"] = page < pages, ["next_cursor"] = page < pages ? "cursor-" + (page + 1) : null }
                };
            }

            return new HttpResponse(request, headers, Encoding.UTF8.GetBytes(body.ToString()));
        }

        private void Respond(Func<HttpRequest, HttpResponse> response)
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Execute(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
            {
                _requests.Add(request);
                return response(request);
            });
        }

        private static string Source(HttpRequest request)
        {
            var segments = new Uri(request.Url.FullUri).AbsolutePath.Split('/');
            return segments.Contains("items") ? segments[Array.IndexOf(segments, "items") - 1] : segments.Last();
        }

        [TestCase("TMDB")]
        [TestCase("Trakt")]
        [TestCase("MDBList")]
        public void should_union_includes_subtract_excludes_and_preserve_same_title_different_ids(string provider)
        {
            var subject = Subject(provider);
            Respond(request => Page(request, provider, new JArray(Source(request) switch
            {
                "1" => new[] { Item(provider, 101), Item(provider, 102) },
                "2" => new[] { Item(provider, 102), Item(provider, 103) },
                _ => new[] { Item(provider, 102), Item(provider, 999) }
            })));

            var result = subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Series.Select(item => item.TmdbId).Should().Equal(101, 103);
            result.Series.Should().OnlyContain(item => item.ImportListId == 42 && item.ImportList == "Fixture");
            _requests.Count.Should().Be(3);
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(42), Times.Once());
        }

        [TestCase("TMDB")]
        [TestCase("Trakt")]
        [TestCase("MDBList")]
        public void should_fail_the_complete_fetch_when_an_excluded_source_is_unavailable(string provider)
        {
            var subject = Subject(provider);
            Respond(request => Source(request) == "3"
                ? new HttpResponse(request, new HttpHeader(), "{}", HttpStatusCode.Forbidden)
                : Page(request, provider, new JArray(Item(provider, 101))));

            var result = subject.Fetch();

            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(42), Times.Never());
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordFailure(42), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("TMDB")]
        [TestCase("Trakt")]
        [TestCase("MDBList")]
        public void test_should_evaluate_unsaved_sources_without_writing_list_status(string provider)
        {
            var subject = Subject(provider);
            Respond(request => Page(request, provider, new JArray(Item(provider, Source(request) == "3" ? 102 : 101))));

            subject.Test().IsValid.Should().BeTrue();

            _requests.Count.Should().Be(3);
            Mocker.GetMock<IImportListStatusService>().VerifyNoOtherCalls();
        }

        [TestCase("TMDB")]
        [TestCase("Trakt")]
        [TestCase("MDBList")]
        public void should_keep_tv_identity_separate_from_same_numeric_movie_id(string provider)
        {
            var subject = Subject(provider);
            Respond(request => Page(request, provider, new JArray(Item(provider, 101, Source(request) == "3"))));

            var result = subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Series.Should().ContainSingle().Which.TmdbId.Should().Be(101);
        }

        [TestCase("TMDB")]
        [TestCase("Trakt")]
        [TestCase("MDBList")]
        public void should_fetch_beyond_old_caps_and_continue_after_movie_only_pages(string provider)
        {
            var subject = Subject(provider, false);
            var page = 0;
            Respond(request => Page(request, provider, new JArray(Item(provider, ++page, page < 11)), page, 11, 11));

            var result = subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Series.Should().ContainSingle().Which.TmdbId.Should().Be(11);
            _requests.Count.Should().Be(11);
            if (provider == "MDBList")
            {
                _requests.Last().Url.FullUri.Should().Contain("cursor=cursor-11");
            }
        }

        [TestCase("TMDB")]
        [TestCase("Trakt")]
        [TestCase("MDBList")]
        public void should_reject_malformed_later_pages_instead_of_returning_a_partial_list(string provider)
        {
            var subject = Subject(provider, false);
            Respond(request => _requests.Count == 1
                ? Page(request, provider, new JArray(Item(provider, 101)), 1, 2, 2)
                : new HttpResponse(request, new HttpHeader(), "{\"error\":\"unavailable\"}"));

            var result = subject.Fetch();

            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void trakt_should_apply_filters_only_to_includes_and_apply_limit_after_exclusions()
        {
            var subject = Subject("Trakt");
            var settings = (TraktListSettings)subject.Definition.Settings;
            settings.Limit = 1;
            settings.Rating = "50-100";
            settings.Genres = "anime";
            settings.Years = "2020-2026";
            settings.TraktAdditionalParameters = "languages=en";
            Respond(request => Page(request, "Trakt", new JArray(Source(request) == "3" ? new[] { Item("Trakt", 101) } : new[] { Item("Trakt", 101), Item("Trakt", 102) })));

            var result = subject.Fetch();

            result.Series.Should().ContainSingle().Which.TmdbId.Should().Be(102);
            _requests.Take(2).Should().OnlyContain(request => request.Url.FullUri.Contains("ratings=50-100") && request.Url.FullUri.Contains("languages=en"));
            _requests.Last().Url.FullUri.Should().NotContain("ratings=").And.NotContain("genres=").And.NotContain("years=").And.NotContain("languages=");
        }

        [Test]
        public void mdblist_should_follow_legacy_offsets_by_raw_count_until_an_empty_page()
        {
            var subject = Subject("MDBList", false);
            Respond(request => new HttpResponse(request, new HttpHeader(), new JArray(_requests.Count switch
            {
                1 => new[] { Item("MDBList", 7, true) },
                2 => new[] { Item("MDBList", 101) },
                _ => Array.Empty<JObject>()
            }).ToString()));

            var result = subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Series.Should().ContainSingle().Which.TmdbId.Should().Be(101);
            _requests.Count.Should().Be(3);
            _requests[1].Url.FullUri.Should().Contain("offset=1");
            _requests[2].Url.FullUri.Should().Contain("offset=2");
            _requests.Should().OnlyContain(request => request.Url.Host == "api.mdblist.com");
        }

        [Test]
        public void mdblist_should_reject_a_repeated_cursor()
        {
            var subject = Subject("MDBList", false);
            Respond(request => Page(request, "MDBList", new JArray(Item("MDBList", 101)), 1, 2));

            subject.Fetch().AnyFailure.Should().BeTrue();

            _requests.Count.Should().Be(2);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void trakt_should_reject_missing_authoritative_pagination_headers()
        {
            var subject = Subject("Trakt", false);
            Respond(request => new HttpResponse(request, new HttpHeader(), new JArray(Item("Trakt", 101)).ToString()));

            subject.Fetch().AnyFailure.Should().BeTrue();

            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("TMDB")]
        [TestCase("Trakt")]
        public void should_load_all_named_choices_beyond_previous_three_page_cap(string provider)
        {
            var subject = Subject(provider, false);
            var page = 0;
            Respond(request =>
            {
                var current = ++page;
                var rows = new JArray(new JObject
                {
                    ["id"] = current,
                    ["name"] = "List " + current,
                    ["ids"] = new JObject { ["slug"] = "list-" + current }
                });
                return Page(request, provider, rows, current, 4, 4);
            });

            var response = JObject.FromObject(subject.RequestAction(provider == "TMDB" ? "getAccountLists" : "getLists", new Dictionary<string, string>()));

            response["options"].Count().Should().Be(4);
            response["options"].Select(option => (string)option["Value"]).Should().Equal(provider == "TMDB" ? new[] { "1", "2", "3", "4" } : new[] { "list-1", "list-2", "list-3", "list-4" });
            _requests.Count.Should().Be(4);
            Mocker.GetMock<IImportListStatusService>().VerifyNoOtherCalls();
        }

        [TestCase("TMDB")]
        [TestCase("Trakt")]
        [TestCase("MDBList")]
        public void should_fail_when_an_excluded_tv_entry_has_no_supported_identity(string provider)
        {
            var subject = Subject(provider);
            Respond(request => Page(request, provider, new JArray(Item(provider, Source(request) == "3" ? 0 : 101))));

            var result = subject.Fetch();

            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_subtract_aliases_connected_by_a_later_included_row()
        {
            var subject = Subject("Trakt");
            Respond(request =>
            {
                var first = Item("Trakt", 101);
                var bridge = Item("Trakt", 101);
                bridge["show"]["ids"]["tvdb"] = 901;
                var excluded = Item("Trakt", 0);
                excluded["show"]["ids"]["tvdb"] = 901;
                return Page(request, "Trakt", new JArray(Source(request) switch
                {
                    "1" => new[] { first, Item("Trakt", 200) },
                    "2" => new[] { bridge },
                    _ => new[] { excluded }
                }));
            });

            var result = subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Series.Should().ContainSingle().Which.TmdbId.Should().Be(200);
        }

        [Test]
        public void trakt_should_accept_the_documented_unpaged_personal_names_response()
        {
            var subject = Subject("Trakt", false);
            Respond(request => new HttpResponse(request, new HttpHeader(), "[{\"name\":\"Legacy names\",\"ids\":{\"trakt\":123,\"slug\":\"legacy-names\"}}]"));

            var response = JObject.FromObject(subject.RequestAction("getLists", new Dictionary<string, string>()));

            response["options"].Should().ContainSingle();
            response["options"][0]["Value"].Value<string>().Should().Be("legacy-names");
            _requests.Should().ContainSingle();
        }

        [Test]
        public void trakt_should_reject_partially_missing_headers_for_named_choices()
        {
            var subject = Subject("Trakt", false);
            Respond(request => new HttpResponse(request, new HttpHeader { ["X-Pagination-Page"] = "1" }, "[{\"name\":\"List\",\"ids\":{\"slug\":\"list\"}}]"));

            Action action = () => subject.RequestAction("getLists", new Dictionary<string, string>());

            action.Should().Throw<ImportListException>();
        }

        [Test]
        public void mdblist_should_reject_an_empty_pagination_object_without_completion_proof()
        {
            var subject = Subject("MDBList", false);
            Respond(request => new HttpResponse(request, new HttpHeader(), "{\"shows\":[{\"id\":101,\"mediatype\":\"show\"}],\"pagination\":{}}"));

            var result = subject.Fetch();

            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
