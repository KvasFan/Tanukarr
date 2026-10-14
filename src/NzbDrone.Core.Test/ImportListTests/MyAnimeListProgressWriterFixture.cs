using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.MyAnimeList;
using NzbDrone.Core.ImportLists.Progress;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class MyAnimeListProgressWriterFixture : CoreTest<MyAnimeListProgressWriter>
    {
        private readonly List<HttpRequest> _requests = new();
        private MyAnimeListSettings _settings;
        private ImportListDefinition _definition;
        private ImportListProgressTarget _target;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _settings = new MyAnimeListSettings { Username = "@me", AccessToken = "PRIVATE-FIXTURE", Expires = DateTime.UtcNow.AddDays(1), SyncWatchedProgress = true };
            _definition = new ImportListDefinition { Id = 8, Settings = _settings };
            Subject.TryGetTarget(_definition,
                new Series { MetadataSource = MetadataSourceType.MyAnimeList, ForeignId = "12" },
                new ImportListItemInfo { MalId = 12 },
                out _target).Should().BeTrue();
        }

        private void Respond(params string[] responses)
        {
            var bodies = new Queue<string>(responses);
            Mocker.GetMock<IMyAnimeListRequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
            {
                _requests.Add(request);
                return new HttpResponse(request, new HttpHeader(), bodies.Dequeue());
            });
        }

        [Test]
        public void should_use_direct_mal_oauth_patch_and_distinguish_request_and_response_field_names()
        {
            Respond("{\"id\":7,\"name\":\"owner\"}", "{\"id\":12,\"num_episodes\":12,\"my_list_status\":{\"num_episodes_watched\":0}}", "{\"num_episodes_watched\":2}");
            Subject.Advance(_definition, _target, 2).Should().Be(2);
            _requests[1].Url.Query.Should().Contain("num_episodes_watched");
            _requests.Last().Method.Should().Be(HttpMethod.Patch);
            _requests.Last().Url.Path.Should().Be("/v2/anime/12/my_list_status");
            Encoding.UTF8.GetString(_requests.Last().ContentData).Should().Be("num_watched_episodes=2");
            _requests.Should().OnlyContain(request => !request.AllowAutoRedirect && !request.LogResponseContent && request.ContentSummary == null);
            _requests.Should().OnlyContain(request => request.Url.Host == "api.myanimelist.net");
        }

        [Test]
        public void should_preserve_higher_remote_progress_without_patch()
        {
            Respond("{\"id\":7,\"name\":\"owner\"}", "{\"id\":12,\"num_episodes\":12,\"my_list_status\":{\"num_episodes_watched\":9}}");
            Subject.Advance(_definition, _target, 2).Should().Be(9);
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_not_create_an_anime_without_existing_own_list_membership()
        {
            Respond("{\"id\":7,\"name\":\"owner\"}", "{\"id\":12,\"num_episodes\":12,\"my_list_status\":null}");
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_refuse_another_users_readable_list()
        {
            _settings.Username = "other-person";
            Respond("{\"id\":7,\"name\":\"owner\"}");
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().ContainSingle();
        }

        [Test]
        public void should_refuse_public_client_id_without_bearer_authentication()
        {
            _settings.AccessToken = null;
            _settings.ClientId = "fixture-public-client";
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_refuse_cross_catalog_and_custom_order_targets()
        {
            Subject.TryGetTarget(_definition, new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "12", MalIds = new HashSet<int> { 12 } }, new ImportListItemInfo { MalId = 12 }, out _).Should().BeFalse();
            Subject.TryGetTarget(_definition, new Series { MetadataSource = MetadataSourceType.MyAnimeList, ForeignId = "12", OrderingId = "different-cut" }, new ImportListItemInfo { MalId = 12 }, out _).Should().BeFalse();
        }
    }
}
