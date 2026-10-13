using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.ImportLists.Simkl;
using NzbDrone.Core.ImportLists.Simkl.User;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class SimklUserV2Fixture : CoreTest<SimklUserImport>
    {
        private SimklUserSettings _settings;
        private SimklCustomListResult _result;

        [SetUp]
        public void Setup()
        {
            _settings = new SimklUserSettings { ClientIdV2 = "generated-client", AccessToken = "generated-access", RefreshToken = "generated-refresh", Expires = DateTime.UtcNow.AddDays(2), AuthUser = "91" };
            Subject.Definition = new ImportListDefinition { Id = 10, Name = "SIMKL controlled fixture", Settings = _settings };
            _result = new SimklCustomListResult
            {
                Items = new List<ImportListItemInfo> { new() { MalId = 11, Title = "Shared title" }, new() { MalId = 12, Title = "Shared title" }, new() { MalId = 11, Title = "Duplicate title alias" } },
                WatchlistSnapshot = new SimklV2WatchlistSnapshot()
            };
            Mocker.GetMock<ISimklCustomListReader>().Setup(reader => reader.Read(_settings)).Returns(_result);
        }

        [Test]
        public void should_commit_only_a_complete_successful_fetch_and_deduplicate_by_metadata_identity()
        {
            var result = Subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Should().HaveCount(2);
            result.Series.Should().OnlyContain(item => item.ImportListId == 10 && item.ImportList == "SIMKL controlled fixture");
            Mocker.GetMock<ISimklCustomListReader>().Verify(reader => reader.Commit(_settings, _result), Times.Once());
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordSuccess(10), Times.Once());
        }

        [Test]
        public void should_not_commit_inventory_or_save_unsaved_settings_during_test_even_after_refresh()
        {
            Mocker.GetMock<ISimklV2AuthService>().Setup(auth => auth.RefreshIfNeeded(_settings)).Returns(true);
            Subject.Test().IsValid.Should().BeTrue();
            Mocker.GetMock<IImportListRepository>().Verify(repository => repository.UpdateSettings(It.IsAny<ImportListDefinition>()), Times.Never());
            Mocker.GetMock<ISimklCustomListReader>().Verify(reader => reader.Commit(It.IsAny<SimklUserSettings>(), It.IsAny<SimklCustomListResult>()), Times.Never());
        }

        [Test]
        public void should_save_a_renewed_token_only_during_fetch()
        {
            Mocker.GetMock<ISimklV2AuthService>().Setup(auth => auth.RefreshIfNeeded(_settings)).Returns(true);
            Subject.Fetch().AnyFailure.Should().BeFalse();
            Mocker.GetMock<IImportListRepository>().Verify(repository => repository.UpdateSettings((ImportListDefinition)Subject.Definition), Times.Once());
        }

        [Test]
        public void should_retain_the_previous_complete_list_and_never_commit_a_failed_membership_read()
        {
            Mocker.GetMock<ISimklCustomListReader>().Setup(reader => reader.Read(_settings)).Throws(new ImportListException(null, "generated incomplete membership"));
            var result = Subject.Fetch();
            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            Mocker.GetMock<ISimklCustomListReader>().Verify(reader => reader.Commit(It.IsAny<SimklUserSettings>(), It.IsAny<SimklCustomListResult>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_keep_the_existing_v1_request_generator_when_optional_v2_is_unset()
        {
            _settings.ClientIdV2 = null;
            Subject.GetRequestGenerator().Should().BeOfType<SimklUserRequestGenerator>();
            Mocker.GetMock<ISimklV2AuthService>().Verify(auth => auth.RefreshIfNeeded(It.IsAny<SimklUserSettings>()), Times.Never());
            Mocker.GetMock<ISimklCustomListReader>().Verify(reader => reader.Read(It.IsAny<SimklUserSettings>()), Times.Never());
        }
    }
}
