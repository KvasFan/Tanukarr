using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using FluentValidation;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Custom;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;
using NzbDrone.Test.Common;
using Sonarr.Http.ClientSchema;
using V3Controller = Sonarr.Api.V3.ImportLists.ImportListController;
using V3Mapper = Sonarr.Api.V3.ImportLists.ImportListResourceMapper;
using V3Resource = Sonarr.Api.V3.ImportLists.ImportListResource;
using V5Controller = Sonarr.Api.V5.ImportLists.ImportListController;
using V5Mapper = Sonarr.Api.V5.ImportLists.ImportListResourceMapper;
using V5Resource = Sonarr.Api.V5.ImportLists.ImportListResource;

namespace NzbDrone.Api.Test.ImportLists
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class ImportListMetadataSourceResourceFixture : TestBase
    {
        private readonly bool _useV3;

        public ImportListMetadataSourceResourceFixture(bool useV3)
        {
            _useV3 = useV3;
        }

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IImportListFactory>().Setup(factory => factory.All()).Returns(new List<ImportListDefinition>());
            Mocker.GetMock<ILocalizationService>()
                .Setup(service => service.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Returns<string, Dictionary<string, object>>((key, _) => key);
            SchemaBuilder.Initialize(Mocker.Container);
        }

        [TestCase(ImportListMetadataSource.Automatic, "automatic")]
        [TestCase(ImportListMetadataSource.Tvdb, "tvdb")]
        [TestCase(ImportListMetadataSource.Tmdb, "tmdb")]
        [TestCase(ImportListMetadataSource.AniList, "aniList")]
        [TestCase(ImportListMetadataSource.MyAnimeList, "myAnimeList")]
        public void should_round_trip_catalog_preferences_through_both_real_serializers(ImportListMetadataSource preference, string expected)
        {
            foreach (var systemTextJson in new[] { true, false })
            {
                var existing = Definition(preference);
                var json = systemTextJson ? STJson.ToJson(Resource(existing)) : Json.ToJson(Resource(existing));
                JObject.Parse(json)["preferredMetadataSource"].Value<string>().Should().Be(expected);
                Read(json, systemTextJson, existing).PreferredMetadataSource.Should().Be(preference);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_preserve_a_saved_preference_when_an_older_client_omits_it(bool systemTextJson)
        {
            var existing = Definition(ImportListMetadataSource.MyAnimeList);
            var document = JObject.Parse(STJson.ToJson(Resource(existing)));
            document.Remove("preferredMetadataSource");

            Read(document.ToString(), systemTextJson, existing).PreferredMetadataSource.Should().Be(ImportListMetadataSource.MyAnimeList);
            Read(document.ToString(), systemTextJson, null).PreferredMetadataSource.Should().Be(ImportListMetadataSource.Automatic);
        }

        [Test]
        public void should_allow_explicitly_resetting_an_existing_preference_to_automatic()
        {
            var existing = Definition(ImportListMetadataSource.MyAnimeList);
            var document = JObject.Parse(STJson.ToJson(Resource(existing)));
            document["preferredMetadataSource"] = "automatic";

            Read(document.ToString(), true, existing).PreferredMetadataSource.Should().Be(ImportListMetadataSource.Automatic);
            Read(document.ToString(), false, existing).PreferredMetadataSource.Should().Be(ImportListMetadataSource.Automatic);
        }

        [TestCase(-1)]
        [TestCase(99)]
        public void should_reject_unknown_values_in_the_actual_shared_controller_validator(int preference)
        {
            if (_useV3)
            {
                var controller = Mocker.Resolve<V3Controller>();
                var validator = (IValidator<V3Resource>)controller.GetType().GetProperty("SharedValidator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                validator.Validate(new V3Resource { PreferredMetadataSource = (ImportListMetadataSource)preference })
                    .Errors.Should().Contain(error => error.PropertyName == "PreferredMetadataSource");
            }
            else
            {
                var controller = Mocker.Resolve<V5Controller>();
                var validator = (IValidator<V5Resource>)controller.GetType().GetProperty("SharedValidator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                validator.Validate(new V5Resource { PreferredMetadataSource = (ImportListMetadataSource)preference })
                    .Errors.Should().Contain(error => error.PropertyName == "PreferredMetadataSource");
            }
        }

        private static ImportListDefinition Definition(ImportListMetadataSource preference)
        {
            return new ImportListDefinition
            {
                Id = 7,
                Name = "Generated preference fixture",
                Implementation = "CustomImport",
                ConfigContract = nameof(CustomSettings),
                Settings = new CustomSettings { BaseUrl = "https://fixture.invalid" },
                PreferredMetadataSource = preference,
                Language = Language.English
            };
        }

        private object Resource(ImportListDefinition definition)
        {
            return _useV3 ? new V3Mapper().ToResource(definition) : new V5Mapper().ToResource(definition);
        }

        private ImportListDefinition Read(string json, bool systemTextJson, ImportListDefinition existing)
        {
            return _useV3
                ? new V3Mapper().ToModel(systemTextJson ? STJson.Deserialize<V3Resource>(json) : Json.Deserialize<V3Resource>(json), existing)
                : new V5Mapper().ToModel(systemTextJson ? STJson.Deserialize<V5Resource>(json) : Json.Deserialize<V5Resource>(json), existing);
        }
    }
}
