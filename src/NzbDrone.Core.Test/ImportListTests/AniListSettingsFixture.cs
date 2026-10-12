using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class AniListSettingsFixture : CoreTest
    {
        private AniListSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new AniListSettings
            {
                Username = "someone",
                AccessToken = "example-access-token",
                RefreshToken = "example-refresh-token",
                Expires = DateTime.UtcNow.AddDays(30)
            };
        }

        [Test]
        public void should_preserve_defaults_and_disable_all_new_exclusions()
        {
            _settings.Validate().IsValid.Should().BeTrue();
            _settings.IncludedCustomLists.Should().BeEmpty();
            _settings.ExcludedCustomLists.Should().BeEmpty();
            _settings.ImportCurrent.Should().BeTrue();
            _settings.ImportPlanning.Should().BeTrue();
            _settings.ExcludeCurrent.Should().BeFalse();
            _settings.ExcludePlanning.Should().BeFalse();
            _settings.ExcludeCompleted.Should().BeFalse();
            _settings.ExcludeDropped.Should().BeFalse();
            _settings.ExcludePaused.Should().BeFalse();
            _settings.ExcludeRepeating.Should().BeFalse();
        }

        [Test]
        public void should_read_legacy_json_without_new_filter_fields()
        {
            var settings = Json.Deserialize<AniListSettings>("""
                {"username":"someone","customList":"Seasonal","importCurrent":true,"importPlanning":false,"importCompleted":true,"importFinished":true,"importReleasing":false}
                """);

            settings.CustomList.Should().Be("Seasonal");
            settings.ImportCompleted.Should().BeTrue();
            settings.ImportPlanning.Should().BeFalse();
            settings.IncludedCustomLists.Should().BeEmpty();
            settings.ExcludedCustomLists.Should().BeEmpty();
            settings.ExcludeCurrent.Should().BeFalse();
            settings.ExcludeCompleted.Should().BeFalse();
        }

        [Test]
        public void should_round_trip_literal_names_and_exclusions_through_provider_settings_json()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal", "seasonal", " Seasonal ", "名前, with comma" };
            _settings.ExcludedCustomLists = new[] { "Excluded", "excluded" };
            _settings.ExcludeCurrent = true;
            _settings.ExcludeCompleted = true;

            var saved = Json.Deserialize<AniListSettings>(Json.ToJson(_settings));

            saved.IncludedCustomLists.Should().Equal(_settings.IncludedCustomLists);
            saved.ExcludedCustomLists.Should().Equal(_settings.ExcludedCustomLists);
            saved.ExcludeCurrent.Should().BeTrue();
            saved.ExcludeCompleted.Should().BeTrue();
        }

        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase(" ", true)]
        [TestCase("\t", true)]
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase(" ", false)]
        [TestCase("\t", false)]
        public void should_reject_empty_custom_list_names(string name, bool included)
        {
            if (included)
            {
                _settings.IncludedCustomLists = new[] { "Valid", name };
            }
            else
            {
                _settings.ExcludedCustomLists = new[] { "Valid", name };
            }

            _settings.Validate().IsValid.Should().BeFalse();
        }

        [Test]
        public void should_accept_null_filter_arrays_for_legacy_settings()
        {
            _settings.IncludedCustomLists = null;
            _settings.ExcludedCustomLists = null;

            _settings.Validate().IsValid.Should().BeTrue();
        }

        [Test]
        public void should_allow_an_explicit_exclusion_to_override_an_included_list_or_status()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            _settings.ExcludedCustomLists = new[] { "Seasonal" };
            _settings.ExcludeCurrent = true;

            _settings.Validate().IsValid.Should().BeTrue();
        }

        [Test]
        public void should_still_require_at_least_one_watching_status()
        {
            _settings.ImportCurrent = false;
            _settings.ImportPlanning = false;
            _settings.IncludedCustomLists = new[] { "Seasonal" };

            _settings.Validate().Errors.Should().Contain(error => error.PropertyName == "ImportCurrent");
        }

        [Test]
        public void should_still_require_at_least_one_media_release_status()
        {
            _settings.ImportFinished = false;
            _settings.ImportReleasing = false;
            _settings.ExcludedCustomLists = new[] { "Excluded" };

            _settings.Validate().Errors.Should().Contain(error => error.PropertyName == "ImportFinished");
        }
    }
}
