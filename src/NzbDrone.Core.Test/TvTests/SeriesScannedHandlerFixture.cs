using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SeriesScannedHandlerFixture : CoreTest<SeriesScannedHandler>
    {
        private Series GivenAddedSeries(MonitorTypes monitor)
        {
            return new Series
            {
                Id = 1,
                Title = "Nathan for You",
                Seasons = new List<Season>
                {
                    new Season { SeasonNumber = 0, Monitored = true },
                    new Season { SeasonNumber = 1, Monitored = true },
                    new Season { SeasonNumber = 2, Monitored = true }
                },
                AddOptions = new AddSeriesOptions { Monitor = monitor, SearchForMissingEpisodes = true }
            };
        }

        [Test]
        public void should_not_monitor_specials_when_added_without_a_monitor_choice()
        {
            var series = GivenAddedSeries(MonitorTypes.Unknown);
            List<Season> applied = null;
            Mocker.GetMock<IEpisodeMonitoredService>()
                .Setup(s => s.SetEpisodeMonitoredStatus(series, It.IsAny<MonitoringOptions>()))
                .Callback<Series, MonitoringOptions>((s, o) => applied = s.Seasons.Select(x => new Season { SeasonNumber = x.SeasonNumber, Monitored = x.Monitored }).ToList());

            Subject.Handle(new SeriesScannedEvent(series, new List<string>()));

            applied.Single(s => s.SeasonNumber == 0).Monitored.Should().BeFalse();
            applied.Where(s => s.SeasonNumber > 0).Should().OnlyContain(s => s.Monitored);
        }

        [TestCase(MonitorTypes.All)]
        [TestCase(MonitorTypes.Future)]
        public void should_keep_specials_as_chosen_with_an_explicit_monitor_choice(MonitorTypes monitor)
        {
            var series = GivenAddedSeries(monitor);

            Subject.Handle(new SeriesScannedEvent(series, new List<string>()));

            series.Seasons.Single(s => s.SeasonNumber == 0).Monitored.Should().BeTrue();
        }

        [Test]
        public void should_queue_search_before_publishing_series_add_completed()
        {
            var series = Builder<Series>.CreateNew()
                                        .With(s => s.AddOptions = new AddSeriesOptions { SearchForMissingEpisodes = true })
                                        .Build();

            var calls = new List<string>();

            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(s => s.Push(It.IsAny<MissingEpisodeSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Callback(() => calls.Add("search"));

            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<SeriesAddCompletedEvent>()))
                  .Callback(() => calls.Add("addCompleted"));

            Subject.Handle(new SeriesScannedEvent(series, new List<string>()));

            calls.Should().Equal("search", "addCompleted");
        }
    }
}
