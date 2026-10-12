using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList.List;
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

        private static string Page(int currentPage, bool hasNextPage, bool onCustomList, params int[] mediaIds)
        {
            return Json.ToJson(new
            {
                data = new
                {
                    Page = new
                    {
                        pageInfo = new { currentPage, lastPage = 1, hasNextPage },
                        mediaList = mediaIds.Select(id => new
                        {
                            status = "PLANNING",
                            customLists = new Dictionary<string, bool> { { "Seasonal", onCustomList } },
                            media = new { id, status = "FINISHED", title = new { userPreferred = "Same title" } }
                        }).ToList()
                    }
                }
            });
        }

        private void GivenResponses(params string[] responses)
        {
            var pages = new Queue<string>(responses);
            Mocker.GetMock<IHttpClient>()
                  .Setup(client => client.Execute(It.IsAny<HttpRequest>()))
                  .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(pages.Dequeue())));
        }

        [Test]
        public void should_keep_distinct_anilist_entries_with_the_same_title_across_pages()
        {
            GivenResponses(Page(1, true, true, 1), Page(2, false, true, 9253));

            var result = _subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Series.Select(item => item.AniListId).Should().Equal(1, 9253);
            result.Series.Should().OnlyContain(item => item.ImportListId == 10);
        }

        [Test]
        public void should_continue_past_pages_filtered_out_of_the_selected_custom_list()
        {
            ((AniListSettings)_subject.Definition.Settings).CustomList = "Seasonal";
            GivenResponses(Page(1, true, false, 1), Page(2, false, true, 9253));

            var result = _subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Series.Should().ContainSingle().Which.AniListId.Should().Be(9253);
        }

        [Test]
        public void should_not_stop_at_an_inaccurate_last_page()
        {
            GivenResponses(Page(1, true, true, 1), Page(2, false, true, 2));

            _subject.Fetch().Series.Should().HaveCount(2);
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void should_fail_on_a_repeated_page_instead_of_looping()
        {
            GivenResponses(Page(1, true, true, 1), Page(1, true, true, 2));

            _subject.Fetch().AnyFailure.Should().BeTrue();

            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Exactly(2));
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(10), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_fail_when_the_api_depth_limit_would_truncate_the_list()
        {
            GivenResponses(Enumerable.Range(1, 100).Select(page => Page(page, true, true)).ToArray());

            _subject.Fetch().AnyFailure.Should().BeTrue();

            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(It.IsAny<HttpRequest>()), Times.Exactly(100));
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(10), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_report_graphql_failure_even_with_http_success()
        {
            GivenResponses(@"{""data"": null, ""errors"": [{""message"": ""Private user""}]}");

            _subject.Fetch().AnyFailure.Should().BeTrue();

            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(10), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
