using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Annotations;
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
        public void should_preserve_default_import_statuses_and_empty_custom_filters()
        {
            _settings.Validate().IsValid.Should().BeTrue();
            _settings.IncludedCustomLists.Should().BeEmpty();
            _settings.ExcludedCustomLists.Should().BeEmpty();
            _settings.ImportCurrent.Should().BeTrue();
            _settings.ImportPlanning.Should().BeTrue();
        }

        [Test]
        public void should_read_legacy_json_without_new_filter_fields()
        {
            var settings = Json.Deserialize<AniListSettings>("""
                {"username":"someone","customList":"Seasonal","importCurrent":true,"importPlanning":false,"importCompleted":true,"importFinished":true,"importReleasing":false}
                """);

            settings.CustomList.Should().BeEmpty();
            settings.ImportCompleted.Should().BeTrue();
            settings.ImportPlanning.Should().BeFalse();
            settings.IncludedCustomLists.Should().Equal("Seasonal");
            settings.ExcludedCustomLists.Should().BeEmpty();
        }

        [Test]
        public void should_round_trip_literal_names_and_exclusions_through_provider_settings_json()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal", "seasonal", " Seasonal ", "名前, with comma" };
            _settings.ExcludedCustomLists = new[] { "Excluded", "excluded" };

            var saved = Json.Deserialize<AniListSettings>(Json.ToJson(_settings));

            saved.IncludedCustomLists.Should().Equal(_settings.IncludedCustomLists);
            saved.ExcludedCustomLists.Should().Equal(_settings.ExcludedCustomLists);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void should_merge_legacy_custom_list_after_all_json_fields_are_read(bool systemTextJson, bool legacyFirst)
        {
            var fields = legacyFirst
                ? "\"customList\":\" Legacy \",\"includedCustomLists\":[\"Modern\",\"legacy\",\" Legacy \"],"
                : "\"includedCustomLists\":[\"Modern\",\"legacy\",\" Legacy \"],\"customList\":\" Legacy \",";
            var json = "{" + fields + "\"username\":\"someone\",\"excludedCustomLists\":[\"Manual only\"],\"importPlanning\":false}";
            var settings = systemTextJson ? STJson.Deserialize<AniListSettings>(json) : Json.Deserialize<AniListSettings>(json);

            settings.IncludedCustomLists.Should().Equal("Modern", "legacy", " Legacy ", "Legacy");
            settings.CustomList.Should().BeEmpty();
            settings.ExcludedCustomLists.Should().Equal("Manual only");
            settings.ImportPlanning.Should().BeFalse();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_not_restore_a_removed_modern_tag_from_a_hidden_legacy_selection(bool systemTextJson)
        {
            const string legacy = """
                {"username":"someone","customList":" Seasonal ","includedCustomLists":["Seasonal"]}
                """;
            var settings = systemTextJson ? STJson.Deserialize<AniListSettings>(legacy) : Json.Deserialize<AniListSettings>(legacy);
            settings.IncludedCustomLists.Should().Equal("Seasonal");
            settings.CustomList.Should().BeEmpty();
            settings.IncludedCustomLists = Array.Empty<string>();

            var saved = systemTextJson ? STJson.ToJson(settings) : Json.ToJson(settings);
            var reloaded = systemTextJson ? STJson.Deserialize<AniListSettings>(saved) : Json.Deserialize<AniListSettings>(saved);

            reloaded.IncludedCustomLists.Should().BeEmpty();
            reloaded.CustomList.Should().BeEmpty();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void should_keep_unfiltered_legacy_lists_unfiltered(string customList)
        {
            _settings.CustomList = customList;
            _settings.IncludedCustomLists = null;

            _settings.Validate().IsValid.Should().BeTrue();

            _settings.IncludedCustomLists.Should().BeNull();
            _settings.CustomList.Should().BeEmpty();
        }

        [Test]
        public void should_normalize_the_legacy_api_field_before_validation_and_hide_its_duplicate_menu()
        {
            _settings.CustomList = " Seasonal ";
            _settings.IncludedCustomLists = new[] { "Other", "seasonal", " Seasonal " };

            _settings.Validate().IsValid.Should().BeTrue();

            _settings.IncludedCustomLists.Should().Equal("Other", "seasonal", " Seasonal ", "Seasonal");
            _settings.CustomList.Should().BeEmpty();
            var field = typeof(AniListSettings).GetProperty(nameof(AniListSettings.CustomList))
                .GetCustomAttributes(typeof(FieldDefinitionAttribute), false).Cast<FieldDefinitionAttribute>().Single();
            field.Hidden.Should().Be(HiddenType.Hidden);
            _settings.IncludedCustomLists = Array.Empty<string>();
            _settings.Validate().IsValid.Should().BeTrue();
            _settings.IncludedCustomLists.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_ignore_removed_status_exclusions_in_saved_settings_and_keep_custom_filters(bool systemTextJson)
        {
            const string legacySettings = """
                {"username":"someone","importCurrent":true,"importPlanning":false,"importCompleted":true,"importFinished":true,
                 "includedCustomLists":["Seasonal"],"excludedCustomLists":["Manual only"],
                 "excludeCurrent":true,"excludePlanning":true,"excludeCompleted":true,
                 "excludeDropped":true,"excludePaused":true,"excludeRepeating":true}
                """;
            var settings = systemTextJson
                ? STJson.Deserialize<AniListSettings>(legacySettings)
                : Json.Deserialize<AniListSettings>(legacySettings);

            settings.Username.Should().Be("someone");
            settings.ImportCurrent.Should().BeTrue();
            settings.ImportPlanning.Should().BeFalse();
            settings.ImportCompleted.Should().BeTrue();
            settings.IncludedCustomLists.Should().Equal("Seasonal");
            settings.ExcludedCustomLists.Should().Equal("Manual only");
            typeof(AniListSettings).GetProperties().Select(property => property.Name).Should().NotContain(new[]
            {
                "ExcludeCurrent", "ExcludePlanning", "ExcludeCompleted", "ExcludeDropped", "ExcludePaused", "ExcludeRepeating"
            });

            var saved = systemTextJson ? STJson.ToJson(settings) : Json.ToJson(settings);
            using var json = JsonDocument.Parse(saved);
            json.RootElement.EnumerateObject().Select(property => property.Name).Should().NotContain(new[]
            {
                "excludeCurrent", "excludePlanning", "excludeCompleted", "excludeDropped", "excludePaused", "excludeRepeating"
            });
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
        public void should_allow_a_custom_list_to_be_selected_for_inclusion_and_exclusion()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            _settings.ExcludedCustomLists = new[] { "Seasonal" };

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
