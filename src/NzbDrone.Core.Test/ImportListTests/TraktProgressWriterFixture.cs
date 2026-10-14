using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Progress;
using NzbDrone.Core.ImportLists.Trakt.User;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class TraktProgressWriterFixture : CoreTest<TraktProgressWriter>
    {
        private const string Identity = "{\"user\":{\"username\":\"Owner Name\",\"ids\":{\"slug\":\"owner\"}}}";
        private const string Show = "[{\"type\":\"show\",\"show\":{\"ids\":{\"tvdb\":12,\"trakt\":20}}}]";
        private const string Episode = "[{\"type\":\"episode\",\"episode\":{\"ids\":{\"tvdb\":101,\"trakt\":201}},\"show\":{\"ids\":{\"trakt\":20}}}]";
        private const string Saved = "{\"added\":{\"episodes\":1},\"not_found\":{\"movies\":[],\"shows\":[],\"episodes\":[]}}";
        private readonly List<HttpRequest> _requests = new();
        private TraktUserSettings _settings;
        private ImportListDefinition _definition;
        private ImportListProgressTarget _target;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _settings = new TraktUserSettings { Username = "owner", AuthUser = "owner", AccessToken = "PRIVATE-FIXTURE", Expires = DateTime.UtcNow.AddDays(1), SyncWatchedProgress = true };
            _definition = new ImportListDefinition { Id = 8, Settings = _settings };
            Subject.TryGetTarget(_definition,
                new Series { MetadataSource = MetadataSourceType.Tvdb, TvdbId = 12, SeasonType = SeasonType.Official },
                new ImportListItemInfo { TvdbId = 12 },
                out _target).Should().BeTrue();
            _target.NativeEpisodes = new List<Episode>
            {
                new Episode
                {
                    Id = 1,
                    TvdbId = 101,
                    SeasonNumber = 4,
                    EpisodeNumber = 3,
                    WatchedAtUtc = new DateTime(2026, 10, 14, 0, 1, 2, DateTimeKind.Utc)
                }
            };
        }

        private void Respond(params string[] responses)
        {
            var bodies = new Queue<string>(responses);
            Mocker.GetMock<IHttpClient>().Setup(service => service.Execute(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
            {
                _requests.Add(request);
                return new HttpResponse(request, new HttpHeader(), bodies.Dequeue());
            });
        }

        private static JObject Payload(HttpRequest request)
        {
            using var reader = new JsonTextReader(new StringReader(System.Text.Encoding.UTF8.GetString(request.ContentData)))
            {
                DateParseHandling = DateParseHandling.None
            };
            return JObject.Load(reader);
        }

        [Test]
        public void should_write_only_the_exact_provider_episode_id_after_identity_and_history_checks()
        {
            Respond(Identity, Show, Episode, "[]", Saved);
            Subject.Advance(_definition, _target, 1).Should().Be(1);
            var update = _requests.Single(request => request.Method == HttpMethod.Post);
            var payload = Payload(update);
            payload.Properties().Select(property => property.Name).Should().Equal("episodes");
            payload["episodes"][0]["ids"]["trakt"].Value<int>().Should().Be(201);
            payload["episodes"][0]["watched_at"].Value<string>().Should().Be("2026-10-14T00:01:02.0000000Z");
            update.Url.Path.Should().Be("/sync/history");
            _requests[3].Url.Path.Should().Be("/sync/history/episodes/201");
            _requests[3].Url.Query.Should().Contain("limit=1");
            _requests.Should().OnlyContain(request => !request.AllowAutoRedirect && !request.LogResponseContent && request.ContentSummary == null);
        }

        [Test]
        public void should_not_duplicate_a_previously_recorded_watch_when_the_last_reply_was_lost()
        {
            Respond(Identity, Show, Episode, "[{\"type\":\"episode\",\"episode\":{\"ids\":{\"trakt\":201}}}]");
            Subject.Advance(_definition, _target, 1).Should().Be(1);
            _requests.Should().OnlyContain(request => request.Method == HttpMethod.Get);
        }

        [Test]
        public void should_normalize_a_local_watch_timestamp_to_the_same_utc_instant()
        {
            _target.NativeEpisodes.Single().WatchedAtUtc = new DateTime(2026, 10, 14, 0, 1, 2, DateTimeKind.Utc).ToLocalTime();
            Respond(Identity, Show, Episode, "[]", Saved);
            Subject.Advance(_definition, _target, 1).Should().Be(1);
            var payload = Payload(_requests.Last());
            payload["episodes"][0]["watched_at"].Value<string>().Should().Be("2026-10-14T00:01:02.0000000Z");
        }

        [Test]
        public void should_use_current_utc_mark_time_only_when_the_source_timestamp_is_missing()
        {
            _target.NativeEpisodes.Single().WatchedAtUtc = null;
            var before = DateTime.UtcNow;
            Respond(Identity, Show, Episode, "[]", Saved);
            Subject.Advance(_definition, _target, 1).Should().Be(1);
            var payload = Payload(_requests.Last());
            var timestamp = payload["episodes"][0]["watched_at"].Value<string>();
            timestamp.Should().EndWith("Z");
            DateTime.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        }

        [Test]
        public void should_refuse_an_episode_lookup_from_another_show()
        {
            Respond(Identity, Show, Episode.Replace("\"trakt\":20", "\"trakt\":99", StringComparison.Ordinal));
            Action action = () => Subject.Advance(_definition, _target, 1);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().HaveCount(3);
        }

        [Test]
        public void should_refuse_foreign_public_lists_before_lookup()
        {
            _settings.Username = "another-owner";
            Respond(Identity);
            Action action = () => Subject.Advance(_definition, _target, 1);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().ContainSingle();
        }

        [Test]
        public void should_refuse_unsafe_numbering_or_non_tvdb_metadata()
        {
            Subject.TryGetTarget(_definition, new Series { MetadataSource = MetadataSourceType.AniList, TvdbId = -12, ForeignId = "12" }, new ImportListItemInfo { TvdbId = 12 }, out _).Should().BeFalse();
            Subject.TryGetTarget(_definition, new Series { MetadataSource = MetadataSourceType.Tvdb, TvdbId = 12, SeasonType = "dvd" }, new ImportListItemInfo { TvdbId = 12 }, out _).Should().BeFalse();
        }

        [Test]
        public void should_not_acknowledge_unrecognized_or_partial_writes()
        {
            Respond(Identity, Show, Episode, "[]", "{\"added\":{\"episodes\":0},\"not_found\":{\"episodes\":[{\"ids\":{\"trakt\":201}}]}}");
            Action action = () => Subject.Advance(_definition, _target, 1);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Count(request => request.Method == HttpMethod.Post).Should().Be(1);
        }

        [Test]
        public void should_dispatch_a_mutation_once_on_rate_limiting()
        {
            Respond(Identity, Show, Episode, "[]");
            Mocker.GetMock<IHttpClient>().Setup(service => service.Execute(It.Is<HttpRequest>(request => request.Method == HttpMethod.Post)))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);
                    return new HttpResponse(request, new HttpHeader { { "Retry-After", "90" } }, "{}", HttpStatusCode.TooManyRequests);
                });
            Action action = () => Subject.Advance(_definition, _target, 1);
            action.Should().Throw<TooManyRequestsException>();
            _requests.Count(request => request.Method == HttpMethod.Post).Should().Be(1);
        }
    }
}
