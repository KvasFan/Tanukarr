using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Jellyfin;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Jellyfin
{
    [TestFixture]
    public class JellyfinSettingsFixture : CoreTest
    {
        private JellyfinSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new JellyfinSettings
            {
                Host = "localhost",
                ApiKey = "test-api-key",
                LibraryId = "d834474f444b4d328119fe8c4b9c71fa"
            };
        }

        [Test]
        public void should_default_to_a_valid_hourly_interval()
        {
            _settings.RefreshMode.Should().Be((int)JellyfinRefreshMode.Interval);
            _settings.RefreshIntervalMinutes.Should().Be(60);
            _settings.Validate().IsValid.Should().BeTrue();
        }

        [TestCase(1)]
        [TestCase(60)]
        [TestCase(int.MaxValue)]
        public void should_accept_positive_intervals_without_date_overflow_restrictions(int minutes)
        {
            _settings.RefreshIntervalMinutes = minutes;

            _settings.Validate().IsValid.Should().BeTrue();
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void should_reject_zero_or_negative_intervals(int minutes)
        {
            _settings.RefreshIntervalMinutes = minutes;

            _settings.Validate().Errors.Should().Contain(error => error.PropertyName == "RefreshIntervalMinutes");
        }

        [TestCase(0)]
        [TestCase(65536)]
        public void should_reject_invalid_ports(int port)
        {
            _settings.Port = port;

            _settings.Validate().Errors.Should().Contain(error => error.PropertyName == "Port");
        }

        [TestCase(1)]
        [TestCase(65535)]
        public void should_accept_port_boundaries(int port)
        {
            _settings.Port = port;

            _settings.Validate().IsValid.Should().BeTrue();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("../Library/Refresh")]
        [TestCase("not-a-library")]
        [TestCase("00000000000000000000000000000000")]
        public void should_require_one_library_guid(string libraryId)
        {
            _settings.LibraryId = libraryId;

            _settings.Validate().Errors.Should().Contain(error => error.PropertyName == "LibraryId");
        }

        [Test]
        public void should_accept_the_hyphenated_form_of_a_library_guid()
        {
            _settings.LibraryId = Guid.Parse(_settings.LibraryId).ToString("D");

            _settings.Validate().IsValid.Should().BeTrue();
        }

        [TestCase("test-key\r\nX-Another: injected")]
        [TestCase("key\"injected")]
        [TestCase("key,Token=other")]
        [TestCase("key\\escape")]
        [TestCase("key with spaces")]
        public void should_reject_api_key_header_injection(string apiKey)
        {
            _settings.ApiKey = apiKey;

            _settings.Validate().Errors.Should().Contain(error => error.PropertyName == "ApiKey");
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void should_reject_unknown_refresh_modes(int mode)
        {
            _settings.RefreshMode = mode;

            _settings.Validate().Errors.Should().Contain(error => error.PropertyName == "RefreshMode");
        }

        [TestCase("/jellyfin?api_key=other")]
        [TestCase("/jellyfin#fragment")]
        [TestCase("/jellyfin\\other")]
        [TestCase("https://other.example")]
        [TestCase("/jellyfin\r\nX-Test: injected")]
        public void should_reject_url_base_query_fragment_or_header_injection(string urlBase)
        {
            _settings.UrlBase = urlBase;

            _settings.Validate().Errors.Should().Contain(error => error.PropertyName == "UrlBase");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("/jellyfin")]
        [TestCase("/jellyfin/server")]
        public void should_accept_an_optional_reverse_proxy_path(string urlBase)
        {
            _settings.UrlBase = urlBase;

            _settings.Validate().IsValid.Should().BeTrue();
        }
    }
}
