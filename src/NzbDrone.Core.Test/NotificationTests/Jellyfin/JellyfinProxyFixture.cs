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
    public class JellyfinProxyFixture : CoreTest<JellyfinProxy>
    {
        private const string LibraryId = "d834474f444b4d328119fe8c4b9c71fa";
        private JellyfinSettings _settings;
        private List<HttpRequest> _requests;

        [SetUp]
        public void Setup()
        {
            _settings = new JellyfinSettings
            {
                Host = "jellyfin.example",
                Port = 8096,
                ApiKey = "private-test-api-key",
                LibraryId = LibraryId,
                UrlBase = "/jellyfin"
            };

            _requests = new List<HttpRequest>();
            GivenLibraries("[{\"Name\":\"Anime\",\"ItemId\":\"" + LibraryId + "\",\"CollectionType\":\"tvshows\",\"Locations\":[\"/media/Anime\"]}]");
            GivenItems("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/Anime\",\"Type\":\"Folder\"}],\"TotalRecordCount\":1}");
            GivenRefreshResponse(HttpStatusCode.NoContent);
        }

        private void GivenLibraries(string content, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get<List<JellyfinLibrary>>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);
                    return new HttpResponse<List<JellyfinLibrary>>(new HttpResponse(request, new HttpHeader(), content, status));
                });
        }

        private void GivenRefreshResponse(HttpStatusCode status)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Post(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);
                    return new HttpResponse(request, new HttpHeader(), string.Empty, status);
                });
        }

        private void GivenItems(string content, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get<JellyfinItems>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);
                    return new HttpResponse<JellyfinItems>(new HttpResponse(request, new HttpHeader(), content, status));
                });
        }

        [Test]
        public void should_deserialize_the_server_library_response_with_the_real_http_serializer()
        {
            var libraries = Subject.GetLibraries(_settings);

            libraries.Should().ContainSingle();
            libraries[0].ItemId.Should().Be(LibraryId);
            libraries[0].Name.Should().Be("Anime");
            libraries[0].CollectionType.Should().Be("tvshows");
            libraries[0].Locations.Should().Equal("/media/Anime");
            _requests.Should().ContainSingle().Which.Url.Path.Should().Be("/jellyfin/Library/VirtualFolders");
        }

        [Test]
        public void should_force_refresh_only_the_selected_library_and_preserve_metadata_and_images()
        {
            _settings.LibraryId = Guid.Parse(LibraryId).ToString("D");

            Subject.RefreshLibrary(_settings);

            _requests.Should().HaveCount(3);
            var request = _requests[2];
            request.Url.Path.Should().Be("/jellyfin/Items/" + LibraryId + "/Refresh");
            request.Url.Query.Should().NotContain("recursive=");
            request.Url.Query.Should().Contain("metadataRefreshMode=FullRefresh");
            request.Url.Query.Should().Contain("imageRefreshMode=Default");
            request.Url.Query.Should().Contain("replaceAllMetadata=false");
            request.Url.Query.Should().Contain("replaceAllImages=false");
            _requests.Should().NotContain(item => item.Url.Path.EndsWith("/Library/Refresh", StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void should_verify_initialized_physical_folders_using_only_the_selected_library_scope()
        {
            Subject.TestLibrary(_settings);

            _requests.Should().HaveCount(2);
            var folders = _requests[1];
            folders.Url.Path.Should().Be("/jellyfin/Items");
            folders.Url.Query.Should().Contain("parentId=" + LibraryId);
            folders.Url.Query.Should().Contain("recursive=true");
            folders.Url.Query.Should().Contain("includeItemTypes=Folder");
            folders.Url.Query.Should().Contain("fields=Path");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase("{\"Items\":[],\"TotalRecordCount\":0}")]
        [TestCase("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/OtherLibrary\",\"Type\":\"Folder\"}],\"TotalRecordCount\":1}")]
        [TestCase("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/Anime/Series\",\"Type\":\"Folder\"}],\"TotalRecordCount\":1}")]
        [TestCase("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/anime\",\"Type\":\"Folder\"}],\"TotalRecordCount\":1}")]
        [TestCase("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/Anime\",\"Type\":\"Series\"}],\"TotalRecordCount\":1}")]
        public void should_reject_a_cold_library_without_posting_or_refreshing_another_library(string content)
        {
            GivenItems(content);

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().Throw<JellyfinException>().Which.PropertyName.Should().Be("LibraryId");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
            _requests.Should().NotContain(request => request.Url.Path.EndsWith("/Library/Refresh", StringComparison.OrdinalIgnoreCase));
        }

        [TestCase("[]")]
        [TestCase("null")]
        public void should_require_configured_library_locations_before_attempting_a_refresh(string locations)
        {
            GivenLibraries("[{\"Name\":\"Anime\",\"ItemId\":\"" + LibraryId + "\",\"Locations\":" + locations + "}]");

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().Throw<JellyfinException>().Which.PropertyName.Should().Be("LibraryId");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_require_all_library_locations_to_be_initialized()
        {
            GivenLibraries("[{\"Name\":\"Anime\",\"ItemId\":\"" + LibraryId + "\",\"Locations\":[\"/media/Anime\",\"/media/AnimeExtra\"]}]");

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().Throw<JellyfinException>().Which.PropertyName.Should().Be("LibraryId");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());

            GivenItems("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/Anime\",\"Type\":\"Folder\"},{\"Id\":\"3d88a080dc9b4983b10b18d21fa69e6b\",\"Path\":\"/media/AnimeExtra\",\"Type\":\"Folder\"}],\"TotalRecordCount\":2}");

            Subject.RefreshLibrary(_settings);

            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void should_compare_windows_server_paths_independently_of_the_sonarr_host_platform()
        {
            GivenLibraries("[{\"Name\":\"Anime\",\"ItemId\":\"" + LibraryId + "\",\"Locations\":[\"D:\\\\Anime\"]}]");
            GivenItems("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"d:/anime/\",\"Type\":\"Folder\"}],\"TotalRecordCount\":1}");

            Subject.RefreshLibrary(_settings);

            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void should_accept_trailing_linux_separators_without_discarding_significant_path_whitespace()
        {
            GivenLibraries("[{\"Name\":\"Anime\",\"ItemId\":\"" + LibraryId + "\",\"Locations\":[\"/media/Anime /\"]}]");
            GivenItems("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/Anime \",\"Type\":\"Folder\"}],\"TotalRecordCount\":1}");

            Subject.RefreshLibrary(_settings);

            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Once());
            GivenItems("{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/Anime\",\"Type\":\"Folder\"}],\"TotalRecordCount\":1}");
            Action action = () => Subject.RefreshLibrary(_settings);
            action.Should().Throw<JellyfinException>();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Once());
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        [TestCase(HttpStatusCode.Found)]
        public void should_reject_failed_readiness_requests_without_exposing_credentials_or_posting(HttpStatusCode status)
        {
            GivenItems("response contains " + _settings.ApiKey, status);

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().Throw<JellyfinException>().Which.ToString().Should().NotContain(_settings.ApiKey);
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_page_the_selected_library_until_its_physical_root_is_found_or_results_end(bool initialized)
        {
            var firstPage = JsonConvert.SerializeObject(new
            {
                Items = Enumerable.Range(0, 200).Select(index => new
                {
                    Id = index.ToString("D32"),
                    Path = "/media/Other/Folder" + index,
                    Type = "Folder"
                }).ToArray(),
                TotalRecordCount = 201
            });
            var lastPage = initialized
                ? "{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/Anime\",\"Type\":\"Folder\"}],\"TotalRecordCount\":201}"
                : "{\"Items\":[],\"TotalRecordCount\":201}";
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get<JellyfinItems>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);
                    var content = request.Url.Query.Contains("startIndex=200", StringComparison.Ordinal) ? lastPage : firstPage;
                    return new HttpResponse<JellyfinItems>(new HttpResponse(request, new HttpHeader(), content));
                });

            Action action = () => Subject.TestLibrary(_settings);

            if (initialized)
            {
                action.Should().NotThrow();
            }
            else
            {
                action.Should().Throw<JellyfinException>().Which.PropertyName.Should().Be("LibraryId");
            }

            _requests.Should().HaveCount(3);
            _requests[1].Url.Query.Should().Contain("startIndex=0");
            _requests[2].Url.Query.Should().Contain("startIndex=200");
            _requests.Where(request => request.Url.Path == "/jellyfin/Items")
                .Should().OnlyContain(request => request.Url.Query.Contains("parentId=" + LibraryId, StringComparison.Ordinal));
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_reject_a_negative_readiness_result_count_without_posting()
        {
            GivenItems("{\"Items\":[],\"TotalRecordCount\":-1}");

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().Throw<JellyfinException>().Which.PropertyName.Should().Be("Host");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("not JSON")]
        public void should_fail_a_malformed_readiness_response_without_acknowledging_a_refresh(string content)
        {
            GivenItems(content);

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().Throw<JellyfinException>();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_keep_credentials_in_headers_and_disable_redirects_and_response_logging()
        {
            Subject.RefreshLibrary(_settings);

            foreach (var request in _requests)
            {
                request.Headers.GetSingleValue("Authorization").Should().Be("MediaBrowser Token=\"" + _settings.ApiKey + "\"");
                request.Url.FullUri.Should().NotContain(_settings.ApiKey);
                request.AllowAutoRedirect.Should().BeFalse();
                request.LogResponseContent.Should().BeFalse();
                request.LogHttpError.Should().BeFalse();
                request.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
            }
        }

        [Test]
        public void should_support_ssl_and_a_reverse_proxy_path()
        {
            _settings.UseSsl = true;

            Subject.GetLibraries(_settings);

            _requests[0].Url.Scheme.Should().Be("https");
            _requests[0].Url.Host.Should().Be("jellyfin.example");
            _requests[0].Url.Port.Should().Be(8096);
        }

        [Test]
        public void should_refuse_a_library_that_has_been_removed_without_refreshing_every_library()
        {
            GivenLibraries("[{\"Name\":\"Other\",\"ItemId\":\"af7440e699464d958939fd71fe016d91\"}]");

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().Throw<JellyfinException>().Which.PropertyName.Should().Be("LibraryId");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_reject_an_invalid_library_id_before_any_http_request()
        {
            _settings.LibraryId = "../Library/Refresh";

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().Throw<JellyfinException>();
            _requests.Should().BeEmpty();
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.Found)]
        public void should_reject_failed_library_requests_without_exposing_the_server_response(HttpStatusCode status)
        {
            GivenLibraries("server body contains " + _settings.ApiKey, status);

            Action action = () => Subject.GetLibraries(_settings);

            var exception = action.Should().Throw<JellyfinException>().Which;
            exception.Message.Should().NotContain(_settings.ApiKey);
            exception.InnerException.Should().BeNull();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase(HttpStatusCode.NoContent)]
        public void should_accept_successful_async_refresh_responses(HttpStatusCode status)
        {
            GivenRefreshResponse(status);

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().NotThrow();
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.NotFound)]
        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.Found)]
        [TestCase(HttpStatusCode.Accepted)]
        [TestCase(HttpStatusCode.OK)]
        public void should_reject_an_unsuccessful_or_redirected_refresh(HttpStatusCode status)
        {
            GivenRefreshResponse(status);

            Action action = () => Subject.RefreshLibrary(_settings);

            action.Should().Throw<JellyfinException>();
        }

        [TestCase("null")]
        [TestCase("not JSON")]
        [TestCase("{}")]
        public void should_reject_an_invalid_library_response(string content)
        {
            GivenLibraries(content);

            Action action = () => Subject.GetLibraries(_settings);

            action.Should().Throw<JellyfinException>();
        }

        [Test]
        public void should_strip_untrusted_transport_exception_messages_and_inner_exceptions()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get<List<JellyfinLibrary>>(It.IsAny<HttpRequest>()))
                .Throws(new Exception("proxy rejected credential " + _settings.ApiKey));

            Action action = () => Subject.GetLibraries(_settings);

            var exception = action.Should().Throw<JellyfinException>().Which;
            exception.ToString().Should().NotContain(_settings.ApiKey);
            exception.InnerException.Should().BeNull();
        }

        [Test]
        public void should_ignore_library_entries_with_non_library_identifiers()
        {
            GivenLibraries("[null,{\"Name\":\"Invalid\",\"ItemId\":\"../Library/Refresh\"},{\"Name\":\"Empty\",\"ItemId\":\"00000000000000000000000000000000\"},{\"Name\":\"Anime\",\"ItemId\":\"" + LibraryId + "\"}]");

            Subject.GetLibraries(_settings).Should().ContainSingle().Which.ItemId.Should().Be(LibraryId);
        }
    }
}
