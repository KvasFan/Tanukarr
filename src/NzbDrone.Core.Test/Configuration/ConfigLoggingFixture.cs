using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class ConfigLoggingFixture
    {
        private LogFactory _factory;
        private MemoryTarget _target;
        private Mock<IConfigRepository> _repository;
        private ConfigService _service;

        [SetUp]
        public void Setup()
        {
            _target = new MemoryTarget { Layout = "${message}" };
            var logging = new LoggingConfiguration();
            logging.AddRule(LogLevel.Trace, LogLevel.Fatal, _target);
            _factory = new LogFactory { Configuration = logging };
            _repository = new Mock<IConfigRepository>();
            _repository.Setup(repository => repository.All()).Returns(new List<Config>());
            _service = new ConfigService(_repository.Object, Mock.Of<IEventAggregator>(), _factory.GetLogger("ConfigSecretTest"));
        }

        [TearDown]
        public void Teardown()
        {
            _factory.Dispose();
        }

        [Test]
        public void should_persist_metadata_keys_without_exporting_their_values_at_trace_level()
        {
            const string tmdb = "generated-tmdb-only-key";
            const string omdb = "generated-omdb-only-key";
            _service.TmdbApiKey = tmdb;
            _service.OmdbApiKey = omdb;

            _repository.Verify(repository => repository.Upsert("tmdbapikey", tmdb), Times.Once());
            _repository.Verify(repository => repository.Upsert("omdbapikey", omdb), Times.Once());
            var messages = string.Join("\n", _target.Logs);
            messages.Should().Contain("tmdbapikey").And.Contain("omdbapikey");
            messages.Should().NotContain(tmdb).And.NotContain(omdb);
        }

        [Test]
        public void should_not_export_generated_hmac_salt_when_using_or_persisting_a_default()
        {
            var salt = _service.HmacSalt;
            salt.Should().NotBeNullOrWhiteSpace();
            _repository.Verify(repository => repository.Upsert("hmacsalt", salt), Times.Once());
            var messages = string.Join("\n", _target.Logs);
            messages.Should().Contain("Using default config value for 'hmacsalt'");
            messages.Should().Contain("Writing Setting to database. Key:'hmacsalt'").And.NotContain(salt);
        }

        [Test]
        public void should_keep_setting_change_diagnostics_and_store_the_original_value()
        {
            _service.RssSyncInterval = 12;
            _repository.Verify(repository => repository.Upsert("rsssyncinterval", "12"), Times.Once());
            _target.Logs.Should().ContainSingle().Which.Should().Be("Writing Setting to database. Key:'rsssyncinterval'");
        }
    }
}
