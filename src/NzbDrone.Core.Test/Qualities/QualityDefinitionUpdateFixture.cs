using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Qualities
{
    [TestFixture]
    public class QualityDefinitionUpdateFixture : CoreTest<QualityDefinitionUpdateService>
    {
        private QualityDefinition _existing;
        private List<QualityDefinition> _saved;
        private QualityProfileSizeLimit[] _limits;

        [SetUp]
        public void Setup()
        {
            _existing = new QualityDefinition(Quality.WEBDL1080p) { Id = 7, Weight = 12, Title = "Existing" };
            _saved = null;
            _limits = null;
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.All())
                .Returns(new List<QualityDefinition> { _existing });
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.UpdateMany(It.IsAny<List<QualityDefinition>>()))
                .Callback<List<QualityDefinition>>(models => _saved = models);
            Mocker.GetMock<IQualityProfileService>().Setup(service => service.UpdateAllSizeLimits(It.IsAny<QualityProfileSizeLimit[]>()))
                .Callback<QualityProfileSizeLimit[]>(limits => _limits = limits);
        }

        [Test]
        public void should_rename_a_definition_without_touching_any_profile_size_limit()
        {
            Subject.UpdateMany(new List<QualityDefinitionSizeUpdate>
            {
                new QualityDefinitionSizeUpdate { Definition = new QualityDefinition { Id = 7, Title = "Renamed", Weight = 999 } }
            });

            _saved.Should().ContainSingle();
            _saved[0].Title.Should().Be("Renamed");
            _saved[0].Weight.Should().Be(12);
            _saved[0].Quality.Should().Be(Quality.WEBDL1080p);
            _existing.Title.Should().Be("Existing");
            _limits.Should().BeNull();
        }

        [Test]
        public void should_pass_explicit_null_and_only_its_field_flag_to_profile_persistence()
        {
            Subject.UpdateMany(new List<QualityDefinitionSizeUpdate>
            {
                new QualityDefinitionSizeUpdate
                {
                    Definition = new QualityDefinition { Id = 7, MaxSize = null },
                    UpdateMaxSize = true
                }
            });

            _limits.Should().ContainSingle();
            _limits[0].Quality.Should().Be(Quality.WEBDL1080p);
            _limits[0].MaxSize.Should().BeNull();
            _limits[0].UpdateMaxSize.Should().BeTrue();
            _limits[0].UpdateMinSize.Should().BeFalse();
            _limits[0].UpdatePreferredSize.Should().BeFalse();
            _saved[0].Title.Should().Be("Existing");
        }

        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("quality")]
        [TestCase("blank-title")]
        public void should_validate_the_whole_batch_before_any_profile_or_definition_write(string failure)
        {
            var request = new QualityDefinition { Id = 7, Quality = Quality.WEBDL1080p, Title = "Valid", MinSize = 3 };
            var updates = new List<QualityDefinitionSizeUpdate>
            {
                new QualityDefinitionSizeUpdate { Definition = request, UpdateMinSize = true }
            };
            if (failure == "missing")
            {
                updates.Add(new QualityDefinitionSizeUpdate { Definition = new QualityDefinition { Id = 999 } });
            }
            else if (failure == "duplicate")
            {
                updates.Add(new QualityDefinitionSizeUpdate { Definition = request });
            }
            else if (failure == "quality")
            {
                request.Quality = Quality.HDTV720p;
            }
            else
            {
                request.Title = "  ";
            }

            Action act = () => Subject.UpdateMany(updates);

            act.Should().Throw<ValidationException>();
            _limits.Should().BeNull();
            _saved.Should().BeNull();
            _existing.Title.Should().Be("Existing");
        }

        [Test]
        public void should_propagate_merged_profile_validation_failure_without_writing_definition_titles()
        {
            Mocker.GetMock<IQualityProfileService>()
                .Setup(service => service.UpdateAllSizeLimits(It.IsAny<QualityProfileSizeLimit[]>()))
                .Throws(new ValidationException("A later profile would have minimum above preferred"));
            Action act = () => Subject.UpdateMany(new List<QualityDefinitionSizeUpdate>
            {
                new QualityDefinitionSizeUpdate
                {
                    Definition = new QualityDefinition { Id = 7, Title = "Not saved", MinSize = 9 },
                    UpdateMinSize = true
                }
            });

            act.Should().Throw<ValidationException>();
            _saved.Should().BeNull();
            _existing.Title.Should().Be("Existing");
        }

        [Test]
        public void should_reject_a_title_swap_before_any_profile_write_instead_of_triggering_a_unique_constraint()
        {
            var other = new QualityDefinition(Quality.HDTV720p) { Id = 8, Title = "Other" };
            Mocker.GetMock<IQualityDefinitionService>().Setup(service => service.All()).Returns(new List<QualityDefinition> { _existing, other });
            Action action = () => Subject.UpdateMany(new List<QualityDefinitionSizeUpdate>
            {
                new QualityDefinitionSizeUpdate { Definition = new QualityDefinition { Id = 7, Title = "Other", MinSize = 3 }, UpdateMinSize = true },
                new QualityDefinitionSizeUpdate { Definition = new QualityDefinition { Id = 8, Title = "Existing" } }
            });
            action.Should().Throw<ValidationException>();
            _saved.Should().BeNull();
            _limits.Should().BeNull();
            _existing.Title.Should().Be("Existing");
            other.Title.Should().Be("Other");
        }
    }
}
