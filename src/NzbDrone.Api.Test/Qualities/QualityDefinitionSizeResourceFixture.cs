using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Test.Common;
using V3Controller = Sonarr.Api.V3.Qualities.QualityDefinitionController;
using V3Profile = Sonarr.Api.V3.Profiles.Quality.QualityProfileResource;
using V3ProfileItem = Sonarr.Api.V3.Profiles.Quality.QualityProfileQualityItemResource;
using V3ProfileValidator = Sonarr.Api.V3.Profiles.Quality.QualityProfileResourceValidator;
using V3Resource = Sonarr.Api.V3.Qualities.QualityDefinitionResource;
using V5Controller = Sonarr.Api.V5.Qualities.QualityDefinitionController;
using V5Profile = Sonarr.Api.V5.Profiles.Quality.QualityProfileResource;
using V5ProfileItem = Sonarr.Api.V5.Profiles.Quality.QualityProfileQualityItemResource;
using V5ProfileValidator = Sonarr.Api.V5.Profiles.Quality.QualityProfileResourceValidator;
using V5Resource = Sonarr.Api.V5.Qualities.QualityDefinitionResource;

namespace NzbDrone.Api.Test.Qualities
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class QualityDefinitionSizeResourceFixture : TestBase
    {
        private readonly bool _useV3;
        private List<QualityDefinitionSizeUpdate> _updates;

        public QualityDefinitionSizeResourceFixture(bool useV3)
        {
            _useV3 = useV3;
        }

        [SetUp]
        public void Setup()
        {
            _updates = null;
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.All())
                .Returns(new List<QualityDefinition> { new QualityDefinition(Quality.WEBDL1080p) { Id = 7, Weight = 12 } });
            Mocker.GetMock<IQualityProfileService>().Setup(service => service.All())
                .Returns(new List<QualityProfile>
                {
                    new QualityProfile
                    {
                        Items = new List<QualityProfileQualityItem>
                        {
                            new QualityProfileQualityItem { Quality = Quality.WEBDL1080p, MinSize = 2, PreferredSize = 5, MaxSize = 10 }
                        }
                    },
                    new QualityProfile
                    {
                        Items = new List<QualityProfileQualityItem>
                        {
                            new QualityProfileQualityItem
                            {
                                Name = "Grouped WEB",
                                Items = new List<QualityProfileQualityItem>
                                {
                                    new QualityProfileQualityItem { Quality = Quality.WEBDL1080p, MinSize = 3, PreferredSize = 5, MaxSize = 12 }
                                }
                            }
                        }
                    }
                });
            Mocker.GetMock<IQualityDefinitionUpdateService>().Setup(service => service.UpdateMany(It.IsAny<List<QualityDefinitionSizeUpdate>>()))
                .Callback<List<QualityDefinitionSizeUpdate>>(updates => _updates = updates);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_preserve_omitted_size_fields_through_both_real_serializers_and_bulk_actions(bool systemTextJson)
        {
            Update("{\"id\":7,\"title\":\"Title only\"}", systemTextJson);

            _updates.Should().ContainSingle();
            _updates[0].UpdateMinSize.Should().BeFalse();
            _updates[0].UpdateMaxSize.Should().BeFalse();
            _updates[0].UpdatePreferredSize.Should().BeFalse();
            _updates[0].Definition.Title.Should().Be("Title only");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_keep_explicit_null_distinct_from_omission_and_ignore_forged_tracking_flags(bool systemTextJson)
        {
            Update("{\"id\":7,\"maxSize\":null,\"maxSizeSpecified\":false,\"minSizeSpecified\":true}", systemTextJson);

            _updates[0].UpdateMaxSize.Should().BeTrue();
            _updates[0].Definition.MaxSize.Should().BeNull();
            _updates[0].UpdateMinSize.Should().BeFalse();
            _updates[0].UpdatePreferredSize.Should().BeFalse();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_keep_api_values_in_mebibytes_per_minute_and_apply_only_the_edited_field(bool systemTextJson)
        {
            Update("{\"id\":7,\"minSize\":3.1}", systemTextJson);

            _updates[0].Definition.MinSize.Should().Be(3.1);
            _updates[0].UpdateMinSize.Should().BeTrue();
            _updates[0].UpdateMaxSize.Should().BeFalse();
            _updates[0].UpdatePreferredSize.Should().BeFalse();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_preserve_all_mixed_limits_when_a_legacy_client_echoes_get_and_changes_only_title(bool systemTextJson)
        {
            var document = MixedDefinition(systemTextJson);
            document["title"] = "Renamed by a legacy client";

            Update(document.ToString(Formatting.None), systemTextJson);

            _updates.Should().ContainSingle();
            _updates[0].Definition.Title.Should().Be("Renamed by a legacy client");
            _updates[0].UpdateMinSize.Should().BeFalse();
            _updates[0].UpdateMaxSize.Should().BeFalse();
            _updates[0].UpdatePreferredSize.Should().BeFalse();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_apply_an_edited_numeric_limit_in_a_full_get_echo_while_preserving_other_mixed_nulls(bool systemTextJson)
        {
            var document = MixedDefinition(systemTextJson);
            document["minSize"] = 4.0;
            document["minSizeIsMixed"].Value<bool>().Should().BeTrue();

            Update(document.ToString(Formatting.None), systemTextJson);

            _updates.Should().ContainSingle();
            _updates[0].Definition.MinSize.Should().Be(4.0);
            _updates[0].UpdateMinSize.Should().BeTrue();
            _updates[0].UpdateMaxSize.Should().BeFalse();
            _updates[0].UpdatePreferredSize.Should().BeFalse();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_never_expose_internal_specified_flags_in_serialized_api_resources(bool systemTextJson)
        {
            object resource = _useV3
                ? new V3Resource { Id = 7, MinSize = 3, MaxSize = null }
                : new V5Resource { Id = 7, MinSize = 3, MaxSize = null };
            var document = JObject.Parse(Serialize(resource, systemTextJson));

            document["minSize"].Value<double>().Should().Be(3);
            document["maxSize"].Type.Should().Be(JTokenType.Null);
            document["minSizeSpecified"].Should().BeNull();
            document["maxSizeSpecified"].Should().BeNull();
            document["preferredSizeSpecified"].Should().BeNull();
        }

        [Test]
        public void should_return_real_profile_limits_and_separate_mixed_flags_when_definition_columns_are_absent()
        {
            object resources = _useV3 ? Mocker.Resolve<V3Controller>().GetAll() : Mocker.Resolve<V5Controller>().GetAll().Value;
            var row = JArray.Parse(Serialize(resources, false))[0];

            row["minSize"].Type.Should().Be(JTokenType.Null);
            row["minSizeIsMixed"].Value<bool>().Should().BeTrue();
            row["maxSizeIsMixed"].Value<bool>().Should().BeTrue();
            row["preferredSize"].Value<double>().Should().Be(5);
            row["preferredSizeIsMixed"].Value<bool>().Should().BeFalse();
        }

        [Test]
        public void should_return_the_backend_codec_policy_catalog_without_copying_or_mutating_weights()
        {
            var result = _useV3 ? Mocker.Resolve<V3Controller>().GetCodecs() : Mocker.Resolve<V5Controller>().GetCodecs();

            result.Value.Should().BeSameAs(CodecSizeNormalizer.MultiplierCatalog);
            var options = result.SerializerSettings.Should().BeOfType<global::System.Text.Json.JsonSerializerOptions>().Which;
            options.DictionaryKeyPolicy.Should().BeNull();
            var json = global::System.Text.Json.JsonSerializer.Serialize(result.Value, result.Value.GetType(), options);
            var catalog = JObject.Parse(json);
            catalog.Properties().Select(property => property.Name).Should().BeEquivalentTo(CodecSizeNormalizer.MultiplierCatalog.Keys);
            catalog["MPEG-2 / H.262 / x262"].Value<decimal>().Should().Be(0.50m);
            catalog["H.264 / AVC / x264"].Value<decimal>().Should().Be(1.00m);
            catalog["H.265 / HEVC / x265"].Value<decimal>().Should().Be(1.67m);
            catalog["AV1"].Value<decimal>().Should().Be(2.22m);
        }

        [Test]
        public void should_reject_minimum_above_finite_maximum_with_null_preferred_inside_nested_groups()
        {
            ValidateProfile(2, null, 1, true).Should().BeFalse();
            ValidateProfile(2, null, 1, false).Should().BeFalse();
        }

        [Test]
        public void should_accept_zero_or_null_unlimited_maximum_with_nonzero_preferred()
        {
            ValidateProfile(2, 10, 0, true).Should().BeTrue();
            ValidateProfile(2, 10, null, true).Should().BeTrue();
        }

        [TestCase("min", double.NaN)]
        [TestCase("preferred", double.PositiveInfinity)]
        [TestCase("max", double.NegativeInfinity)]
        [TestCase("min", -1d)]
        [TestCase("preferred", 1001d)]
        [TestCase("max", 1001d)]
        public void should_reject_non_finite_negative_or_excessive_values_in_actual_profile_validators(string field, double value)
        {
            ValidateProfile(field == "min" ? value : 2, field == "preferred" ? value : 5, field == "max" ? value : 10, true)
                .Should().BeFalse();
        }

        private JObject MixedDefinition(bool systemTextJson)
        {
            var profiles = Mocker.GetMock<IQualityProfileService>().Object.All();
            QualityProfileSizeLimit.Leaves(profiles[1].Items).Single().PreferredSize = 6;
            object resources = _useV3 ? Mocker.Resolve<V3Controller>().GetAll() : Mocker.Resolve<V5Controller>().GetAll().Value;
            var document = (JObject)JArray.Parse(Serialize(resources, systemTextJson))[0];
            foreach (var field in new[] { "minSize", "preferredSize", "maxSize" })
            {
                document[field].Type.Should().Be(JTokenType.Null);
                document[field + "IsMixed"].Value<bool>().Should().BeTrue();
            }

            return document;
        }

        private void Update(string json, bool systemTextJson)
        {
            if (_useV3)
            {
                Mocker.Resolve<V3Controller>().UpdateMany(new List<V3Resource> { Deserialize<V3Resource>(json, systemTextJson) });
            }
            else
            {
                Mocker.Resolve<V5Controller>().UpdateMany(new List<V5Resource> { Deserialize<V5Resource>(json, systemTextJson) });
            }
        }

        private bool ValidateProfile(double? minimum, double? preferred, double? maximum, bool grouped)
        {
            if (_useV3)
            {
                var leaf = new V3ProfileItem { Quality = Quality.WEBDL1080p, MinSize = minimum, PreferredSize = preferred, MaxSize = maximum };
                var items = grouped
                    ? new List<V3ProfileItem> { new V3ProfileItem { Items = new List<V3ProfileItem> { leaf } } }
                    : new List<V3ProfileItem> { leaf };
                return new V3ProfileValidator().Validate(new V3Profile { Items = items }).IsValid;
            }

            var v5Leaf = new V5ProfileItem { Quality = Quality.WEBDL1080p, MinSize = minimum, PreferredSize = preferred, MaxSize = maximum };
            var v5Items = grouped
                ? new List<V5ProfileItem> { new V5ProfileItem { Items = new List<V5ProfileItem> { v5Leaf } } }
                : new List<V5ProfileItem> { v5Leaf };
            return new V5ProfileValidator().Validate(new V5Profile { Items = v5Items }).IsValid;
        }

        private static T Deserialize<T>(string json, bool systemTextJson)
        {
            return systemTextJson
                ? global::System.Text.Json.JsonSerializer.Deserialize<T>(json, new global::System.Text.Json.JsonSerializerOptions(global::System.Text.Json.JsonSerializerDefaults.Web))
                : JsonConvert.DeserializeObject<T>(json);
        }

        private static string Serialize(object resource, bool systemTextJson)
        {
            return systemTextJson
                ? global::System.Text.Json.JsonSerializer.Serialize(resource, resource.GetType(), new global::System.Text.Json.JsonSerializerOptions(global::System.Text.Json.JsonSerializerDefaults.Web))
                : JsonConvert.SerializeObject(resource, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
        }
    }
}
