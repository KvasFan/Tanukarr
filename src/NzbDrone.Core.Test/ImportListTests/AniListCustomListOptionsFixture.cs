using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.AniList;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class AniListCustomListOptionsFixture : CoreTest
    {
        private AniListImport _subject;
        private AniListSettings _settings;
        private HttpRequest _request;

        [SetUp]
        public void Setup()
        {
            _settings = new AniListSettings
            {
                Username = " someone ",
                AccessToken = "fixture-token",
                RefreshToken = "fixture-refresh",
                Expires = DateTime.UtcNow.AddDays(-1),
                IncludedCustomLists = new[] { "Stored unavailable name" },
                ExcludedCustomLists = new[] { "Excluded" }
            };
            _subject = Mocker.Resolve<AniListImport>();
            _subject.Definition = new ImportListDefinition { Id = 10, Name = "Fixture", Settings = _settings };
        }

        private void GivenResponse(string json)
        {
            Mocker.GetMock<IAniListRequestService>().Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    _request = request;
                    return new HttpResponse(request, new HttpHeader(), json);
                });
        }

        private List<FieldSelectStringOption> Load()
        {
            var result = _subject.RequestAction("getCustomLists", new Dictionary<string, string>());
            return (List<FieldSelectStringOption>)result.GetType().GetProperty("options").GetValue(result);
        }

        [Test]
        public void should_load_names_without_fetching_entries_or_changing_settings_or_oauth()
        {
            GivenResponse("{\"data\":{\"User\":{\"mediaListOptions\":{\"animeList\":{\"customLists\":[\"DaiBaNanaTop\",\"Other\",\"DaiBaNanaTop\",\"other\"]}}}}}");
            var expiration = _settings.Expires;

            var options = Load();

            options.ConvertAll(o => o.Value).Should().Equal("DaiBaNanaTop", "Other", "other");
            options.Should().OnlyContain(o => o.Name == o.Value);
            _request.Url.FullUri.TrimEnd('/').Should().Be("https://graphql.anilist.co");
            using var body = JsonDocument.Parse(_request.ContentData);
            body.RootElement.GetProperty("variables").GetProperty("username").GetString().Should().Be("someone");
            body.RootElement.GetProperty("query").GetString().Should().Contain("mediaListOptions").And.NotContain("Page(").And.NotContain("media {");
            _settings.Username.Should().Be(" someone ");
            _settings.Expires.Should().Be(expiration);
            _settings.IncludedCustomLists.Should().Equal("Stored unavailable name");
            _settings.ExcludedCustomLists.Should().Equal("Excluded");
            Mocker.GetMock<IHttpClient>().Verify(s => s.Execute(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IImportListRepository>().Verify(s => s.UpdateSettings(It.IsAny<ImportListDefinition>()), Times.Never());
        }

        [Test]
        public void should_accept_an_explicit_empty_custom_list_array()
        {
            GivenResponse("{\"data\":{\"User\":{\"mediaListOptions\":{\"animeList\":{\"customLists\":[]}}}}}");

            Load().Should().BeEmpty();
            _settings.ExcludedCustomLists.Should().Equal("Excluded");
        }

        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("{\"data\":{\"User\":null}}")]
        [TestCase("{\"data\":{\"User\":{\"mediaListOptions\":null}}}")]
        [TestCase("{\"data\":{\"User\":{\"mediaListOptions\":{\"animeList\":{\"customLists\":null}}}}}")]
        [TestCase("{\"data\":{\"User\":{\"mediaListOptions\":{\"animeList\":{\"customLists\":[null]}}}}}")]
        [TestCase("{\"data\":{\"User\":{\"mediaListOptions\":{\"animeList\":{\"customLists\":[true]}}}}}")]
        [TestCase("{\"data\":{\"User\":{\"mediaListOptions\":{\"animeList\":{\"customLists\":[\" \" ]}}}}}")]
        [TestCase("{\"errors\":[{\"message\":\"Private fixture error\"}],\"data\":{\"User\":{\"mediaListOptions\":{\"animeList\":{\"customLists\":[]}}}}}")]
        [TestCase("<html>Upstream failure</html>")]
        public void should_reject_incomplete_or_partial_options_without_replacing_selections(string json)
        {
            GivenResponse(json);

            Action load = () => Load();

            load.Should().Throw<ImportListException>();
            _settings.IncludedCustomLists.Should().Equal("Stored unavailable name");
            _settings.ExcludedCustomLists.Should().Equal("Excluded");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void should_require_an_account_name_before_making_a_request(string username)
        {
            _settings.Username = username;

            Action load = () => Load();

            load.Should().Throw<ImportListException>();
            Mocker.GetMock<IAniListRequestService>().Verify(s => s.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }
    }
}
