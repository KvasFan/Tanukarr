using System;
using System.Collections.Generic;
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
    public class QualityProfilePartialSizeFixture : CoreTest<QualityProfileService>
    {
        private List<QualityProfile> _profiles;
        private QualityProfileQualityItem _first;
        private QualityProfileQualityItem _second;

        [SetUp]
        public void Setup()
        {
            _first = new QualityProfileQualityItem { Quality = Quality.WEBDL1080p, MinSize = 2, PreferredSize = 5, MaxSize = 10 };
            _second = new QualityProfileQualityItem { Quality = Quality.WEBDL1080p, MinSize = 3, PreferredSize = 6, MaxSize = 12 };
            _profiles = new List<QualityProfile>
            {
                new QualityProfile { Id = 1, Name = "First", Items = new List<QualityProfileQualityItem> { _first } },
                new QualityProfile
                {
                    Id = 2,
                    Name = "Grouped",
                    Items = new List<QualityProfileQualityItem>
                    {
                        new QualityProfileQualityItem
                        {
                            Name = "Outer",
                            Items = new List<QualityProfileQualityItem>
                            {
                                new QualityProfileQualityItem
                                {
                                    Name = "Nested WEB",
                                    Items = new List<QualityProfileQualityItem> { _second }
                                }
                            }
                        }
                    }
                }
            };
            Mocker.GetMock<IQualityProfileRepository>().Setup(repository => repository.All()).Returns(_profiles);
        }

        [Test]
        public void should_update_only_the_explicit_field_and_preserve_mixed_other_fields_inside_nested_groups()
        {
            Subject.UpdateAllSizeLimits(new QualityProfileSizeLimit
            {
                Quality = Quality.WEBDL1080p,
                MinSize = 4,
                UpdateMinSize = true,
                UpdateMaxSize = false,
                UpdatePreferredSize = false
            });

            _first.MinSize.Should().Be(4);
            _second.MinSize.Should().Be(4);
            _first.PreferredSize.Should().Be(5);
            _second.PreferredSize.Should().Be(6);
            _first.MaxSize.Should().Be(10);
            _second.MaxSize.Should().Be(12);
            Mocker.GetMock<IQualityProfileRepository>().Verify(repository => repository.UpdateMany(_profiles), Times.Once());
        }

        [TestCase(null)]
        [TestCase(0d)]
        public void should_allow_explicit_null_or_zero_maximum_without_clearing_minimum_or_preferred(double? maximum)
        {
            Subject.UpdateAllSizeLimits(new QualityProfileSizeLimit
            {
                Quality = Quality.WEBDL1080p,
                MaxSize = maximum,
                UpdateMinSize = false,
                UpdateMaxSize = true,
                UpdatePreferredSize = false
            });

            _first.MaxSize.Should().Be(maximum);
            _second.MaxSize.Should().Be(maximum);
            _first.MinSize.Should().Be(2);
            _second.PreferredSize.Should().Be(6);
        }

        [Test]
        public void should_validate_later_profile_before_mutating_or_writing_an_earlier_valid_profile()
        {
            _first.PreferredSize = 8;
            _second.PreferredSize = 5;
            var update = new QualityProfileSizeLimit
            {
                Quality = Quality.WEBDL1080p,
                MinSize = 6,
                UpdateMaxSize = false,
                UpdatePreferredSize = false
            };

            Action act = () => Subject.UpdateAllSizeLimits(update);

            act.Should().Throw<ValidationException>();
            _first.MinSize.Should().Be(2);
            _second.MinSize.Should().Be(3);
            NoWrite();
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-1d)]
        [TestCase(1001d)]
        public void should_reject_non_finite_or_out_of_range_numbers_without_changing_profiles(double minimum)
        {
            Action act = () => Subject.UpdateAllSizeLimits(new QualityProfileSizeLimit
            {
                Quality = Quality.WEBDL1080p,
                MinSize = minimum,
                UpdateMaxSize = false,
                UpdatePreferredSize = false
            });

            act.Should().Throw<ValidationException>();
            _first.MinSize.Should().Be(2);
            _second.MinSize.Should().Be(3);
            NoWrite();
        }

        [Test]
        public void should_reject_a_missing_quality_without_mutating_any_other_profile()
        {
            _profiles[1].Items.Clear();
            Action act = () => Subject.UpdateAllSizeLimits(new QualityProfileSizeLimit { Quality = Quality.WEBDL1080p, MinSize = 4 });

            act.Should().Throw<ValidationException>();
            _first.MinSize.Should().Be(2);
            NoWrite();
        }

        [Test]
        public void should_distinguish_mixed_values_from_uniform_unlimited_values()
        {
            var summary = QualityProfileSizeLimit.Summarize(_profiles)[Quality.WEBDL1080p.Id];
            summary.MinSize.Should().BeNull();
            summary.MinSizeIsMixed.Should().BeTrue();
            summary.MaxSizeIsMixed.Should().BeTrue();
            summary.PreferredSizeIsMixed.Should().BeTrue();
            QualityProfileSizeLimit.Leaves(_profiles[1].Items).Should().ContainSingle().Which.Should().BeSameAs(_second);

            _first.MinSize = _second.MinSize = null;
            _first.MaxSize = _second.MaxSize = null;
            _first.PreferredSize = _second.PreferredSize = null;
            summary = QualityProfileSizeLimit.Summarize(_profiles)[Quality.WEBDL1080p.Id];
            summary.MinSize.Should().BeNull();
            summary.MinSizeIsMixed.Should().BeFalse();
            summary.MaxSizeIsMixed.Should().BeFalse();
            summary.PreferredSizeIsMixed.Should().BeFalse();
        }

        [Test]
        public void should_summarize_legacy_zero_and_null_maxima_as_the_same_effective_unlimited_value_without_rewriting_profiles()
        {
            _first.MaxSize = 0;
            _second.MaxSize = null;

            var summary = QualityProfileSizeLimit.Summarize(_profiles)[Quality.WEBDL1080p.Id];

            summary.MaxSize.Should().BeNull();
            summary.MaxSizeIsMixed.Should().BeFalse();
            summary.MinSizeIsMixed.Should().BeTrue();
            summary.PreferredSizeIsMixed.Should().BeTrue();
            _first.MaxSize.Should().Be(0);
            _second.MaxSize.Should().BeNull();
            NoWrite();
        }

        [Test]
        public void should_not_query_or_write_profiles_for_an_empty_size_batch()
        {
            Subject.UpdateAllSizeLimits();

            Mocker.GetMock<IQualityProfileRepository>().Verify(repository => repository.All(), Times.Never());
            NoWrite();
        }

        private void NoWrite()
        {
            Mocker.GetMock<IQualityProfileRepository>()
                .Verify(repository => repository.UpdateMany(It.IsAny<IList<QualityProfile>>()), Times.Never());
        }
    }
}
