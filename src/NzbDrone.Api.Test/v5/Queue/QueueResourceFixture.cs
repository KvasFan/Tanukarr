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
        public void should_map_actual_episode_seasons_and_existing_files_without_episode_subresources()
        {
            var model = new NzbDrone.Core.Queue.Queue
            {
                SeasonNumber = 1,
                Episodes = new List<Episode>
                {
                    new Episode { Id = 21, SeasonNumber = 2 },
                    new Episode { Id = 11, SeasonNumber = 1, EpisodeFileId = 50 },
                    new Episode { Id = 51, SeasonNumber = 5, EpisodeFileId = 51 }
                }
            };

            var resource = model.ToResource(false, false);

            resource.SeasonNumbers.Should().Equal(1, 2, 5);
            resource.EpisodeIds.Should().BeEquivalentTo(new[] { 11, 21, 51 });
            resource.EpisodeIdsBySeason[1].Should().Equal(11);
            resource.EpisodeIdsBySeason[2].Should().Equal(21);
            resource.EpisodeIdsBySeason[5].Should().Equal(51);
            resource.EpisodeIdsWithFiles.Should().BeEquivalentTo(new[] { 11, 51 });
            resource.EpisodesWithFilesCount.Should().Be(2);
            resource.Episodes.Should().BeNull();
        }

        [Test]
        public void should_count_repeated_episode_associations_once()
        {
            var episode = new Episode { Id = 7, SeasonNumber = 0, EpisodeFileId = 70 };
            var model = new NzbDrone.Core.Queue.Queue { Episodes = new List<Episode> { episode, episode } };

            var resource = model.ToResource(false, false);

            resource.EpisodeIds.Should().Equal(7);
            resource.SeasonNumbers.Should().Equal(0);
            resource.EpisodeIdsBySeason[0].Should().Equal(7);
            resource.EpisodeIdsWithFiles.Should().Equal(7);
            resource.EpisodesWithFilesCount.Should().Be(1);
        }

        [Test]
        public void unmatched_queue_item_should_retain_known_season_without_inventing_episodes()
        {
            var resource = new NzbDrone.Core.Queue.Queue { SeasonNumber = 4 }.ToResource(false, false);

            resource.SeasonNumbers.Should().Equal(4);
            resource.EpisodeIds.Should().BeEmpty();
            resource.EpisodeIdsBySeason.Should().BeEmpty();
            resource.EpisodeIdsWithFiles.Should().BeEmpty();
        }

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
