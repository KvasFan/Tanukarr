using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Notifications.Jellyfin;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Jellyfin
{
    [TestFixture]
    public class JellyfinWatchProxyFixture : CoreTest<JellyfinWatchProxy>
    {
        private const string LibraryId = "d834474f444b4d328119fe8c4b9c71fa";
        private const string UserId = "3d88a080dc9b4983b10b18d21fa69e6b";
        private const string ItemId = "af7440e699464d958939fd71fe016d91";
        private JellyfinSettings _settings;
        private List<HttpRequest> _requests;

        [SetUp]
        public void Setup()
        {
            _settings = new JellyfinSettings
            {
                Host = "jellyfin.example", Port = 8096, UrlBase = "/jellyfin", ApiKey = "private-fixture-key",
                LibraryId = LibraryId, WatchedUserId = UserId, EnableWatchedSync = true
            };
            _requests = new List<HttpRequest>();
            Mocker.GetMock<IJellyfinProxy>().Setup(proxy => proxy.GetLibraries(_settings))
                .Returns(new List<JellyfinLibrary> { new JellyfinLibrary { ItemId = LibraryId } });
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<List<JellyfinUser>>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<List<JellyfinUser>>(request, "[{\"Id\":\"" + UserId + "\",\"Name\":\"Selected fixture user\"}]"));
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JellyfinItem>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<JellyfinItem>(request, "{\"Id\":\"" + LibraryId + "\",\"Type\":\"CollectionFolder\"}"));
            GivenPage("{\"Items\":[{\"Id\":\"" + ItemId + "\",\"Type\":\"Episode\",\"Path\":\"/media/Fixture/S01E01.mkv\",\"UserData\":{\"Played\":true}}],\"TotalRecordCount\":1}");
        }

        private HttpResponse<T> Response<T>(HttpRequest request, string content, HttpStatusCode status = HttpStatusCode.OK)
            where T : new()
        {
            _requests.Add(request);
            return new HttpResponse<T>(new HttpResponse(request, new HttpHeader(), content, status));
        }

        private void GivenPage(string content, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JellyfinWatchedItems>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<JellyfinWatchedItems>(request, content, status));
        }

        [Test]
        public void should_read_played_status_through_the_real_newtonsoft_http_serializer()
        {
            var actual = Subject.GetSnapshot(_settings);
            actual.Should().ContainSingle();
            actual[0].UserData.Played.Should().BeTrue();
            var request = _requests.Last();
            request.Url.Path.Should().Be("/jellyfin/Users/" + UserId + "/Items");
            request.Url.Query.Should().Contain("parentId=" + LibraryId).And.Contain("enableUserData=true");
            request.Url.Query.Should().Contain("fields=Path").And.Contain("startIndex=0").And.Contain("limit=200");
            foreach (var sent in _requests)
            {
                sent.Headers.GetSingleValue("Authorization").Should().Be("MediaBrowser Token=\"" + _settings.ApiKey + "\"");
                sent.Url.FullUri.Should().NotContain(_settings.ApiKey);
                sent.AllowAutoRedirect.Should().BeFalse();
                sent.LogResponseContent.Should().BeFalse();
                sent.LogHttpError.Should().BeFalse();
                sent.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
            }

            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase("{\"Items\":[],\"TotalRecordCount\":0}", 0)]
        [TestCase("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Type\":\"Movie\",\"Path\":\"/media/Fixture/Film.mkv\",\"UserData\":{\"Played\":false}}],\"TotalRecordCount\":1}", 1)]
        public void should_accept_an_explicit_empty_library_or_unwatched_movie(string content, int count)
        {
            GivenPage(content);
            Subject.GetSnapshot(_settings).Should().HaveCount(count);
        }

        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("{\"Items\":[],\"TotalRecordCount\":-1}")]
        [TestCase("{\"Items\":[],\"TotalRecordCount\":100001}")]
        [TestCase("{\"Items\":[],\"TotalRecordCount\":1}")]
        [TestCase("{\"Items\":[null],\"TotalRecordCount\":1}")]
        [TestCase("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Type\":\"Episode\",\"Path\":\"/media/Fixture.mkv\"}],\"TotalRecordCount\":1}")]
        [TestCase("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Type\":\"Episode\",\"Path\":\"/media/Fixture.mkv\",\"UserData\":{}}],\"TotalRecordCount\":1}")]
        [TestCase("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Type\":\"Season\",\"Path\":\"/media/Fixture\",\"UserData\":{\"Played\":true}}],\"TotalRecordCount\":1}")]
        [TestCase("{\"Items\":[{\"Id\":\"not-a-guid\",\"Type\":\"Episode\",\"Path\":\"/media/Fixture.mkv\",\"UserData\":{\"Played\":true}}],\"TotalRecordCount\":1}")]
        [TestCase("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Type\":\"Episode\",\"Path\":\"relative.mkv\",\"UserData\":{\"Played\":true}}],\"TotalRecordCount\":1}")]
        public void should_reject_incomplete_data_before_returning_a_snapshot(string content)
        {
            GivenPage(content);
            Action action = () => Subject.GetSnapshot(_settings);
            action.Should().Throw<JellyfinException>().Which.Message.Should().Contain("Previous watched state is retained");
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.MovedPermanently)]
        public void should_reject_error_and_redirect_responses_without_following_or_echoing_the_key(HttpStatusCode status)
        {
            GivenPage("private-fixture-key upstream details", status);
            Action action = () => Subject.GetSnapshot(_settings);
            action.Should().Throw<JellyfinException>().Which.Message.Should().NotContain(_settings.ApiKey);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_reject_duplicate_ids_or_changed_totals_on_later_pages(bool changedTotal)
        {
            var first = new JellyfinWatchedItems
            {
                TotalRecordCount = 2, Items = new List<JellyfinWatchedItem>
                {
                    new JellyfinWatchedItem { Id = ItemId, Path = "/media/one.mkv", Type = "Episode", UserData = new JellyfinWatchedUserData { Played = true } }
                }
            };
            var second = new JellyfinWatchedItems
            {
                TotalRecordCount = changedTotal ? 3 : 2, Items = new List<JellyfinWatchedItem>
                {
                    new JellyfinWatchedItem { Id = changedTotal ? UserId : ItemId, Path = "/media/two.mkv", Type = "Episode", UserData = new JellyfinWatchedUserData { Played = false } }
                }
            };
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JellyfinWatchedItems>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<JellyfinWatchedItems>(request,
                    JsonConvert.SerializeObject(request.Url.Query.Contains("startIndex=0", StringComparison.Ordinal) ? first : second)));
            Action action = () => Subject.GetSnapshot(_settings);
            action.Should().Throw<JellyfinException>();
            _requests.Last().Url.Query.Should().Contain("startIndex=1");
        }

        [Test]
        public void should_collect_all_pages_and_use_the_selected_user_on_every_request()
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<JellyfinWatchedItems>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<JellyfinWatchedItems>(request,
                    "{\"Items\":[{\"Id\":\"" + (request.Url.Query.Contains("startIndex=0", StringComparison.Ordinal) ? ItemId : UserId) +
                    "\",\"Type\":\"Episode\",\"Path\":\"/media/Fixture.mkv\",\"UserData\":{\"Played\":false}}],\"TotalRecordCount\":2}"));
            Subject.GetSnapshot(_settings).Should().HaveCount(2);
            _requests.Where(request => request.Url.Path.EndsWith("/Items", StringComparison.Ordinal))
                .Should().OnlyContain(request => request.Url.Path.Contains(UserId, StringComparison.Ordinal));
        }
    }
}
