using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Tv;
using Sonarr.Api.V5.Queue;

namespace NzbDrone.Api.Test.v5.Queue
{
    [TestFixture]
    public class QueueResourceFixture
    {
        [Test]
        public void should_leave_import_progress_absent_when_queue_has_no_snapshot()
        {
            var resource = new NzbDrone.Core.Queue.Queue { Episodes = new List<Episode>() }.ToResource(false, false);

            resource.ImportProgress.Should().BeNull();
        }

        [Test]
        public void should_preserve_legacy_season_number_when_episode_collection_is_empty()
        {
            var model = new NzbDrone.Core.Queue.Queue
            {
                SeasonNumber = 3,
                Episodes = new List<Episode>()
            };

            model.ToResource(false, false).SeasonNumbers.Should().Equal(3);
        }

        [Test]
        public void should_preserve_legacy_season_number_when_episode_collection_is_null()
        {
            var model = new NzbDrone.Core.Queue.Queue { SeasonNumber = 4, Episodes = null };

            model.ToResource(false, false).SeasonNumbers.Should().Equal(4);
        }

        [Test]
        public void should_map_import_progress_and_child_details()
        {
            var started = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
            var model = new NzbDrone.Core.Queue.Queue
            {
                Episodes = new List<Episode>(),
                ImportProgress = new ImportProgress
                {
                    TotalEpisodes = 4,
                    ImportedEpisodes = 2,
                    ImportingEpisodes = 1,
                    WaitingEpisodes = 1,
                    Items = new List<ImportProgressItem>
                    {
                        new ImportProgressItem
                        {
                            Id = "import-1",
                            EpisodeIds = new List<int> { 301, 302 },
                            SeasonNumber = 3,
                            EpisodeNumbers = new List<int> { 1, 2 },
                            SourcePath = @"C:\Import\Example Show S03E01-E02.mkv",
                            Size = 1234,
                            State = ImportProgressState.Importing,
                            Started = started,
                            Completed = started.AddMinutes(1),
                            Errors = new List<string> { "Example rejection" }
                        }
                    }
                }
            };

            var resource = model.ToResource(false, false);

            resource.ImportProgress.TotalEpisodes.Should().Be(4);
            resource.ImportProgress.Items.Should().ContainSingle();
            resource.ImportProgress.Items[0].EpisodeIds.Should().Equal(301, 302);
            resource.ImportProgress.Items[0].State.Should().Be(ImportProgressState.Importing);
            resource.ImportProgress.Items[0].Started.Should().Be(started);
            resource.ImportProgress.Items[0].Completed.Should().Be(started.AddMinutes(1));
            resource.ImportProgress.Items[0].Errors.Should().Equal("Example rejection");
        }

        [Test]
        public void should_map_distinct_season_numbers_from_all_mapped_episodes()
        {
            var model = new NzbDrone.Core.Queue.Queue
            {
                SeasonNumber = 1,
                Episodes = new List<Episode>
                {
                    new Episode { SeasonNumber = 1 },
                    new Episode { SeasonNumber = 2 },
                    new Episode { SeasonNumber = 3 },
                    new Episode { SeasonNumber = 4 },
                    new Episode { SeasonNumber = 4 }
                }
            };

            model.ToResource(false, false).SeasonNumbers.Should().Equal(1, 2, 3, 4);
        }
    }
}
