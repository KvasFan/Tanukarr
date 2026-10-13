using System;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Test.Common;
using V3Mapper = Sonarr.Api.V3.Config.NamingConfigResourceMapper;
using V3Resource = Sonarr.Api.V3.Config.NamingConfigResource;
using V5Mapper = Sonarr.Api.V5.Settings.NamingConfigResourceMapper;
using V5Resource = Sonarr.Api.V5.Settings.NamingSettingsResource;

namespace NzbDrone.Api.Test.ConfigTests
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class AnimeGroupNamingResourceFixture : TestBase
    {
        private readonly bool _useV3;
        private NamingConfig _existing;
        private NamingConfig _saved;

        public AnimeGroupNamingResourceFixture(bool useV3)
        {
            _useV3 = useV3;
        }

        [SetUp]
        public void Setup()
        {
            _existing = NamingConfig.Default;
            _existing.Id = 1;
            _existing.AnimeGroupFolderFormat = "Anime - {Group Title}";
            _existing.AnimeGroupSeasonFolderFormat = "Part {Group Season:00}";
            _existing.AnimeGroupArcFolderFormat = "Arc - {Group Arc Title}";
            _existing.AnimeGroupMovieFolderFormat = "Movie - {Series Title}";
            _existing.AnimeGroupMovieSagaFolderFormat = "Feature {Group Movie:00}";
            _saved = null;
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(() => _existing);
            Mocker.GetMock<INamingConfigService>().Setup(s => s.Save(It.IsAny<NamingConfig>())).Callback<NamingConfig>(saved => _saved = saved);

            // Filename parser behavior is covered by the real Core formatter fixtures.
            // Here the action/filter executes real folder validation and legacy mapping.
            Mocker.GetMock<IFilenameSampleService>().Setup(s => s.GetAnimeGroupMovieSample(It.IsAny<NamingConfig>())).Returns(new SampleResult());
        }

        [Test]
        public void should_preserve_user_configured_group_formats_for_a_legacy_put_omitting_new_fields()
        {
            var resource = Resource();
            resource.AnimeGroupFolderFormat = null;
            resource.AnimeGroupSeasonFolderFormat = null;
            resource.AnimeGroupArcFolderFormat = null;
            resource.AnimeGroupMovieFolderFormat = null;
            resource.AnimeGroupMovieSagaFolderFormat = null;

            Update(resource);

            _saved.AnimeGroupFolderFormat.Should().Be(_existing.AnimeGroupFolderFormat);
            _saved.AnimeGroupSeasonFolderFormat.Should().Be(_existing.AnimeGroupSeasonFolderFormat);
            _saved.AnimeGroupArcFolderFormat.Should().Be(_existing.AnimeGroupArcFolderFormat);
            _saved.AnimeGroupMovieFolderFormat.Should().Be(_existing.AnimeGroupMovieFolderFormat);
            _saved.AnimeGroupMovieSagaFolderFormat.Should().Be(_existing.AnimeGroupMovieSagaFolderFormat);
        }

        [Test]
        public void should_save_explicit_custom_group_formats_through_the_real_put_action()
        {
            var resource = Resource();
            resource.AnimeGroupSeasonFolderFormat = "Arc {Group Season:000}";
            resource.AnimeGroupArcFolderFormat = "Story - {Group Arc Title}";
            resource.AnimeGroupMovieSagaFolderFormat = "Movie {Group Movie:000}";

            Update(resource);

            _saved.AnimeGroupSeasonFolderFormat.Should().Be("Arc {Group Season:000}");
            _saved.AnimeGroupArcFolderFormat.Should().Be("Story - {Group Arc Title}");
            _saved.AnimeGroupMovieSagaFolderFormat.Should().Be("Movie {Group Movie:000}");
        }

        [TestCase("../{Group Title}")]
        [TestCase("C:\\{Group Title}")]
        [TestCase("{Group Title}\n")]
        [TestCase("{Unknown}")]
        [TestCase("")]
        public void should_reject_invalid_group_folder_expressions_before_saving(string value)
        {
            var resource = Resource();
            resource.AnimeGroupFolderFormat = value;
            Action update = () => Update(resource);

            update.Should().Throw<ValidationException>();
            Mocker.GetMock<INamingConfigService>().Verify(s => s.Save(It.IsAny<NamingConfig>()), Times.Never());
        }

        [Test]
        public void should_reject_group_numbers_as_a_replacement_for_mandatory_native_episode_tokens()
        {
            var resource = Resource();
            resource.AnimeEpisodeFormat = "{Series Title} - Film {Group Movie:00}";
            Action update = () => Update(resource);

            update.Should().Throw<ValidationException>();
            Mocker.GetMock<INamingConfigService>().Verify(s => s.Save(It.IsAny<NamingConfig>()), Times.Never());
        }

        [Test]
        public void should_reject_invalid_group_number_formats_even_with_valid_native_episode_tokens()
        {
            var resource = Resource();
            resource.AnimeEpisodeFormat = "{Series Title} - Film {Group Movie:abc} - S{season:00}E{episode:00}";
            Action update = () => Update(resource);

            update.Should().Throw<ValidationException>();
            Mocker.GetMock<INamingConfigService>().Verify(s => s.Save(It.IsAny<NamingConfig>()), Times.Never());
        }

        private V3Resource Resource()
        {
            return V3Mapper.ToResource(_existing);
        }

        [TestCase("../{Group Arc Title}")]
        [TestCase("{Group Arc Title}\\child")]
        [TestCase("{Group Season:00}")]
        [TestCase("{Group Arc Title:0}")]
        [TestCase("")]
        public void should_reject_invalid_arc_folder_expressions_before_saving(string value)
        {
            var resource = Resource();
            resource.AnimeGroupArcFolderFormat = value;
            Action update = () => Update(resource);

            update.Should().Throw<ValidationException>();
            Mocker.GetMock<INamingConfigService>().Verify(s => s.Save(It.IsAny<NamingConfig>()), Times.Never());
        }

        private void Update(V3Resource resource)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "PUT";
            if (_useV3)
            {
                var controller = new V3Controller(
                    Mocker.GetMock<INamingConfigService>().Object,
                    Mocker.GetMock<IFilenameSampleService>().Object,
                    Mocker.GetMock<IFilenameValidationService>().Object,
                    Mocker.GetMock<IBuildFileNames>().Object);
                controller.ControllerContext.HttpContext = context;
                controller.Url = new Mock<IUrlHelper>().Object;
                controller.CheckedUpdate(resource);
            }
            else
            {
                var model = V3Mapper.ToModel(resource, _existing);
                var v5 = V5Mapper.ToResource(model);
                v5.AnimeGroupFolderFormat = resource.AnimeGroupFolderFormat;
                v5.AnimeGroupSeasonFolderFormat = resource.AnimeGroupSeasonFolderFormat;
                v5.AnimeGroupArcFolderFormat = resource.AnimeGroupArcFolderFormat;
                v5.AnimeGroupMovieFolderFormat = resource.AnimeGroupMovieFolderFormat;
                v5.AnimeGroupMovieSagaFolderFormat = resource.AnimeGroupMovieSagaFolderFormat;
                var controller = new V5Controller(
                    Mocker.GetMock<INamingConfigService>().Object,
                    Mocker.GetMock<IFilenameSampleService>().Object,
                    Mocker.GetMock<IFilenameValidationService>().Object);
                controller.ControllerContext.HttpContext = context;
                controller.Url = new Mock<IUrlHelper>().Object;
                controller.CheckedUpdate(v5);
            }
        }

        private sealed class V3Controller : Sonarr.Api.V3.Config.NamingConfigController
        {
            public V3Controller(INamingConfigService config, IFilenameSampleService samples, IFilenameValidationService validation, IBuildFileNames names)
                : base(config, samples, validation, names)
            {
            }

            public void CheckedUpdate(V3Resource resource)
            {
                ValidateResource(resource, true);
                UpdateNamingConfig(resource);
            }
        }

        private sealed class V5Controller : Sonarr.Api.V5.Settings.NamingSettingsController
        {
            public V5Controller(INamingConfigService config, IFilenameSampleService samples, IFilenameValidationService validation)
                : base(config, samples, validation)
            {
            }

            public void CheckedUpdate(V5Resource resource)
            {
                ValidateResource(resource, true);
                UpdateNamingConfig(resource);
            }
        }
    }
}
