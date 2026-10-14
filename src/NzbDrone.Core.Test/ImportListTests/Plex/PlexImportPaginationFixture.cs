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
using NzbDrone.Core.ImportLists.Plex;
using NzbDrone.Core.Notifications.Plex.PlexTv;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests.Plex
{
    [TestFixture]
    public class PlexImportPaginationFixture : CoreTest<PlexImport>
    {
        private readonly List<int> _offsets = new();
        private readonly List<HttpRequest> _requests = new();

        [SetUp]
        public void Setup()
        {
            _offsets.Clear();
            _requests.Clear();
            Subject.Definition = new ImportListDefinition
            {
                Id = 12,
                Name = "Generated Plex Watchlist",
                Settings = new PlexListSettings { AccessToken = "generated-plex-token" }
            };
            Mocker.GetMock<IPlexTvService>()
                  .Setup(service => service.GetWatchlist(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                  .Returns((string token, int pageSize, int offset) =>
                  {
                      _offsets.Add(offset);
                      pageSize.Should().Be(100);
                      return new HttpRequest($"https://plex-fixture.invalid/watchlist/{offset}");
                  });
        }

        private static JObject Page(int offset, int size, int? total)
        {
            var container = new JObject
            {
                ["offset"] = offset,
                ["size"] = size,
                ["Metadata"] = new JArray(Enumerable.Range(offset + 1, size).Select(id => new JObject
                {
                    ["type"] = "show",
                    ["title"] = "Generated show " + id,
                    ["Guid"] = new JArray(new JObject { ["id"] = "tvdb://" + id })
                }))
            };
            if (total.HasValue)
            {
                container["totalSize"] = total.Value;
            }

            return new JObject { ["MediaContainer"] = container };
        }

        private void GivenPages(params JObject[] pages)
        {
            var queue = new Queue<JObject>(pages);
            GivenResponse(_ => queue.Dequeue());
        }

        private void GivenResponse(Func<HttpRequest, JObject> body, HttpHeader headers = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);
                    return new HttpResponse(request, headers ?? new HttpHeader(), Encoding.UTF8.GetBytes(body(request).ToString()), status);
                });
        }

        private void AssertFailure()
        {
            var result = Subject.Fetch();
            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(12), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_fetch_a_complete_watchlist_past_the_inherited_thousand_item_limit()
        {
            GivenResponse(request =>
            {
                var offset = int.Parse(request.Url.FullUri.Split('/').Last());
                return Page(offset, Math.Min(100, 1001 - offset), 1001);
            });

            var result = Subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Should().HaveCount(1001);
            result.Series.Last().TvdbId.Should().Be(1001);
            result.Series.Should().OnlyContain(item => item.ImportListId == 12);
            _offsets.Should().Equal(Enumerable.Range(0, 11).Select(index => index * 100));
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(12), Times.Once());
        }

        [Test]
        public void should_follow_actual_raw_size_when_the_service_caps_requested_pages()
        {
            GivenPages(Page(0, 1, 2), Page(1, 1, 2));
            Subject.Fetch().Series.Select(item => item.TvdbId).Should().Equal(1, 2);
            _offsets.Should().Equal(0, 1);
        }

        [Test]
        public void should_not_treat_filtered_non_tv_rows_as_an_empty_raw_page()
        {
            var moviePage = Page(0, 1, 2);
            moviePage["MediaContainer"]["Metadata"][0]["type"] = "movie";
            GivenPages(moviePage, Page(1, 1, 2));
            var result = Subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Should().ContainSingle().Which.TvdbId.Should().Be(2);
            _offsets.Should().Equal(0, 1);
        }

        [Test]
        public void should_require_a_terminal_empty_raw_page_when_total_size_is_absent()
        {
            GivenPages(Page(0, 1, null), Page(1, 0, null));
            var result = Subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Should().ContainSingle();
            _offsets.Should().Equal(0, 1);
        }

        [Test]
        public void should_accept_matching_headers_and_a_header_only_total()
        {
            GivenResponse(_ => Page(0, 1, null), new HttpHeader
            {
                { "X-Plex-Container-Start", "0" },
                { "X-Plex-Container-Total-Size", "1" }
            });
            Subject.Fetch().AnyFailure.Should().BeFalse();
            _offsets.Should().Equal(0);
        }

        [Test]
        public void should_accept_a_verified_empty_container_without_metadata_array()
        {
            var empty = Page(0, 0, 0);
            ((JObject)empty["MediaContainer"]).Remove("Metadata");
            GivenPages(empty);
            var result = Subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Should().BeEmpty();
        }

        [TestCase("missingOffset")]
        [TestCase("wrongOffset")]
        [TestCase("wrongSize")]
        [TestCase("negativeSize")]
        [TestCase("stringSize")]
        [TestCase("nullTotal")]
        [TestCase("tooSmallTotal")]
        [TestCase("missingMetadata")]
        [TestCase("nullMetadata")]
        [TestCase("missingType")]
        [TestCase("missingIdentity")]
        [TestCase("negativeIdentity")]
        [TestCase("invalidImdbIdentity")]
        public void should_reject_unconfirmed_or_malformed_watchlist_data(string kind)
        {
            var page = Page(0, 1, 1);
            var container = (JObject)page["MediaContainer"];
            var row = (JObject)container["Metadata"][0];
            switch (kind)
            {
                case "missingOffset":
                    container.Remove("offset");
                    break;
                case "wrongOffset":
                    container["offset"] = 1;
                    break;
                case "wrongSize":
                    container["size"] = 2;
                    break;
                case "negativeSize":
                    container["size"] = -1;
                    break;
                case "stringSize":
                    container["size"] = "1";
                    break;
                case "nullTotal":
                    container["totalSize"] = JValue.CreateNull();
                    break;
                case "tooSmallTotal":
                    container["totalSize"] = 0;
                    break;
                case "missingMetadata":
                    container.Remove("Metadata");
                    break;
                case "nullMetadata":
                    container["Metadata"] = JValue.CreateNull();
                    break;
                case "missingType":
                    row.Remove("type");
                    break;
                case "missingIdentity":
                    row["Guid"] = new JArray();
                    break;
                case "negativeIdentity":
                    row["Guid"] = new JArray(new JObject { ["id"] = "tvdb://-1" });
                    break;
                case "invalidImdbIdentity":
                    row["Guid"] = new JArray(new JObject { ["id"] = "imdb://unknown" });
                    break;
            }

            GivenPages(page);
            AssertFailure();
        }

        [TestCase("X-Plex-Container-Start", "1")]
        [TestCase("X-Plex-Container-Total-Size", "2")]
        [TestCase("X-Plex-Container-Start", "invalid")]
        [TestCase("X-Plex-Container-Total-Size", "-1")]
        public void should_reject_conflicting_or_invalid_pagination_headers(string header, string value)
        {
            GivenResponse(_ => Page(0, 1, 1), new HttpHeader { { header, value } });
            AssertFailure();
        }

        [Test]
        public void should_reject_duplicate_pagination_headers()
        {
            GivenResponse(_ => Page(0, 1, 1), new HttpHeader
            {
                { "X-Plex-Container-Start", "0" },
                { "X-Plex-Container-Start", "0" }
            });
            AssertFailure();
        }

        [Test]
        public void should_discard_prepared_rows_if_the_declared_total_changes()
        {
            GivenPages(Page(0, 1, 2), Page(1, 1, 3));
            AssertFailure();
        }

        [Test]
        public void should_reject_a_premature_empty_page()
        {
            GivenPages(Page(0, 1, 2), Page(1, 0, 2));
            AssertFailure();
        }

        [Test]
        public void should_reject_repeated_offsets_instead_of_replacing_with_a_partial_list()
        {
            GivenPages(Page(0, 1, 2), Page(0, 1, 2));
            AssertFailure();
        }

        [Test]
        public void should_fail_closed_if_no_completion_proof_arrives_before_the_page_bound()
        {
            GivenResponse(request => Page(int.Parse(request.Url.FullUri.Split('/').Last()), 1, null));
            AssertFailure();
            _offsets.Should().HaveCount(PlexListRequestGenerator.MaxPages);
        }

        [Test]
        public void should_read_every_test_page_without_recording_or_saving_status()
        {
            GivenPages(Page(0, 1, 2), Page(1, 1, 2));
            Subject.Test().Errors.Should().BeEmpty();
            _offsets.Should().Equal(0, 1);
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(It.IsAny<int>()), Times.Never());
            ((PlexListSettings)Subject.Definition.Settings).AccessToken.Should().Be("generated-plex-token");
        }

        [Test]
        public void should_fail_test_when_a_later_page_is_unconfirmed()
        {
            GivenPages(Page(0, 1, 2), Page(0, 1, 2));
            Subject.Test().Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("previous list will be retained");
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordFailure(It.IsAny<int>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_suppress_private_http_error_and_response_content_logging()
        {
            GivenPages(Page(0, 1, 1));
            Subject.Fetch().AnyFailure.Should().BeFalse();
            _requests.Single().LogHttpError.Should().BeFalse();
            _requests.Single().LogResponseContent.Should().BeFalse();
            _requests.Single().AllowAutoRedirect.Should().BeFalse();
            _requests.Single().RateLimit.Should().Be(TimeSpan.FromSeconds(5));
        }

        [Test]
        public void should_preserve_the_server_retry_after_cooldown()
        {
            GivenResponse(_ => Page(0, 0, 0), new HttpHeader { { "Retry-After", "60" } }, HttpStatusCode.TooManyRequests);
            AssertFailure();
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordFailure(12, TimeSpan.FromSeconds(60)), Times.Once());
        }

        [Test]
        public void should_reuse_one_complete_watchlist_then_release_it_for_the_next_import()
        {
            GivenPages(Page(0, 1, 1), Page(0, 2, 2));
            using (ImportListSnapshotScope.Begin())
            {
                var first = Subject.Fetch();
                Subject.Definition.Id = 13;
                var second = Subject.Fetch();
                _requests.Should().ContainSingle();
                _offsets.Should().Equal(0);
                first.Series.Single().ImportListId.Should().Be(12);
                second.Series.Single().ImportListId.Should().Be(13);
                second.Series.Single().Should().NotBeSameAs(first.Series.Single());
            }

            Subject.Fetch().Series.Should().HaveCount(2);
            _requests.Should().HaveCount(2);
            ImportListSnapshotScope.Current.Should().BeNull();
        }

        [Test]
        public void should_not_reuse_a_watchlist_after_its_authenticated_account_changes()
        {
            using var scope = ImportListSnapshotScope.Begin();
            GivenPages(Page(0, 1, 1), Page(0, 2, 2));
            Subject.Fetch().Series.Should().ContainSingle();
            ((PlexListSettings)Subject.Definition.Settings).AccessToken = "another-generated-token";
            Subject.Fetch().Series.Should().HaveCount(2);
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_load_test_data_in_a_fresh_scope_without_replacing_the_import_snapshot()
        {
            using var scope = ImportListSnapshotScope.Begin();
            GivenPages(Page(0, 1, 1), Page(0, 2, 2));
            Subject.Fetch().Series.Should().ContainSingle();
            Subject.Test().IsValid.Should().BeTrue();
            _requests.Should().HaveCount(2);
            Subject.Fetch().Series.Should().ContainSingle();
            _requests.Should().HaveCount(2);
            ImportListSnapshotScope.Current.Should().BeSameAs(scope);
        }
    }
}
