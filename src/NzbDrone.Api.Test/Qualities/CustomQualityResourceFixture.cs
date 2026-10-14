using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Qualities;
using NzbDrone.Test.Common;
using V3Profile = Sonarr.Api.V3.Profiles.Quality.QualityProfileResource;
using V5Profile = Sonarr.Api.V5.Profiles.Quality.QualityProfileResource;

namespace NzbDrone.Api.Test.Qualities
{
    [TestFixture]
    public class CustomQualityResourceFixture : TestBase
    {
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void should_preserve_canonical_codec_keys_and_values_through_both_real_serializer_policies(bool v3, bool systemTextJson)
        {
            var factors = new CodecMultiplierOverrides { { "HEVC", 2.35m }, { "AV1", 3.10m }, { "Unknown", 0.90m } };
            object resource = v3 ? new V3Profile { CodecMultipliers = factors } : new V5Profile { CodecMultipliers = factors };
            var json = systemTextJson
                ? global::System.Text.Json.JsonSerializer.Serialize(resource, resource.GetType(), STJson.GetSerializerSettings())
                : JsonConvert.SerializeObject(resource, Json.GetSerializerSettings());
            var document = JObject.Parse(json);
            document["codecMultipliers"]["HEVC"].Value<decimal>().Should().Be(2.35m);
            document["codecMultipliers"]["AV1"].Value<decimal>().Should().Be(3.10m);
            document["codecMultipliers"]["Unknown"].Value<decimal>().Should().Be(0.90m);
            var restored = systemTextJson
                ? global::System.Text.Json.JsonSerializer.Deserialize<CodecMultiplierOverrides>(document["codecMultipliers"].ToString(), STJson.GetSerializerSettings())
                : Json.Deserialize<CodecMultiplierOverrides>(document["codecMultipliers"].ToString());
            restored.Should().BeEquivalentTo(factors);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_distinguish_omitted_profile_factors_from_an_explicit_default_reset(bool v3)
        {
            if (v3)
            {
                Json.Deserialize<V3Profile>("{\"id\":7}").CodecMultipliers.Should().BeNull();
                Json.Deserialize<V3Profile>("{\"id\":7,\"codecMultipliers\":{}}").CodecMultipliers.Should().BeEmpty();
            }
            else
            {
                STJson.Deserialize<V5Profile>("{\"id\":7}").CodecMultipliers.Should().BeNull();
                STJson.Deserialize<V5Profile>("{\"id\":7,\"codecMultipliers\":{}}").CodecMultipliers.Should().BeEmpty();
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_reject_unknown_families_and_invalid_values_with_profile_validation(bool v3)
        {
            var factors = new CodecMultiplierOverrides { { "HEVC", 0m }, { "not-a-codec", 1m } };
            var valid = v3
                ? new Sonarr.Api.V3.Profiles.Quality.QualityProfileResourceValidator().Validate(new V3Profile { Items = new List<Sonarr.Api.V3.Profiles.Quality.QualityProfileQualityItemResource>(), CodecMultipliers = factors }).IsValid
                : new Sonarr.Api.V5.Profiles.Quality.QualityProfileResourceValidator().Validate(new V5Profile { CodecMultipliers = factors }).IsValid;
            valid.Should().BeFalse();
        }

        [Test]
        public void should_deserialize_legacy_camel_case_families_to_canonical_names_for_persisted_settings()
        {
            var restored = STJson.Deserialize<CodecMultiplierOverrides>("{\"hevc\":2.4,\"h264\":1.2,\"unknown\":0.8}");
            restored.Keys.Should().BeEquivalentTo("HEVC", "H264", "Unknown");
            restored["HEVC"].Should().Be(2.4m);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void should_expose_retirement_without_changing_a_historical_quality_identity(bool v3, bool systemTextJson)
        {
            var definition = new QualityDefinition(new Quality { Id = 10051, Name = "Generated archived quality", Source = QualitySource.Bluray, Resolution = 2160, IsRetired = true }) { Id = 51, IsRetired = true };
            object resource = v3
                ? Sonarr.Api.V3.Qualities.QualityDefinitionResourceMapper.ToResource(definition)
                : Sonarr.Api.V5.Qualities.QualityDefinitionResourceMapper.ToResource(definition);
            var json = systemTextJson
                ? global::System.Text.Json.JsonSerializer.Serialize(resource, resource.GetType(), STJson.GetSerializerSettings())
                : JsonConvert.SerializeObject(resource, Json.GetSerializerSettings());
            var document = JObject.Parse(json);
            document["quality"]["id"].Value<int>().Should().Be(10051);
            document["quality"]["isRetired"].Value<bool>().Should().BeTrue();
            document["isRetired"].Value<bool>().Should().BeTrue();
            Quality.All.Should().NotContain(quality => quality.Id == 10051);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void should_return_dynamic_quality_weights_for_new_and_archived_manual_import_entries(bool v3, bool retired)
        {
            var custom = new Quality { Id = 10001, Name = "Generated custom import", Source = QualitySource.Bluray, Resolution = 2160, IsRetired = retired };
            var items = new List<NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportItem>
            {
                new NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportItem
                {
                    Path = "/generated/incoming/Generated.Show.S01E01.mkv", Quality = new QualityModel(custom, new Revision(2, 1)),
                    Episodes = new List<NzbDrone.Core.Tv.Episode>()
                }
            };
            Mocker.GetMock<NzbDrone.Core.MediaFiles.EpisodeImport.Manual.IManualImportService>()
                .Setup(service => service.GetMediaFiles("/generated/incoming", null, null, false)).Returns(items);
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.Get(custom))
                .Returns(new QualityDefinition(custom) { Weight = 19, IsRetired = retired });
            object resources = v3
                ? Mocker.Resolve<Sonarr.Api.V3.ManualImport.ManualImportController>().GetMediaFiles("/generated/incoming", null, null, null, false)
                : Mocker.Resolve<Sonarr.Api.V5.ManualImport.ManualImportController>().GetMediaFiles("/generated/incoming", null, null, null, false).Value;
            var document = JArray.Parse(JsonConvert.SerializeObject(resources, Json.GetSerializerSettings()));
            document[0]["quality"]["quality"]["id"].Value<int>().Should().Be(10001);
            document[0]["quality"]["quality"]["isRetired"].Value<bool>().Should().Be(retired);
            document[0]["qualityWeight"].Value<int>().Should().Be(31);
            Quality.All.Should().NotContain(quality => quality.Id == 10001);
        }
    }
}
