using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    [TestFixture]
    public class QualityProfileCodecFixture : CoreTest<QualityProfileService>
    {
        private QualityProfile _existing;

        [SetUp]
        public void Setup()
        {
            _existing = new QualityProfile
            {
                Id = 7, Name = "Generated existing profile",
                Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = Quality.Bluray2160p, Allowed = true } },
                CodecMultipliers = new CodecMultiplierOverrides { { "HEVC", 2.35m } }
            };
            Mocker.GetMock<IQualityProfileRepository>().Setup(repository => repository.Get(7)).Returns(_existing);
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.All()).Returns(Quality.DefaultQualityDefinitions.ToList());
        }

        [Test]
        public void should_preserve_factors_when_older_clients_omit_them_and_reset_only_on_an_explicit_empty_object()
        {
            var update = new QualityProfile { Id = 7, Name = "Renamed", Items = _existing.Items, CodecMultipliers = null };
            Subject.Update(update);
            update.CodecMultipliers["HEVC"].Should().Be(2.35m);

            update.CodecMultipliers = new CodecMultiplierOverrides();
            Subject.Update(update);
            update.CodecMultipliers.Should().BeEmpty();
        }

        [TestCase("HEVC", 0)]
        [TestCase("HEVC", -1)]
        [TestCase("HEVC", 10.01)]
        [TestCase("Unsupported", 1)]
        public void should_reject_invalid_factors_before_any_profile_write(string codec, double value)
        {
            var update = new QualityProfile { Id = 7, Items = _existing.Items, CodecMultipliers = new CodecMultiplierOverrides { { codec, (decimal)value } } };
            Action action = () => Subject.Update(update);
            action.Should().Throw<ValidationException>();
            Mocker.GetMock<IQualityProfileRepository>().Verify(repository => repository.Update(It.IsAny<QualityProfile>()), Times.Never());
            _existing.CodecMultipliers["HEVC"].Should().Be(2.35m);
        }

        [Test]
        public void should_preserve_an_archived_file_quality_in_new_profiles_instead_of_treating_it_as_unknown()
        {
            var archived = new Quality { Id = 10093, Name = "Generated archived 4K", Source = QualitySource.Bluray, Resolution = 2160, IsRetired = true };
            Quality.Register(archived);
            var definitions = Quality.DefaultQualityDefinitions.ToList();
            definitions.Add(new QualityDefinition(archived) { Id = 93, IsRetired = true, Source = QualitySource.Bluray, Resolution = 2160 });
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.All()).Returns(definitions);
            Mocker.GetMock<NzbDrone.Core.CustomFormats.ICustomFormatService>().Setup(service => service.All()).Returns(new List<NzbDrone.Core.CustomFormats.CustomFormat>());
            var profile = Subject.GetDefaultProfile("Generated new profile", Quality.Bluray1080p, Quality.Bluray1080p);
            var oldFile = new NzbDrone.Core.MediaFiles.EpisodeFile { Quality = new QualityModel(archived) };
            var leaf = QualityProfileSizeLimit.Leaves(profile.Items).Single(item => item.Quality.Id == archived.Id);
            leaf.Allowed.Should().BeFalse();
            leaf.Quality.IsRetired.Should().BeTrue();
            profile.GetIndex(oldFile.Quality.Quality).Index.Should().NotBe(0);
            new QualityModelComparer(profile).Compare(oldFile.Quality, new QualityModel(Quality.Bluray1080p)).Should().BePositive();
            oldFile.Quality.Quality.Id.Should().Be(10093);
        }

        [Test]
        public void should_reject_a_profile_whose_only_allowed_group_contains_retired_qualities()
        {
            var retired = new Quality { Id = 10094, Name = "Generated retired", IsRetired = true };
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.All()).Returns(new List<QualityDefinition> { new QualityDefinition(retired) { IsRetired = true } });
            var profile = new QualityProfile
            {
                Id = 7,
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Id = 1000, Allowed = true, Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = retired, Allowed = false } } }
                }
            };
            Action action = () => Subject.Update(profile);
            action.Should().Throw<ValidationException>();
            Mocker.GetMock<IQualityProfileRepository>().Verify(repository => repository.Update(It.IsAny<QualityProfile>()), Times.Never());
        }
    }
}
