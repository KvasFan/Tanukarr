using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.AniList;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.ImportLists.Progress;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class AniListProgressWriterFixture : CoreTest<AniListProgressWriter>
    {
        private const string Identity = "{\"data\":{\"Viewer\":{\"id\":7,\"name\":\"owner\"},\"User\":{\"id\":7,\"name\":\"owner\"}}}";
        private readonly List<HttpRequest> _requests = new();
        private AniListSettings _settings;
        private ImportListDefinition _definition;
        private ImportListProgressTarget _target;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _settings = new AniListSettings { Username = "owner", AccessToken = "PRIVATE-FIXTURE", Expires = DateTime.UtcNow.AddDays(1), SyncWatchedProgress = true };
            _definition = new ImportListDefinition { Id = 8, Settings = _settings };
            Subject.TryGetTarget(_definition,
                new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "12" },
                new ImportListItemInfo { AniListId = 12 },
                out _target).Should().BeTrue();
        }

        private void Respond(params string[] responses)
        {
            var bodies = new Queue<string>(responses);
            Mocker.GetMock<IAniListRequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
            {
                _requests.Add(request);
                return new HttpResponse(request, new HttpHeader(), bodies.Dequeue());
            });
        }

        private static string Entry(int progress = 0, int total = 12) => new JObject
        {
            ["data"] = new JObject
            {
                ["MediaList"] = new JObject
                {
                    ["id"] = 91,
                    ["userId"] = 7,
                    ["mediaId"] = 12,
                    ["progress"] = progress,
                    ["media"] = new JObject { ["type"] = "ANIME", ["episodes"] = total }
                }
            }
        }.ToString();

        [Test]
        public void should_advance_only_the_existing_own_entry_progress_with_real_json()
        {
            Respond(Identity, Entry(), "{\"data\":{\"SaveMediaListEntry\":{\"id\":91,\"userId\":7,\"mediaId\":12,\"progress\":2}}}");
            Subject.Advance(_definition, _target, 2).Should().Be(2);
            var mutation = JObject.Parse(System.Text.Encoding.UTF8.GetString(_requests.Last().ContentData));
            mutation["query"].Value<string>().Should().Contain("SaveMediaListEntry(id:$entryId,progress:$progress)").And.NotContain("status:").And.NotContain("customLists:");
            ((JObject)mutation["variables"]).Properties().Select(property => property.Name).Should().BeEquivalentTo("entryId", "progress");
            mutation["variables"]["entryId"].Value<int>().Should().Be(91);
            mutation["variables"]["progress"].Value<int>().Should().Be(2);
            _requests.Should().OnlyContain(request => request.Method == HttpMethod.Post && !request.AllowAutoRedirect && !request.LogResponseContent && request.ContentSummary == null);
            _requests.Last().Headers["Authorization"].Should().Be("Bearer PRIVATE-FIXTURE");
        }

        [Test]
        public void should_preserve_a_higher_remote_progress_without_mutation()
        {
            Respond(Identity, Entry(8));
            Subject.Advance(_definition, _target, 2).Should().Be(8);
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_refuse_a_public_list_owned_by_another_account()
        {
            Respond("{\"data\":{\"Viewer\":{\"id\":7},\"User\":{\"id\":99}}}");
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().ContainSingle();
        }

        [Test]
        public void should_not_create_an_entry_when_membership_is_missing()
        {
            Respond(Identity, "{\"data\":{\"MediaList\":null}}");
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_refuse_a_provider_total_mismatch_before_mutation()
        {
            Respond(Identity, Entry(0, 1));
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_require_opt_in_and_native_catalog_identity()
        {
            _settings.SyncWatchedProgress = false;
            Subject.TryGetTarget(_definition, new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "12" }, new ImportListItemInfo { AniListId = 12 }, out _).Should().BeFalse();
            _settings.SyncWatchedProgress = true;
            Subject.TryGetTarget(_definition, new Series { MetadataSource = MetadataSourceType.Tvdb, TvdbId = 88, AniListIds = new HashSet<int> { 12 } }, new ImportListItemInfo { AniListId = 12 }, out _).Should().BeFalse();
        }

        [Test]
        public void should_not_dispatch_an_expired_token()
        {
            _settings.Expires = DateTime.UtcNow.AddMinutes(-1);
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>();
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_not_acknowledge_graphql_errors_or_wrong_saved_identity()
        {
            Respond(Identity, Entry(), "{\"errors\":[{\"message\":\"PRIVATE-FIXTURE\"}],\"data\":{\"SaveMediaListEntry\":null}}");
            Action action = () => Subject.Advance(_definition, _target, 2);
            action.Should().Throw<ImportListProgressUnavailableException>().WithMessage("*Watched progress requires*");
            _requests.Should().HaveCount(3);
        }
    }
}
