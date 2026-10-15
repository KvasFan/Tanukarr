using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using NzbDrone.Host;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class ContainerHealthCheckFixture
    {
        [Test]
        public void should_use_the_configured_http_port_and_url_base_without_an_api_key()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Port"] = "8990",
                ["UrlBase"] = "/tanukarr/",
                ["ApiKey"] = "must-not-be-sent"
            }).Build();
            ContainerHealthCheck.GetEndpoint(config).AbsoluteUri.Should().Be("http://127.0.0.1:8990/tanukarr/ping");
        }

        [Test]
        public void should_respect_server_option_precedence()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Port"] = "8990",
                ["UrlBase"] = "/old",
                ["Sonarr:Server:Port"] = "8991",
                ["Sonarr:Server:UrlBase"] = "new"
            }).Build();
            ContainerHealthCheck.GetEndpoint(config).AbsoluteUri.Should().Be("http://127.0.0.1:8991/new/ping");
        }

        [TestCase("*", "127.0.0.1")]
        [TestCase("0.0.0.0", "127.0.0.1")]
        [TestCase("localhost", "127.0.0.1")]
        [TestCase("::", "[::1]")]
        [TestCase("[::1]", "[::1]")]
        public void should_probe_the_corresponding_loopback_address(string bind, string host)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["BindAddress"] = bind
            }).Build();
            ContainerHealthCheck.GetEndpoint(config).Host.Should().Be(host);
        }

        [Test]
        public void should_refuse_a_remote_bind_address()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["BindAddress"] = "203.0.113.123"
            }).Build();
            Assert.Throws<System.IO.InvalidDataException>(() => ContainerHealthCheck.GetEndpoint(config));
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("65536")]
        public void should_refuse_an_invalid_port(string port)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["Port"] = port }).Build();
            Assert.Throws<System.IO.InvalidDataException>(() => ContainerHealthCheck.GetEndpoint(config));
        }

        [TestCase(HttpStatusCode.OK, "{\"status\":\"OK\"}", true)]
        [TestCase(HttpStatusCode.OK, "{\"status\":\"Error\"}", false)]
        [TestCase(HttpStatusCode.InternalServerError, "{\"status\":\"Error\"}", false)]
        [TestCase(HttpStatusCode.Redirect, "", false)]
        public async Task should_require_a_successful_database_ping(HttpStatusCode status, string body, bool expected)
        {
            using var handler = new PingHandler(status, body);
            using var client = new HttpClient(handler);
            var result = await ContainerHealthCheck.ProbeAsync(client, new System.Uri("http://127.0.0.1:8989/ping"));
            result.Should().Be(expected);
            handler.Request.Headers.Host.Should().Be("localhost");
            handler.Request.Headers.Authorization.Should().BeNull();
            handler.Request.Headers.Contains("X-Api-Key").Should().BeFalse();
        }

        private sealed class PingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            public PingHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            public HttpRequestMessage Request { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
            }
        }
    }
}
