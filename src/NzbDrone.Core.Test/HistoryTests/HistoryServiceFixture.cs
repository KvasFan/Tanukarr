using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.HistoryTests
{
    public class HistoryServiceFixture : CoreTest<HistoryService>
    {
        private QualityProfile _profile;
        private QualityProfile _profileCustom;

        [SetUp]
        public void Setup()
        {
            _profile = new QualityProfile
                {
                    Cutoff = Quality.WEBDL720p.Id,
                    Items = QualityFixture.GetDefaultQualities(),
                };

            _profileCustom = new QualityProfile
                {
                    Cutoff = Quality.WEBDL720p.Id,
                    Items = QualityFixture.GetDefaultQualities(Quality.DVD),
                };
        }

        [Test]
        public void should_use_file_name_for_source_title_if_scene_name_is_null()
        {
            var series = Builder<Series>.CreateNew().Build();
            var episodes = Builder<Episode>.CreateListOfSize(1).Build().ToList();
            var episodeFile = Builder<EpisodeFile>.CreateNew()
                                                  .With(f => f.SceneName = null)
                                                  .Build();

            var localEpisode = new LocalEpisode
                               {
                                   Series = series,
                                   Episodes = episodes,
                                   Path = @"C:\Test\Unsorted\Series.s01e01.mkv"
                               };

            var downloadClientItem = new DownloadClientItem
                                     {
                                         DownloadClientInfo = new DownloadClientItemClientInfo
                                         {
                                             Protocol = DownloadProtocol.Usenet,
                                             Id = 1,
                                             Name = "sab"
                                         },
                                         DownloadId = "abcd"
                                     };

            Subject.Handle(new EpisodeImportedEvent(localEpisode, episodeFile, new List<DeletedEpisodeFile>(), true, downloadClientItem));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EpisodeHistory>(h => h.SourceTitle == Path.GetFileNameWithoutExtension(localEpisode.Path))));
        }

        [Test]
        public void should_record_grab_history_for_every_episode_in_a_multi_season_release()
        {
            var episodes = new List<Episode>
            {
                new Episode { Id = 101, SeriesId = 1, SeasonNumber = 1, EpisodeNumber = 1 },
                new Episode { Id = 201, SeriesId = 1, SeasonNumber = 2, EpisodeNumber = 1 },
                new Episode { Id = 301, SeriesId = 1, SeasonNumber = 3, EpisodeNumber = 1 },
                new Episode { Id = 401, SeriesId = 1, SeasonNumber = 4, EpisodeNumber = 1 }
            };
            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series { Id = 1 },
                Episodes = episodes,
                ParsedEpisodeInfo = Parser.Parser.ParseTitle("Example.Show.S01-S04.1080p.WEB-DL"),
                Release = new ReleaseInfo
                {
                    Title = "Example.Show.S01-S04.1080p.WEB-DL",
                    Indexer = "Test Indexer",
                    PublishDate = System.DateTime.UtcNow
                }
            };
            var inserted = new List<EpisodeHistory>();

            Mocker.GetMock<IHistoryRepository>()
                .Setup(r => r.Insert(It.IsAny<EpisodeHistory>()))
                .Returns<EpisodeHistory>(history =>
                {
                    inserted.Add(history);
                    return history;
                });

            Subject.Handle(new EpisodeGrabbedEvent(remoteEpisode) { DownloadId = "multi-season-download" });

            var grabbedRelease = new GrabbedReleaseInfo(inserted);

            remoteEpisode.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(1, 2, 3, 4);
            inserted.Should().HaveCount(4);
            inserted.Select(history => history.EpisodeId).Should().Equal(101, 201, 301, 401);
            grabbedRelease.EpisodeIds.Should().Equal(101, 201, 301, 401);
        }
    }
}
