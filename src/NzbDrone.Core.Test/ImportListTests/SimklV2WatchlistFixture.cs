using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.ImportLists.Simkl;
using NzbDrone.Core.ImportLists.Simkl.User;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class SimklV2WatchlistFixture : CoreTest<SimklV2WatchlistService>
    {
        private const string First = "2026-10-13T15:00:00.123456Z";
        private const string Second = "2026-10-13T16:00:00.654321Z";
        private readonly List<HttpRequest> _requests = new();
        private SimklUserSettings _settings;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.GetMock<IAppFolderInfo>().SetupGet(info => info.AppDataFolder).Returns(TempFolder);
            _settings = new SimklUserSettings { ClientIdV2 = "fixture-client", AuthUser = "42", AccessToken = "PRIVATE-ACCESS", RefreshToken = "PRIVATE-REFRESH", ClientSecretV2 = "PRIVATE-SECRET" };
        }

        private static string Activities(string all = First, string watching = First, string completed = First, string removed = First)
        {
            return new JObject
            {
                ["all"] = all,
                ["tv_shows"] = new JObject
                {
                    ["all"] = all,
                    ["watching"] = watching,
                    ["plantowatch"] = First,
                    ["hold"] = First,
                    ["completed"] = completed,
                    ["dropped"] = First,
                    ["removed_from_list"] = removed
                },
                ["custom_lists"] = new JObject { ["lists"] = new JObject { ["all"] = all } }
            }.ToString();
        }

        private static JObject Row(int id, string status = "watching")
        {
            return new JObject
            {
                ["status"] = status,
                ["show"] = new JObject { ["title"] = "Same title", ["ids"] = new JObject { ["simkl"] = id, ["tvdb"] = id + 1000 } }
            };
        }

        private static string Rows(params JObject[] rows) => new JObject { ["shows"] = new JArray(rows) }.ToString();

        private void Respond(params string[] bodies)
        {
            var queue = new Queue<string>(bodies);
            Mocker.GetMock<ISimklV2RequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>())).Returns((HttpRequest request) =>
            {
                _requests.Add(request);
                return new HttpResponse(request, new HttpHeader(), queue.Dequeue());
            });
        }

        private void Baseline(params JObject[] rows)
        {
            Respond(Activities(), Rows(rows));
            Subject.Commit(_settings, Subject.Read(_settings));
            _requests.Clear();
        }

        private string SavedPath() => Directory.GetFiles(Path.Combine(TempFolder, "cache", "simkl-v2"), "*.json").Single();

        [Test]
        public void should_share_one_staged_inventory_and_commit_it_once_while_preserving_the_incremental_baseline()
        {
            Respond(Activities(), Rows(Row(1)));
            using (ImportListSnapshotScope.Begin())
            {
                var first = Subject.Read(_settings);
                var second = Subject.Read(_settings);
                second.Should().BeSameAs(first);
                _requests.Should().HaveCount(2);
                Subject.Commit(_settings, first);
                var saved = File.ReadAllText(SavedPath());
                Subject.Commit(_settings, second);
                File.ReadAllText(SavedPath()).Should().Be(saved);
                saved.Should().NotContain("CommittedRevision").And.NotContain("PRIVATE-ACCESS");
            }

            ImportListSnapshotScope.Current.Should().BeNull();
            File.Exists(SavedPath()).Should().BeTrue();
            Respond(Activities());
            Subject.Read(_settings).Rows.Select(row => row.Show.Ids.Simkl).Should().Equal(1);
            _requests.Should().HaveCount(3);
            _requests.Last().Url.Path.Should().Be("/sync/activities");
        }

        [Test]
        public void should_still_reject_a_shared_stage_after_an_independent_sync_commits_a_newer_revision()
        {
            using var import = ImportListSnapshotScope.Begin();
            Respond(Activities(), Rows(Row(1)));
            var staged = Subject.Read(_settings);
            Subject.Commit(_settings, staged);
            Respond(Activities(Second, Second), Rows(Row(2)));
            using (ImportListSnapshotScope.Begin())
            {
                Subject.Commit(_settings, Subject.Read(_settings));
            }

            var newer = File.ReadAllText(SavedPath());
            Action commit = () => Subject.Commit(_settings, staged);
            commit.Should().Throw<ImportListException>();
            File.ReadAllText(SavedPath()).Should().Be(newer);
        }

        [Test]
        public void should_revalidate_a_changed_token_instead_of_reusing_the_account_activity_response()
        {
            using var scope = ImportListSnapshotScope.Begin();
            Respond(Activities(), Rows(Row(1)), Activities());
            var original = Subject.Read(_settings);
            Subject.Commit(_settings, original);
            _settings.AccessToken = "OTHER-GENERATED-TOKEN";
            Subject.Read(_settings).Should().NotBeSameAs(original);
            _requests.Should().HaveCount(3);
            _requests.Last().Headers["Authorization"].Should().Be("Bearer OTHER-GENERATED-TOKEN");
        }

        [Test]
        public void should_stage_all_statuses_after_activities_and_persist_only_on_commit_without_credentials()
        {
            Respond(Activities(), Rows(Row(1), Row(2, "completed")));

            var staged = Subject.Read(_settings);

            staged.Rows.Select(row => row.Status).Should().Equal("watching", "completed");
            Directory.Exists(Path.Combine(TempFolder, "cache", "simkl-v2")).Should().BeFalse();
            _requests.Select(request => request.Url.Path).Should().Equal("/sync/activities", "/sync/all-items/shows");
            _requests.Last().Url.Query.Should().NotContain("date_from");
            Subject.Commit(_settings, staged);
            var saved = File.ReadAllText(SavedPath());
            saved.Should().NotContain("PRIVATE-ACCESS").And.NotContain("PRIVATE-REFRESH").And.NotContain("PRIVATE-SECRET").And.NotContain("fixture-client");
            saved.Should().Contain(First);
            if (!OperatingSystem.IsWindows())
            {
                var publicAccess = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
                (File.GetUnixFileMode(SavedPath()) & publicAccess).Should().Be(0);
            }
        }

        [Test]
        public void should_resume_after_service_restart_and_merge_status_changes_with_exact_server_cursor()
        {
            Baseline(Row(1), Row(2));
            Respond(Activities(Second, Second, Second), Rows(Row(1, "completed"), Row(3)));
            var restarted = new SimklV2WatchlistService(Mocker.GetMock<ISimklV2RequestService>().Object, Mocker.GetMock<IAppFolderInfo>().Object, Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>().Object, new CacheManager());

            var staged = restarted.Read(_settings);

            staged.Rows.Select(row => row.Show.Ids.Simkl).Should().Equal(1, 2, 3);
            staged.Rows[0].Status.Should().Be("completed");
            Uri.UnescapeDataString(_requests[1].Url.Query).Should().Contain("date_from=" + First);
            File.ReadAllText(SavedPath()).Should().NotContain(Second);
            restarted.Commit(_settings, staged);
            File.ReadAllText(SavedPath()).Should().Contain(Second);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_not_read_the_watchlist_when_all_or_only_custom_activity_is_unchanged(bool customChanged)
        {
            Baseline(Row(1));
            Respond(Activities(customChanged ? Second : First));

            var staged = Subject.Read(_settings);

            staged.Rows.Should().ContainSingle();
            _requests.Should().ContainSingle().Which.Url.Path.Should().Be("/sync/activities");
        }

        [Test]
        public void should_reconcile_deleted_native_ids_only_when_removal_activity_changes()
        {
            Baseline(Row(1), Row(2));
            Respond(Activities(Second, removed: Second), Rows(new JObject { ["show"] = new JObject { ["ids"] = new JObject { ["simkl"] = 2 } } }));

            var staged = Subject.Read(_settings);

            staged.Rows.Should().ContainSingle().Which.Show.Ids.Simkl.Should().Be(2);
            _requests.Count.Should().Be(2);
            _requests.Last().Url.Query.Should().Contain("extended=simkl_ids_only").And.NotContain("date_from=");
        }

        [Test]
        public void should_keep_the_saved_cursor_and_inventory_when_a_delta_is_malformed()
        {
            Baseline(Row(1));
            var original = File.ReadAllText(SavedPath());
            Respond(Activities(Second, Second), "{\"shows\":[{\"status\":\"watching\",\"show\":{\"title\":\"Incomplete\"}}]}");
            Action action = () => Subject.Read(_settings);

            action.Should().Throw<ImportListException>();
            File.ReadAllText(SavedPath()).Should().Be(original);
            Respond(Activities(Second, Second), Rows(Row(2)));
            _requests.Clear();
            Subject.Read(_settings).Rows.Select(row => row.Show.Ids.Simkl).Should().Equal(1, 2);
            Uri.UnescapeDataString(_requests.Last().Url.Query).Should().Contain("date_from=" + First);
        }

        [Test]
        public void should_refuse_an_older_concurrent_commit_instead_of_overwriting_the_new_cursor()
        {
            Baseline(Row(1));
            Respond(Activities(Second, Second), Rows(Row(2)), Activities(Second, Second), Rows(Row(3)));
            var first = Subject.Read(_settings);
            var concurrent = Subject.Read(_settings);
            Subject.Commit(_settings, first);
            var committed = File.ReadAllText(SavedPath());
            Action action = () => Subject.Commit(_settings, concurrent);

            action.Should().Throw<ImportListException>();
            File.ReadAllText(SavedPath()).Should().Be(committed);
        }

        [Test]
        public void should_accept_an_authoritative_empty_bucket_and_avoid_repeating_the_initial_full_pull()
        {
            Respond(Activities(), "{}");
            var initial = Subject.Read(_settings);
            initial.Rows.Should().BeEmpty();
            Subject.Commit(_settings, initial);
            _requests.Clear();
            Respond(Activities());

            Subject.Read(_settings).Rows.Should().BeEmpty();

            _requests.Should().ContainSingle();
        }

        [TestCase("account")]
        [TestCase("client")]
        [TestCase("media")]
        public void should_scope_saved_inventory_to_account_client_and_media(string change)
        {
            Baseline(Row(1));
            if (change == "account")
            {
                _settings.AuthUser = "43";
            }
            else if (change == "client")
            {
                _settings.ClientIdV2 = "another-client";
            }
            else
            {
                _settings.ShowType = (int)SimklUserShowType.Anime;
            }

            var activity = change == "media" ? Activities().Replace("tv_shows", "anime") : Activities();
            Respond(activity, "{}");

            Subject.Read(_settings).Rows.Should().BeEmpty();

            _requests.Count.Should().Be(2);
            _requests.Last().Url.Query.Should().NotContain("date_from=");
        }

        [Test]
        public void should_retain_an_unreadable_persisted_inventory_instead_of_silently_starting_full_sync_again()
        {
            Baseline(Row(1));
            File.WriteAllText(SavedPath(), "{broken");
            Action action = () => Subject.Read(_settings);

            action.Should().Throw<ImportListException>();
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_refuse_a_backward_server_cursor_without_changing_persisted_inventory()
        {
            Baseline(Row(1));
            var original = File.ReadAllText(SavedPath());
            Respond(Activities("2026-10-13T14:00:00Z"));
            Action action = () => Subject.Read(_settings);

            action.Should().Throw<ImportListException>();
            File.ReadAllText(SavedPath()).Should().Be(original);
            _requests.Should().ContainSingle();
        }

        [Test]
        public void should_handle_a_brand_new_null_cursor_without_inventing_a_local_date_from()
        {
            const string emptyActivity = "{\"all\":null,\"tv_shows\":{\"watching\":null,\"plantowatch\":null,\"hold\":null,\"completed\":null,\"dropped\":null,\"removed_from_list\":null}}";
            Respond(emptyActivity, "{}");
            Subject.Commit(_settings, Subject.Read(_settings));
            _requests.Clear();
            Respond(emptyActivity);
            Subject.Read(_settings).Rows.Should().BeEmpty();
            _requests.Should().ContainSingle();
            _requests.Clear();
            Respond(Activities(removed: null), Rows(Row(1)));

            Subject.Read(_settings).Rows.Should().ContainSingle();

            _requests.Count.Should().Be(2);
            _requests.Last().Url.Query.Should().NotContain("date_from=");
        }

        [Test]
        public void should_retain_the_inventory_when_a_previously_valid_activity_cursor_becomes_null()
        {
            Baseline(Row(1));
            var original = File.ReadAllText(SavedPath());
            Respond(Activities(all: null));
            Action action = () => Subject.Read(_settings);

            action.Should().Throw<ImportListException>();
            File.ReadAllText(SavedPath()).Should().Be(original);
            _requests.Should().ContainSingle().Which.Url.Path.Should().Be("/sync/activities");
        }
    }
}
