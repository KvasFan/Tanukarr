using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Qualities
{
    [TestFixture]
    public class CustomQualityCatalogueFixture : CoreTest<QualityDefinitionService>
    {
        private List<QualityDefinition> _definitions;
        private List<QualityProfile> _profiles;

        [SetUp]
        public void Setup()
        {
            _definitions = Quality.DefaultQualityDefinitions.Select((definition, index) => new QualityDefinition(definition.Quality) { Id = index + 1 }).ToList();
            _profiles = new List<QualityProfile>
            {
                new QualityProfile
                {
                    Id = 7, Name = "Generated profile", Cutoff = Quality.Bluray1080p.Id,
                    Items = new List<QualityProfileQualityItem>
                    {
                        new QualityProfileQualityItem { Quality = Quality.Bluray2160p, Allowed = false, MinSize = 4, PreferredSize = 90, MaxSize = 100 },
                        new QualityProfileQualityItem { Quality = Quality.Bluray1080p, Allowed = true }
                    }
                }
            };
            Mocker.GetMock<IQualityDefinitionRepository>().Setup(repository => repository.All()).Returns(() => _definitions);
            Mocker.GetMock<IQualityDefinitionRepository>().Setup(repository => repository.Insert(It.IsAny<QualityDefinition>()))
                .Returns<QualityDefinition>(definition =>
                {
                    definition.Id = _definitions.Max(existing => existing.Id) + 1;
                    _definitions.Add(definition);
                    return definition;
                });
            Mocker.GetMock<IQualityProfileRepository>().Setup(repository => repository.All()).Returns(_profiles);
        }

        [Test]
        public void should_create_and_copy_independent_quality_ids_without_changing_existing_profiles_or_sizes()
        {
            var original = _definitions.Single(definition => definition.Quality == Quality.Bluray2160p);
            var added = Subject.Add(
                new QualityDefinition
                {
                    Title = "Generated BdRip4k", Source = QualitySource.Bluray, Resolution = 2160, Codec = "HEVC", TitlePattern = "BDRip"
                },
                original.Id);
            added.Quality.Id.Should().BeGreaterThanOrEqualTo(Quality.CustomIdStart);
            var leaf = _profiles[0].Items.Last();
            leaf.Allowed.Should().BeFalse();
            leaf.Quality.Should().Be(added.Quality);
            leaf.MinSize.Should().Be(4);
            leaf.PreferredSize.Should().Be(90);
            leaf.MaxSize.Should().Be(100);
            _profiles[0].Items[0].Quality.Should().Be(Quality.Bluray2160p);
            _profiles[0].Cutoff.Should().Be(Quality.Bluray1080p.Id);
        }

        [Test]
        public void should_retain_custom_and_retired_definitions_across_startup()
        {
            var custom = new QualityDefinition(new Quality { Id = 10042, Name = "Generated custom", Source = QualitySource.Web, Resolution = 720 })
            {
                Id = 42, Source = QualitySource.Web, Resolution = 720, IsRetired = true
            };
            _definitions.Add(custom);
            Subject.Handle(new ApplicationStartedEvent());
            Mocker.GetMock<IQualityDefinitionRepository>().Verify(repository => repository.DeleteMany(It.Is<List<QualityDefinition>>(definitions => definitions.Contains(custom))), Times.Never());
            Subject.GetById(42).IsRetired.Should().BeTrue();
            Quality.FindById(10042).Name.Should().Be("Generated custom");
        }

        [Test]
        public void should_retire_without_deleting_identity_and_never_reuse_that_id()
        {
            var first = Subject.Add(new QualityDefinition { Title = "Generated first", Source = QualitySource.Web, Resolution = 720 });
            Subject.Retire(first.Id);
            first.IsRetired.Should().BeTrue();
            Quality.FindById(first.Quality.Id).Id.Should().Be(first.Quality.Id);
            Quality.FindById(first.Quality.Id).IsRetired.Should().BeTrue();
            var next = Subject.Add(new QualityDefinition { Title = "Generated next", Source = QualitySource.Web, Resolution = 720 });
            next.Quality.Id.Should().BeGreaterThan(first.Quality.Id);
            Mocker.GetMock<IQualityDefinitionRepository>().Verify(repository => repository.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_reject_deletion_of_an_allowed_quality_or_leaf_or_group_cutoff()
        {
            var original = _definitions.Single(definition => definition.Quality == Quality.Bluray2160p);
            _profiles[0].Items[0].Allowed = true;
            Action action = () => Subject.Retire(original.Id);
            action.Should().Throw<ValidationException>();
            _profiles[0].Items[0].Allowed = false;
            _profiles[0].Cutoff = Quality.Bluray2160p.Id;
            action.Should().Throw<ValidationException>();
            _profiles[0].Items[0] = new QualityProfileQualityItem { Id = 1000, Items = new List<QualityProfileQualityItem> { _profiles[0].Items[0] } };
            _profiles[0].Cutoff = 1000;
            action.Should().Throw<ValidationException>();
            original.IsRetired.Should().BeFalse();
        }

        [TestCase("invalid-source")]
        [TestCase("invalid-resolution")]
        [TestCase("invalid-regex")]
        [TestCase("long-regex")]
        [TestCase("unknown-codec")]
        public void should_validate_custom_parameters_before_writes(string failure)
        {
            var definition = new QualityDefinition { Title = "Generated valid", Source = QualitySource.Bluray, Resolution = 2160 };
            switch (failure)
            {
                case "invalid-source": definition.Source = (QualitySource)999; break;
                case "invalid-resolution": definition.Resolution = 10000; break;
                case "invalid-regex": definition.TitlePattern = "["; break;
                case "long-regex": definition.TitlePattern = new string('x', 257); break;
                case "unknown-codec": definition.Codec = "generated-unknown"; break;
            }

            Action action = () => Subject.Add(definition);
            action.Should().Throw<ValidationException>();
            Mocker.GetMock<IQualityDefinitionRepository>().Verify(repository => repository.Insert(It.IsAny<QualityDefinition>()), Times.Never());
        }

        [Test]
        public void should_allow_retirement_from_a_disabled_group_and_restore_the_same_quality_identity()
        {
            var custom = Subject.Add(new QualityDefinition { Title = "Generated archived group member", Source = QualitySource.Web, Resolution = 720 });
            var leaf = _profiles[0].Items.Last();
            leaf.Allowed = true;
            _profiles[0].Items.Remove(leaf);
            _profiles[0].Items.Add(new QualityProfileQualityItem { Id = 1000, Allowed = false, Items = new List<QualityProfileQualityItem> { leaf } });
            Subject.Retire(custom.Id);
            custom.IsRetired.Should().BeTrue();
            var restored = Subject.Restore(custom.Id);
            restored.Quality.Id.Should().Be(custom.Quality.Id);
            restored.IsRetired.Should().BeFalse();
            restored.Quality.IsRetired.Should().BeFalse();
        }
    }
}
