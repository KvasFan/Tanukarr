using System;
using System.Text;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.MyAnimeList;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class MyAnimeListRefreshHandoffFixture : CoreTest<MyAnimeListImport>
    {
        private int _refreshRequests;
        private bool _rejectRepeatedRefresh;
        private string _persistedSettings;

        [SetUp]
        public void Setup()
        {
            _refreshRequests = 0;
            _rejectRepeatedRefresh = false;
            _persistedSettings = null;
            Subject.Definition = Definition();
            Mocker.GetMock<IMyAnimeListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    string body;
                    if (request.Url.FullUri == MyAnimeListImport.TokenUrl)
                    {
                        _refreshRequests++;
                        if (_rejectRepeatedRefresh && _refreshRequests > 1)
                        {
                            throw new InvalidOperationException("Fixture rejects reuse of a rotated refresh token");
                        }

                        body = "{\"access_token\":\"generated-renewed-access\",\"refresh_token\":\"generated-rotated-refresh\",\"expires_in\":7200,\"token_type\":\"Bearer\"}";
                    }
                    else
                    {
                        request.Headers["Authorization"].Should().Be("Bearer generated-renewed-access");
                        body = "{\"data\":[{\"node\":{\"id\":21,\"title\":\"Generated anime\",\"status\":\"finished_airing\"},\"list_status\":{\"status\":\"watching\",\"score\":8,\"tags\":[],\"is_rewatching\":false}}],\"paging\":{}}";
                    }

                    return new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(body));
                });
            Mocker.GetMock<IImportListRepository>()
                .Setup(repository => repository.UpdateSettings(It.IsAny<ImportListDefinition>()))
                .Callback<ImportListDefinition>(definition => _persistedSettings = Json.ToJson(definition.Settings));
        }

        private static ImportListDefinition Definition()
        {
            return new ImportListDefinition
            {
                Id = 10,
                Name = "Generated handoff list",
                Settings = new MyAnimeListSettings
                {
                    ClientId = "generated-client",
                    AccessToken = "generated-old-access",
                    RefreshToken = "generated-old-refresh",
                    Expires = DateTime.UtcNow.AddDays(-1)
                }
            };
        }

        [Test]
        public void should_reuse_renewed_credentials_after_unsaved_test_without_persisting_its_filters()
        {
            var original = Definition();
            ((MyAnimeListSettings)Subject.Definition.Settings).IncludedTags = new[] { "Unsaved unmatched tag" };
            Subject.Test().IsValid.Should().BeTrue();
            _persistedSettings.Should().BeNull();
            _rejectRepeatedRefresh = true;

            // A later factory instance receives the original saved credentials,
            // not the in-memory settings modified by the earlier Test request.
            var nextProvider = new MyAnimeListImport(
                Mocker.GetMock<IImportListRepository>().Object,
                Mocker.GetMock<IHttpClient>().Object,
                Mocker.GetMock<IImportListStatusService>().Object,
                Mocker.GetMock<IConfigService>().Object,
                Mocker.GetMock<IParsingService>().Object,
                Mocker.GetMock<ILocalizationService>().Object,
                Mocker.GetMock<IMyAnimeListRequestService>().Object,
                Mocker.Resolve<ICacheManager>(),
                LogManager.GetLogger("Generated MAL credential handoff"));
            nextProvider.Definition = original;
            nextProvider.Fetch().Series.Should().ContainSingle().Which.MalId.Should().Be(21);

            _refreshRequests.Should().Be(1, "the old refresh token must not be exchanged twice after a provider rotates it");
            var saved = Json.Deserialize<MyAnimeListSettings>(_persistedSettings);
            saved.AccessToken.Should().Be("generated-renewed-access");
            saved.RefreshToken.Should().Be("generated-rotated-refresh");
            saved.IncludedTags.Should().BeEmpty();
            Mocker.GetMock<IImportListRepository>().Verify(repository => repository.UpdateSettings(It.IsAny<ImportListDefinition>()), Times.Once());
        }

        [TestCase("client")]
        [TestCase("secret")]
        [TestCase("access")]
        [TestCase("refresh")]
        public void should_not_reuse_a_token_handoff_after_any_credential_context_changes(string changed)
        {
            Subject.Test().IsValid.Should().BeTrue();
            var next = Definition();
            var settings = (MyAnimeListSettings)next.Settings;
            switch (changed)
            {
                case "client":
                    settings.ClientId = "different-generated-client";
                    break;
                case "secret":
                    settings.ClientSecret = "different-generated-secret";
                    break;
                case "access":
                    settings.AccessToken = "different-generated-access";
                    break;
                case "refresh":
                    settings.RefreshToken = "different-generated-refresh";
                    break;
            }

            Subject.Definition = next;
            Subject.Fetch().AnyFailure.Should().BeFalse();

            _refreshRequests.Should().Be(2);
        }
    }
}
