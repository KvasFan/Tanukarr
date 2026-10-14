using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Progress;
using NzbDrone.Core.ImportLists.Simkl;
using NzbDrone.Core.ImportLists.Simkl.User;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class SimklProgressWriterFixture : CoreTest<SimklProgressWriter>
    {
        private const string Member = "[{\"simkl\":44,\"list\":\"watching\",\"result\":true}]";
        private const string Saved = "{\"added\":{\"episodes\":2,\"movies\":0},\"not_found\":{\"movies\":[],\"shows\":[],\"episodes\":[]}}";
        private readonly List<HttpRequest> _requests = new();
        private SimklUserSettings _settings;
        private ImportListDefinition _definition;
        private ImportListProgressTarget _target;
        private Series _series;
        private ImportListItemInfo _item;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _settings = new SimklUserSettings { ClientIdV2 = "fixture-client", AuthUser = "7", AccessToken = "PRIVATE-FIXTURE", Expires = DateTime.UtcNow.AddDays(1), ScopeV2 = "media:read media:write", SyncWatchedProgress = true };
            _definition = new ImportListDefinition { Id = 8, Settings = _settings };
            _series = new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "12", AnimeGroupSeasonNumber = 3 };
            _item = new ImportListItemInfo { AniListId = 12 };
            Subject.TryGetTarget(_definition, _series, _item, out _target).Should().BeTrue();
            _target.NativeEpisodes = new List<Episode>
            {
                new Episode { Id = 1, SeasonNumber = 1, EpisodeNumber = 1 },
                new Episode { Id = 2, SeasonNumber = 1, EpisodeNumber = 2 }
            };
        }

        private void Respond(params string[] responses)
        {
            var bodies = new Queue<string>(responses);
            HttpResponse Read(HttpRequest request)
            {
                _requests.Add(request);
                return new HttpResponse(request, new HttpHeader(), bodies.Dequeue(), request.Url.Path == "/sync/history" ? HttpStatusCode.Created : HttpStatusCode.OK);
            }

            Mocker.GetMock<ISimklV2RequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>())).Returns((HttpRequest request) => Read(request));
            Mocker.GetMock<ISimklV2RequestService>().Setup(service => service.ExecuteOnce(It.IsAny<HttpRequest>())).Returns((HttpRequest request) => Read(request));
        }

        [Test]
        public void should_write_explicit_native_episodes_without_whole_season_expansion_or_rewatch()
        {
            Respond("{\"account\":{\"id\":7}}", Member, Saved);
            Subject.Advance(_definition, _target, 2).Should().Be(2);
            var update = _requests.Last();
            var payload = JObject.Parse(System.Text.Encoding.UTF8.GetString(update.ContentData));
            var show = payload["shows"][0];
            show["ids"]["simkl"].Value<int>().Should().Be(44);
            JArray.Parse(System.Text.Encoding.UTF8.GetString(_requests[1].ContentData))[0]["ids"]["anilist"].Value<string>().Should().Be("12");
            show["seasons"][0]["number"].Value<int>().Should().Be(1);
            show["seasons"][0]["episodes"].Select(episode => episode["number"].Value<int>()).Should().Equal(1, 2);
            show["status"].Should().BeNull();
            update.Url.Query.Should().Contain("skip_auto_watching=yes").And.NotContain("allow_rewatch");
            update.Headers.GetValues("Authorization").Should().Equal("Bearer PRIVATE-FIXTURE");
            Mocker.GetMock<ISimklV2RequestService>().Verify(service => service.ExecuteOnce(It.IsAny<HttpRequest>()), Times.Once());
        }

        [TestCase(null)]
        [TestCase("media:read")]
        [TestCase("media:write-extra")]
        public void should_require_a_real_write_grant_before_any_request(string scope)
        {
            _settings.ScopeV2 = scope;
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_not_create_membership_when_existing_list_entry_is_missing()
        {
            Respond("{\"account\":{\"id\":7}}", "[{\"simkl\":44,\"list\":null,\"result\":false}]");
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            Mocker.GetMock<ISimklV2RequestService>().Verify(service => service.ExecuteOnce(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_refuse_changed_authenticated_accounts()
        {
            Respond("{\"account\":{\"id\":99}}");
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().ContainSingle();
        }

        [Test]
        public void should_enable_the_documented_tvdb_anime_numbering_switch()
        {
            _series.MetadataSource = MetadataSourceType.Tvdb;
            _series.TvdbId = 12;
            _series.SeasonType = SeasonType.Official;
            _item.TvdbId = 12;
            Subject.TryGetTarget(_definition, _series, _item, out var target).Should().BeTrue();
            target.NativeEpisodes = _target.NativeEpisodes;
            Respond("{\"account\":{\"id\":7}}", Member, Saved);
            Subject.Advance(_definition, target, 2).Should().Be(2);
            var show = JObject.Parse(System.Text.Encoding.UTF8.GetString(_requests.Last().ContentData))["shows"][0];
            show["ids"]["simkl"].Value<int>().Should().Be(44);
            JArray.Parse(System.Text.Encoding.UTF8.GetString(_requests[1].ContentData))[0]["ids"]["tvdb"].Value<string>().Should().Be("12");
            show["use_tvdb_anime_seasons"].Value<bool>().Should().BeTrue();
        }

        [Test]
        public void should_treat_documented_duplicate_noop_as_success_without_enabling_rewatch()
        {
            Respond("{\"account\":{\"id\":7}}", Member, Saved.Replace("\"episodes\":2", "\"episodes\":0", StringComparison.Ordinal));
            Subject.Advance(_definition, _target, 2).Should().Be(2);
            _requests.Last().Url.Query.Should().NotContain("allow_rewatch");
        }

        [Test]
        public void should_not_acknowledge_the_http_status_when_not_found_is_nonempty()
        {
            Respond("{\"account\":{\"id\":7}}", Member, "{\"added\":{\"episodes\":0},\"not_found\":{\"episodes\":[{\"number\":1}]}}");
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
        }

        [Test]
        public void should_use_movie_completion_only_for_a_single_native_movie_episode()
        {
            _series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            Subject.TryGetTarget(_definition, _series, _item, out var target).Should().BeTrue();
            target.NativeEpisodes = _target.NativeEpisodes.Take(1).ToList();
            Respond("{\"account\":{\"id\":7}}", Member, Saved);
            Subject.Advance(_definition, target, 1).Should().Be(1);
            var payload = JObject.Parse(System.Text.Encoding.UTF8.GetString(_requests.Last().ContentData));
            payload["movies"].Should().NotBeNull();
            payload["shows"].Should().BeNull();
        }
    }
}
