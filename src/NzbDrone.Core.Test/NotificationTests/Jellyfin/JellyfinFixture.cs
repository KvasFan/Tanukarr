using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Jellyfin;
using NzbDrone.Core.Test.Framework;
using JellyfinNotification = NzbDrone.Core.Notifications.Jellyfin.Jellyfin;

namespace NzbDrone.Core.Test.NotificationTests.Jellyfin
{
    [TestFixture]
    public class JellyfinFixture : CoreTest<JellyfinNotification>
    {
        private JellyfinSettings _settings;
        private NotificationDefinition _definition;

        [SetUp]
        public void Setup()
        {
            _settings = new JellyfinSettings
            {
                Host = "jellyfin.example",
                ApiKey = "test-api-key",
                LibraryId = "d834474f444b4d328119fe8c4b9c71fa"
            };

            _definition = new NotificationDefinition
            {
                Id = 10,
                Implementation = "Jellyfin",
                OnDownload = true,
                Settings = _settings
            };
            Subject.Definition = _definition;
            Mocker.GetMock<IJellyfinProxy>()
                .Setup(proxy => proxy.GetLibraries(_settings))
                .Returns(new List<JellyfinLibrary>
                {
                    new JellyfinLibrary { ItemId = _settings.LibraryId, Name = "Anime", CollectionType = "tvshows" }
                });
        }

        [Test]
        public void should_expose_a_dedicated_integration_with_import_and_upgrade_triggers()
        {
            Subject.Name.Should().Be("Jellyfin");
            Subject.SupportsOnDownload.Should().BeTrue();
            Subject.SupportsOnUpgrade.Should().BeTrue();
            Subject.SupportsOnImportComplete.Should().BeFalse("a completed download must not count the same imported media again");
            Subject.SupportsOnGrab.Should().BeFalse();
            Subject.SupportsOnRename.Should().BeFalse();
        }

        [Test]
        public void should_pass_the_saved_connection_to_the_persistent_import_service()
        {
            Subject.OnDownload(new DownloadMessage { EpisodeFile = new EpisodeFile { Id = 123 } });

            Mocker.GetMock<IJellyfinRefreshService>().Verify(service => service.OnImport(_definition), Times.Once());
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_test_access_to_the_selected_library_without_triggering_a_scan()
        {
            Subject.Test().IsValid.Should().BeTrue();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.TestLibrary(_settings), Times.Once());
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
            Mocker.GetMock<IJellyfinRefreshService>().Verify(service => service.OnImport(It.IsAny<NotificationDefinition>()), Times.Never());
        }

        [Test]
        public void should_fail_the_connection_test_for_a_deleted_library()
        {
            Mocker.GetMock<IJellyfinProxy>().Setup(proxy => proxy.TestLibrary(_settings)).Throws(new JellyfinException("LibraryId", "The selected library is unavailable."));

            Subject.Test().Errors.Should().Contain(error => error.PropertyName == "LibraryId");

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_report_the_sanitized_api_key_error_as_a_validation_failure()
        {
            Mocker.GetMock<IJellyfinProxy>()
                .Setup(proxy => proxy.TestLibrary(_settings))
                .Throws(new JellyfinException("ApiKey", "Jellyfin rejected the API key."));

            Subject.Test().Errors.Should().ContainSingle().Which.PropertyName.Should().Be("ApiKey");
        }

        [Test]
        public void should_reject_invalid_settings_without_contacting_jellyfin()
        {
            _settings.LibraryId = "not-a-guid";

            Subject.Test().IsValid.Should().BeFalse();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.TestLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_reject_an_uninitialized_library_without_marking_imports_as_refreshed()
        {
            Mocker.GetMock<IJellyfinProxy>()
                .Setup(proxy => proxy.TestLibrary(_settings))
                .Throws(new JellyfinException("LibraryId", "Initialize the library in Jellyfin before enabling refresh."));

            Subject.Test().Errors.Should().ContainSingle().Which.PropertyName.Should().Be("LibraryId");

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
            Mocker.GetMock<IJellyfinRefreshService>().Verify(service => service.OnImport(It.IsAny<NotificationDefinition>()), Times.Never());
        }

        [Test]
        public void should_fetch_named_library_choices_before_a_library_has_been_selected()
        {
            _settings.LibraryId = string.Empty;
            Mocker.GetMock<IJellyfinProxy>()
                .Setup(proxy => proxy.GetLibraries(_settings))
                .Returns(new List<JellyfinLibrary>
                {
                    new JellyfinLibrary { ItemId = "af7440e699464d958939fd71fe016d91", Name = "TV", CollectionType = "tvshows" },
                    new JellyfinLibrary { ItemId = "d834474f444b4d328119fe8c4b9c71fa", Name = "Anime", CollectionType = "tvshows" }
                });

            var result = Subject.RequestAction("libraries", new Dictionary<string, string>());
            var options = (JArray)JObject.Parse(Json.ToJson(result))["options"];

            options.Should().HaveCount(2);
            options.Select(option => (string)option["name"]).Should().Equal("Anime", "TV");
            options.Select(option => (string)option["value"]).Should().Equal("d834474f444b4d328119fe8c4b9c71fa", "af7440e699464d958939fd71fe016d91");
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }
    }
}
