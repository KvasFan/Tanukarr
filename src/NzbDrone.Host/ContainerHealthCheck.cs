using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Host
{
    public static class ContainerHealthCheck
    {
        public static int Run(string[] args)
        {
            try
            {
                var context = new StartupContext(args);
                var configuration = new ConfigurationBuilder()
                    .AddXmlFile(new AppFolderInfo(context).GetConfigPath(), optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .Build();
                using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, CheckCertificateRevocationList = true };
                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(3),
                    MaxResponseContentBufferSize = 4096
                };
                return ProbeAsync(client, GetEndpoint(configuration)).GetAwaiter().GetResult() ? 0 : 1;
            }
            catch
            {
                // Health output is retained by Docker. Never export configuration,
                // credentials, paths or response bodies to that diagnostic log.
                return 1;
            }
        }

        public static Uri GetEndpoint(IConfiguration configuration)
        {
            var port = configuration.GetValue<int?>("Sonarr:Server:Port") ?? configuration.GetValue("Port", 8989);
            if (port is < 1 or > 65535)
            {
                throw new InvalidDataException("Invalid health check port");
            }

            var bind = (configuration["Sonarr:Server:BindAddress"] ?? configuration["BindAddress"] ?? "*").ToLowerInvariant();
            var address = bind switch
            {
                "*" or "0.0.0.0" or "localhost" => IPAddress.Loopback,
                "::" or "[::]" => IPAddress.IPv6Loopback,
                _ => IPAddress.Parse(bind.Trim('[', ']'))
            };
            if (!IPAddress.IsLoopback(address) && !NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Any(unicast => unicast.Address.Equals(address)))
            {
                throw new InvalidDataException("Health checks require a local bind address");
            }

            var urlBase = (configuration["Sonarr:Server:UrlBase"] ?? configuration["UrlBase"] ?? string.Empty).Trim('/');
            var path = urlBase.Length == 0 ? "/ping" : $"/{urlBase}/ping";
            return new UriBuilder(Uri.UriSchemeHttp, address.ToString(), port, path).Uri;
        }

        public static async Task<bool> ProbeAsync(HttpClient client, Uri endpoint)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Host = "localhost";
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return false;
            }

            var body = await response.Content.ReadAsStringAsync();
            return JObject.Parse(body).Value<string>("status") == "OK";
        }
    }
}
