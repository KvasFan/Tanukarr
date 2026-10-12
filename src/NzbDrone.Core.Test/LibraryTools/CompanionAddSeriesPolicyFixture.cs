using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;
using NzbDrone.Core.LibraryTools;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.LibraryTools
{
    [TestFixture]
    public class CompanionAddSeriesPolicyFixture
    {
        private Series _series;
        private List<RootFolder> _roots;
        private string _rootPath;

        [SetUp]
        public void Setup()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), "companion-library");
            _roots = new List<RootFolder> { new RootFolder { Path = _rootPath } };
            _series = new Series { RootFolderPath = _rootPath };
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void permits_generated_series_folders_without_search_options(string path)
        {
            _series.Path = path;

            Action validate = () => CompanionAddSeriesPolicy.Validate(_series, _roots);

            validate.Should().NotThrow();
        }

        [Test]
        public void permits_monitoring_options_without_triggering_searches()
        {
            _series.AddOptions = new AddSeriesOptions { Monitor = MonitorTypes.All };

            CompanionAddSeriesPolicy.Validate(_series, _roots);

            _series.AddOptions.Monitor.Should().Be(MonitorTypes.All);
            _series.AddOptions.SearchForMissingEpisodes.Should().BeFalse();
            _series.AddOptions.SearchForCutoffUnmetEpisodes.Should().BeFalse();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void rejects_custom_series_paths_inside_and_outside_registered_roots(bool insideRoot)
        {
            _series.Path = insideRoot ? Path.Combine(_rootPath, "Custom series") : Path.Combine(Path.GetTempPath(), "outside-library");

            Assert.Throws<ValidationException>(() => CompanionAddSeriesPolicy.Validate(_series, _roots))
                .Errors.Select(error => error.PropertyName).Should().Contain(nameof(Series.Path));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("invalid\0path")]
        public void rejects_missing_or_invalid_root_paths(string path)
        {
            _series.RootFolderPath = path;

            Assert.Throws<ValidationException>(() => CompanionAddSeriesPolicy.Validate(_series, _roots))
                .Errors.Select(error => error.PropertyName).Should().Contain(nameof(Series.RootFolderPath));
        }

        [TestCase("nested")]
        [TestCase("../outside-library")]
        [TestCase("nested/..")]
        public void rejects_children_and_paths_escaping_registered_roots(string suffix)
        {
            _series.RootFolderPath = Path.Combine(_rootPath, suffix);

            Assert.Throws<ValidationException>(() => CompanionAddSeriesPolicy.Validate(_series, _roots))
                .Errors.Select(error => error.PropertyName).Should().Contain(nameof(Series.RootFolderPath));
        }

        [Test]
        public void rejects_sibling_paths_sharing_a_registered_root_prefix()
        {
            _series.RootFolderPath = _rootPath + "-outside";

            Assert.Throws<ValidationException>(() => CompanionAddSeriesPolicy.Validate(_series, _roots));
        }

        [Test]
        public void rejects_an_add_when_no_root_folder_is_registered()
        {
            Assert.Throws<ValidationException>(() => CompanionAddSeriesPolicy.Validate(_series, new List<RootFolder>()));
        }

        [Test]
        public void canonicalizes_equivalent_paths_to_the_registered_root()
        {
            _series.RootFolderPath = _rootPath + Path.DirectorySeparatorChar;

            CompanionAddSeriesPolicy.Validate(_series, _roots);

            _series.RootFolderPath.Should().Be(_rootPath);
        }

        [TestCase(true, false, "AddOptions.SearchForMissingEpisodes")]
        [TestCase(false, true, "AddOptions.SearchForCutoffUnmetEpisodes")]
        [TestCase(true, true, "AddOptions.SearchForMissingEpisodes")]
        public void rejects_search_options(bool missing, bool cutoff, string property)
        {
            _series.AddOptions = new AddSeriesOptions
            {
                SearchForMissingEpisodes = missing,
                SearchForCutoffUnmetEpisodes = cutoff
            };

            Assert.Throws<ValidationException>(() => CompanionAddSeriesPolicy.Validate(_series, _roots))
                .Errors.Select(error => error.PropertyName).Should().Contain(property);
        }

        [Test]
        public void rejected_requests_do_not_mutate_the_model()
        {
            var requestedRoot = _rootPath + Path.DirectorySeparatorChar;
            _series.RootFolderPath = requestedRoot;
            _series.AddOptions = new AddSeriesOptions { SearchForMissingEpisodes = true };

            Assert.Throws<ValidationException>(() => CompanionAddSeriesPolicy.Validate(_series, _roots));

            _series.RootFolderPath.Should().Be(requestedRoot);
            _series.AddOptions.SearchForMissingEpisodes.Should().BeTrue();
        }
    }
}
