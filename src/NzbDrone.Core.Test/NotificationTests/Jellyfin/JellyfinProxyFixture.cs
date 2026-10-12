using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
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
            GivenLibraries("[{\"Name\":\"Anime\",\"ItemId\":\"" + LibraryId + "\",\"CollectionType\":\"tvshows\"}]");
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

        [Test]
        public void should_deserialize_the_server_library_response_with_the_real_http_serializer()
        {
            var libraries = Subject.GetLibraries(_settings);

            libraries.Should().ContainSingle();
            libraries[0].ItemId.Should().Be(LibraryId);
            libraries[0].Name.Should().Be("Anime");
            libraries[0].CollectionType.Should().Be("tvshows");
            _requests.Should().ContainSingle().Which.Url.Path.Should().Be("/jellyfin/Library/VirtualFolders");
        }

        [Test]
        public void should_force_refresh_only_the_selected_library_and_preserve_metadata_and_images()
        {
            _settings.LibraryId = Guid.Parse(LibraryId).ToString("D");

            Subject.RefreshLibrary(_settings);

            _requests.Should().HaveCount(2);
            var request = _requests[1];
            request.Url.Path.Should().Be("/jellyfin/Items/" + LibraryId + "/Refresh");
            request.Url.Query.Should().NotContain("recursive=");
            request.Url.Query.Should().Contain("metadataRefreshMode=FullRefresh");
            request.Url.Query.Should().Contain("imageRefreshMode=Default");
            request.Url.Query.Should().Contain("replaceAllMetadata=false");
            request.Url.Query.Should().Contain("replaceAllImages=false");
            _requests.Should().NotContain(item => item.Url.Path.EndsWith("/Library/Refresh", StringComparison.OrdinalIgnoreCase));
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
