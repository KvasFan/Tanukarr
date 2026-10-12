using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class SeriesSearchServiceFixture : CoreTest<SeriesSearchService>
    {
        private Series _series;

        [SetUp]
        public void Setup()
        {
            _series = new Series
                      {
                          Id = 1,
                          Title = "Title",
                          Seasons = new List<Season>(),
                          QualityProfile = new LazyLoaded<QualityProfile>(Builder<QualityProfile>.CreateNew().With(q => q.UpgradeAllowed = true).Build())
                      };

            Mocker.GetMock<IQueueService>().Setup(s => s.GetQueue()).Returns(new List<Queue.Queue>());
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(It.IsAny<int>())).Returns(Enumerable.Range(1, 3).Select(n => new Episode { Id = n, SeasonNumber = n, Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-1) }).ToList());

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(It.IsAny<int>()))
                  .Returns(_series);

            Mocker.GetMock<ISearchForReleases>()
                  .Setup(s => s.SeasonSearch(_series.Id, It.IsAny<int>(), It.IsAny<bool>(), true, true, false))
                  .ReturnsAsync(new List<DownloadDecision>());

            Mocker.GetMock<ISearchForReleases>()
                  .Setup(s => s.CompleteSeriesSearch(_series.Id, It.IsAny<bool>(), false))
                  .ReturnsAsync(new List<DownloadDecision>());

            Mocker.GetMock<IProcessDownloadDecisions>()
                  .Setup(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                  .ReturnsAsync(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>()));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_try_complete_series_before_seasons_and_only_skip_successful_grabs(bool grabbed)
        {
            _series.Seasons = Enumerable.Range(1, 3).Select(n => new Season { SeasonNumber = n, Monitored = true }).ToList();
            var episodes = Enumerable.Range(1, 3).Select(n => new Episode
            {
                Id = n, SeriesId = _series.Id, SeasonNumber = n, Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-1)
            }).ToList();
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(_series.Id)).Returns(episodes);
            var pack = new DownloadDecision(new RemoteEpisode { Series = _series, Episodes = episodes });
            var order = new List<int>();
            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.CompleteSeriesSearch(_series.Id, true, false))
                .Callback(() => order.Add(0))
                .ReturnsAsync(new List<DownloadDecision> { pack });
            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.SeasonSearch(_series.Id, It.IsAny<int>(), false, true, true, false))
                .Callback<int, int, bool, bool, bool, bool>((id, season, missing, monitored, user, interactive) => order.Add(season))
                .ReturnsAsync(new List<DownloadDecision>());
            Mocker.GetMock<IProcessDownloadDecisions>()
                .Setup(s => s.ProcessDecisions(It.Is<List<DownloadDecision>>(d => d.Contains(pack))))
                .ReturnsAsync(new ProcessedDecisions(grabbed ? new List<DownloadDecision> { pack } : new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>()));

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            order.Should().Equal(grabbed ? new[] { 0 } : new[] { 0, 1, 2, 3 });
        }

        [TestCase(false, false, false, 1)]
        [TestCase(true, false, false, 0)]
        [TestCase(false, true, false, 0)]
        [TestCase(false, false, true, 0)]
        public void complete_search_requires_two_incomplete_aired_monitored_seasons(bool complete, bool future, bool unmonitored, int searches)
        {
            _series.Seasons = Enumerable.Range(1, 2).Select(n => new Season { SeasonNumber = n, Monitored = true }).ToList();
            var episodes = new List<Episode>
            {
                new Episode { Id = 1, SeasonNumber = 1, Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-1) },
                new Episode { Id = 2, SeasonNumber = 2, Monitored = !unmonitored, EpisodeFileId = complete ? 42 : 0, AirDateUtc = DateTime.UtcNow.AddDays(future ? 1 : -1) }
            };
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(_series.Id)).Returns(episodes);

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.CompleteSeriesSearch(_series.Id, true, false), Times.Exactly(searches));
        }

        [Test]
        public void should_fall_back_when_complete_search_is_unavailable()
        {
            _series.Seasons = Enumerable.Range(1, 2).Select(n => new Season { SeasonNumber = n, Monitored = true }).ToList();
            Mocker.GetMock<ISearchForReleases>().Setup(s => s.CompleteSeriesSearch(_series.Id, true, false))
                .ThrowsAsync(new SearchFailedException("No torrent indexers"));

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.SeasonSearch(_series.Id, It.IsAny<int>(), false, true, true, false), Times.Exactly(2));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_repeat_complete_search_for_active_multiseason_coverage()
        {
            _series.Seasons = Enumerable.Range(1, 3).Select(n => new Season { SeasonNumber = n, Monitored = true }).ToList();
            var episodes = Mocker.GetMock<IEpisodeService>().Object.GetEpisodeBySeries(_series.Id);
            Mocker.GetMock<IQueueService>().Setup(s => s.GetQueue()).Returns(new List<Queue.Queue>
            {
                new Queue.Queue
                {
                    TrackedDownloadState = TrackedDownloadState.Downloading,
                    RemoteEpisode = new RemoteEpisode
                    {
                        Series = _series, Episodes = episodes.Take(2).ToList(),
                        ParsedEpisodeInfo = new ParsedEpisodeInfo { FullSeason = true }
                    }
                }
            });

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.CompleteSeriesSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<ISearchForReleases>().Verify(s => s.SeasonSearch(_series.Id, 3, false, true, true, false), Times.Once());
        }

        [TestCase(SeriesTypes.Standard, false, false, false, 0)]
        [TestCase(SeriesTypes.Anime, false, false, false, 0)]
        [TestCase(SeriesTypes.Standard, true, true, false, 1)]
        [TestCase(SeriesTypes.Anime, true, true, false, 1)]
        [TestCase(SeriesTypes.Standard, true, false, false, 0)]
        [TestCase(SeriesTypes.Standard, true, true, true, 0)]
        public void should_search_only_seasons_with_missing_aired_monitored_episodes(SeriesTypes type, bool missing, bool monitored, bool future, int searches)
        {
            _series.SeriesType = type;
            _series.Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = true } };
            var episodes = new List<Episode>
            {
                new Episode { Id = 1, SeasonNumber = 1, Monitored = true, EpisodeFileId = 1, AirDateUtc = DateTime.UtcNow.AddDays(-1) },
                new Episode { Id = 2, SeasonNumber = 1, Monitored = monitored, EpisodeFileId = missing ? 0 : 2, AirDateUtc = DateTime.UtcNow.AddDays(future ? 1 : -1) }
            };
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(_series.Id)).Returns(episodes);
            Mocker.GetMock<ISearchForReleases>().Setup(s => s.AnimeSeasonPackSearch(_series, It.IsAny<List<Episode>>(), true, true, false)).ReturnsAsync(new List<DownloadDecision>());
            Mocker.GetMock<ISearchForReleases>().Setup(s => s.EpisodeSearch(It.IsAny<Episode>(), true, false)).ReturnsAsync(new List<DownloadDecision>());

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            if (type == SeriesTypes.Anime)
            {
                Mocker.GetMock<ISearchForReleases>().Verify(s => s.AnimeSeasonPackSearch(_series, It.Is<List<Episode>>(e => e.Count == 2), true, true, false), Times.Exactly(searches));
            }
            else
            {
                // Keep missingOnly false so a pack may include already-owned episodes.
                Mocker.GetMock<ISearchForReleases>().Verify(s => s.SeasonSearch(_series.Id, 1, false, true, true, false), Times.Exactly(searches));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void anime_should_search_only_episodes_not_successfully_grabbed_in_pack(int grabbedCount)
        {
            _series.SeriesType = SeriesTypes.Anime;
            _series.Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = true } };
            var episodes = Enumerable.Range(1, 2).Select(n => new Episode
            {
                Id = n, SeriesId = _series.Id, SeasonNumber = 1, Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-1)
            }).ToList();
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(_series.Id)).Returns(episodes);
            var pack = new DownloadDecision(new RemoteEpisode
            {
                Series = _series,
                Episodes = grabbedCount == 0 ? episodes : episodes.Take(grabbedCount).ToList(),
                ParsedEpisodeInfo = new ParsedEpisodeInfo { FullSeason = true }
            });
            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.AnimeSeasonPackSearch(_series, It.IsAny<List<Episode>>(), true, true, false))
                .ReturnsAsync(new List<DownloadDecision> { pack });
            Mocker.GetMock<IProcessDownloadDecisions>()
                .Setup(s => s.ProcessDecisions(It.Is<List<DownloadDecision>>(d => d.Contains(pack))))
                .ReturnsAsync(new ProcessedDecisions(
                    grabbedCount > 0 ? new List<DownloadDecision> { pack } : new List<DownloadDecision>(),
                    new List<DownloadDecision>(),
                    new List<DownloadDecision>()));
            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.EpisodeSearch(It.IsAny<Episode>(), true, false))
                .ReturnsAsync(new List<DownloadDecision>());

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.EpisodeSearch(It.IsAny<Episode>(), true, false), Times.Exactly(2 - grabbedCount));
            foreach (var episode in episodes.Take(grabbedCount))
            {
                Mocker.GetMock<ISearchForReleases>().Verify(s => s.EpisodeSearch(episode, true, false), Times.Never());
            }

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.SeasonSearch(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_only_include_monitored_seasons()
        {
            _series.Seasons = new List<Season>
                              {
                                  new Season { SeasonNumber = 0, Monitored = false },
                                  new Season { SeasonNumber = 1, Monitored = true }
                              };

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.SeasonSearch(_series.Id, It.IsAny<int>(), false, true, true, false), Times.Exactly(_series.Seasons.Count(s => s.Monitored)));
        }

        [Test]
        public void should_only_search_missing_if_profile_does_not_allow_upgrades()
        {
            _series.Seasons = new List<Season>
            {
                new Season { SeasonNumber = 0, Monitored = false },
                new Season { SeasonNumber = 1, Monitored = true }
            };

            _series.QualityProfile.Value.UpgradeAllowed = false;

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.SeasonSearch(_series.Id, It.IsAny<int>(), true, true, true, false), Times.Exactly(_series.Seasons.Count(s => s.Monitored)));
        }

        [Test]
        public void should_start_with_lower_seasons_first()
        {
            var seasonOrder = new List<int>();

            _series.Seasons = new List<Season>
                              {
                                  new Season { SeasonNumber = 3, Monitored = true },
                                  new Season { SeasonNumber = 1, Monitored = true },
                                  new Season { SeasonNumber = 2, Monitored = true }
                              };

            Mocker.GetMock<ISearchForReleases>()
                  .Setup(s => s.SeasonSearch(_series.Id, It.IsAny<int>(), false, true, true, false))
                  .ReturnsAsync(new List<DownloadDecision>())
                  .Callback<int, int, bool, bool, bool, bool>((seriesId, seasonNumber, missingOnly, monitoredOnly, userInvokedSearch, interactiveSearch) => seasonOrder.Add(seasonNumber));

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            seasonOrder.First().Should().Be(_series.Seasons.OrderBy(s => s.SeasonNumber).First().SeasonNumber);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_carry_successful_pack_coverage_across_seasons(bool grabbed)
        {
            _series.Seasons = Enumerable.Range(1, 3).Select(n => new Season { SeasonNumber = n, Monitored = true }).ToList();
            var episodes = Enumerable.Range(1, 3).Select(n => new Episode { Id = n, SeriesId = _series.Id, SeasonNumber = n, Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-1) }).ToList();
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(_series.Id)).Returns(episodes);
            var pack = new DownloadDecision(new RemoteEpisode { Series = _series, Episodes = episodes.Take(2).ToList() });
            var searches = new List<int>();
            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.SeasonSearch(_series.Id, It.IsAny<int>(), false, true, true, false))
                .Callback<int, int, bool, bool, bool, bool>((id, season, missing, monitored, user, interactive) => searches.Add(season))
                .ReturnsAsync(new List<DownloadDecision>());
            Mocker.GetMock<IProcessDownloadDecisions>()
                .SetupSequence(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                .ReturnsAsync(new ProcessedDecisions(grabbed ? new List<DownloadDecision> { pack } : new List<DownloadDecision>(), new List<DownloadDecision> { pack }, new List<DownloadDecision>()))
                .ReturnsAsync(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>()))
                .ReturnsAsync(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>()));

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            searches.Should().Equal(grabbed ? new[] { 1, 3 } : new[] { 1, 2, 3 });
        }

        [TestCase(false, TrackedDownloadState.Downloading, 0)]
        [TestCase(true, TrackedDownloadState.Downloading, 1)]
        [TestCase(false, TrackedDownloadState.FailedPending, 1)]
        [TestCase(false, TrackedDownloadState.Imported, 0)]
        public void should_skip_only_fully_covered_seasons(bool partial, TrackedDownloadState state, int expectedSearches)
        {
            _series.Seasons = new List<Season> { new Season { SeasonNumber = 2, Monitored = true } };
            var episodes = new List<Episode>
            {
                new Episode { Id = 1, SeasonNumber = 1, Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-1) },
                new Episode { Id = 2, SeasonNumber = 2, Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-1), EpisodeFileId = state == TrackedDownloadState.Imported ? 42 : 0 },
                new Episode { Id = 3, SeasonNumber = 2, Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-1), EpisodeFileId = state == TrackedDownloadState.Imported ? 43 : 0 }
            };
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(_series.Id)).Returns(episodes);
            Mocker.GetMock<IQueueService>().Setup(s => s.GetQueue()).Returns(new List<Queue.Queue>
            {
                new Queue.Queue
                {
                    TrackedDownloadState = state,
                    RemoteEpisode = new RemoteEpisode
                    {
                        Series = _series,
                        Episodes = partial ? episodes.Take(2).ToList() : episodes,
                        ParsedEpisodeInfo = new ParsedEpisodeInfo { FullSeason = true }
                    }
                }
            });

            Subject.Execute(new SeriesSearchCommand { SeriesId = _series.Id, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.SeasonSearch(_series.Id, 2, false, true, true, false), Times.Exactly(expectedSearches));
        }
    }
}
