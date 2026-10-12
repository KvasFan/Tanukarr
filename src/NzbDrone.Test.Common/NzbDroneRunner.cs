using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using RestSharp;

namespace NzbDrone.Test.Common
{
    public class NzbDroneRunner
    {
        private readonly IProcessProvider _processProvider;
        private readonly IRestClient _restClient;
        private Process _nzbDroneProcess;
        private bool _enableAuth;

        public string AppData { get; private set; }
        public string ApiKey { get; private set; }
        public PostgresOptions PostgresOptions { get; private set; }
        public int Port { get; private set; }

        public NzbDroneRunner(Logger logger, PostgresOptions postgresOptions, int port = 0)
        {
            Port = port == 0 ? GetAvailablePort() : port;
            _processProvider = new ProcessProvider(logger);
            _restClient = new RestClient($"http://localhost:{Port}/api/v3");

            PostgresOptions = postgresOptions;
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        public void Start(bool enableAuth = false)
        {
            _enableAuth = enableAuth;
            AppData = Path.Combine(TestContext.CurrentContext.TestDirectory, "_intg_" + TestBase.GetUID());
            Directory.CreateDirectory(AppData);

            GenerateConfigFile(enableAuth);

            string consoleExe;
            if (OsInfo.IsWindows)
            {
                consoleExe = "Sonarr.Console.exe";
            }
            else
            {
                consoleExe = "Sonarr";
            }

            if (BuildInfo.IsDebug)
            {
                Start(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "_output", "net10.0", consoleExe));
            }
            else
            {
                Start(Path.Combine(TestContext.CurrentContext.TestDirectory, "bin", consoleExe));
            }

            var startupTimer = Stopwatch.StartNew();

            while (startupTimer.Elapsed < TimeSpan.FromMinutes(3))
            {
                _nzbDroneProcess.Refresh();

                if (_nzbDroneProcess.HasExited)
                {
                    Assert.Fail("Process has exited");
                }

                var request = new RestRequest("system/status");
                request.AddHeader("Authorization", ApiKey);
                request.AddHeader("X-Api-Key", ApiKey);

                var statusCall = _restClient.Get(request);

                // Require the expected instance, since another local service may occupy the port.
                if (statusCall.ResponseStatus == ResponseStatus.Completed &&
                    statusCall.StatusCode == HttpStatusCode.OK &&
                    Json.TryDeserialize<JObject>(statusCall.Content ?? string.Empty, out var status) &&
                    status?["appData"]?.Type == JTokenType.String &&
                    AppData.PathEquals(status.Value<string>("appData")))
                {
                    TestContext.Progress.WriteLine($"Sonarr {Port} is started. Running Tests");
                    return;
                }

                TestContext.Progress.WriteLine("Waiting for Sonarr {0} to start. Response Status : {1}  [{2}] {3}", Port, statusCall.ResponseStatus, statusCall.StatusDescription ?? statusCall.StatusCode.ToString(), statusCall.ErrorException?.Message);

                Thread.Sleep(500);
            }

            Assert.Fail($"Sonarr on port {Port} did not become ready within 3 minutes.");
        }

        public void Kill()
        {
            try
            {
                if (_nzbDroneProcess != null)
                {
                    _nzbDroneProcess.Refresh();
                    if (_nzbDroneProcess.HasExited)
                    {
                        var log = File.ReadAllLines(Path.Combine(AppData, "logs", "Sonarr.trace.txt"));
                        var output = log.Join(Environment.NewLine);
                        TestContext.Progress.WriteLine("Process has exited prematurely: ExitCode={0} Output:\n{1}", _nzbDroneProcess.ExitCode, output);
                    }

                    _processProvider.Kill(_nzbDroneProcess.Id);
                }
            }
            catch (InvalidOperationException)
            {
                // May happen if the process closes while being closed
            }

            TestBase.DeleteTempFolder(AppData);
        }

        public void KillAll()
        {
            try
            {
                if (_nzbDroneProcess != null)
                {
                    _processProvider.Kill(_nzbDroneProcess.Id);
                }

                _processProvider.KillAll(ProcessProvider.SONARR_CONSOLE_PROCESS_NAME);
                _processProvider.KillAll(ProcessProvider.SONARR_PROCESS_NAME);
            }
            catch (InvalidOperationException)
            {
                // May happen if the process closes while being closed
            }

            TestBase.DeleteTempFolder(AppData);
        }

        private void Start(string outputSonarrConsoleExe)
        {
            StringDictionary envVars = new();
            if (!_enableAuth)
            {
                envVars.Add("Sonarr__Auth__Method", "None");
            }

            if (PostgresOptions?.Host != null)
            {
                envVars.Add("Sonarr__Postgres__Host", PostgresOptions.Host);
                envVars.Add("Sonarr__Postgres__Port", PostgresOptions.Port.ToString());
                envVars.Add("Sonarr__Postgres__User", PostgresOptions.User);
                envVars.Add("Sonarr__Postgres__Password", PostgresOptions.Password);
                envVars.Add("Sonarr__Postgres__MainDb", PostgresOptions.MainDb);
                envVars.Add("Sonarr__Postgres__LogDb", PostgresOptions.LogDb);

                TestContext.Progress.WriteLine("Using env vars:\n{0}", envVars.ToJson());
            }

            TestContext.Progress.WriteLine("Starting instance from {0} on port {1}", outputSonarrConsoleExe, Port);

            var args = "-nobrowser -nosingleinstancecheck -data=\"" + AppData + "\"";
            _nzbDroneProcess = _processProvider.Start(outputSonarrConsoleExe, args, envVars, OnOutputDataReceived, OnOutputDataReceived);
        }

        private void OnOutputDataReceived(string data)
        {
            TestContext.Progress.WriteLine($" [{Port}] > " + data);

            if (data.Contains("Press enter to exit"))
            {
                _nzbDroneProcess.StandardInput.WriteLine(" ");
            }
        }

        private void GenerateConfigFile(bool enableAuth)
        {
            var configFile = Path.Combine(AppData, "config.xml");

            // Generate and set the api key so we don't have to poll the config file
            var apiKey = Guid.NewGuid().ToString().Replace("-", "");

            var xDoc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(ConfigFileProvider.CONFIG_ELEMENT_NAME,
                             new XElement(nameof(ConfigFileProvider.ApiKey), apiKey),
                             new XElement(nameof(ConfigFileProvider.LogLevel), "trace"),
                             new XElement(nameof(ConfigFileProvider.AnalyticsEnabled), false),
                             new XElement(nameof(ConfigFileProvider.AuthenticationMethod), enableAuth ? "Forms" : "None"),
                             new XElement(nameof(ConfigFileProvider.AuthenticationRequired), "DisabledForLocalAddresses"),
                             new XElement(nameof(ConfigFileProvider.AllowedHosts), "localhost"),
                             new XElement(nameof(ConfigFileProvider.Port), Port)));

            var data = xDoc.ToString();

            File.WriteAllText(configFile, data);

            ApiKey = apiKey;
        }

        public static void EnsureUiContent()
        {
            // Writes a dummy CSS file for testing static resources, since neither debug nor
            // release builds ship one as part of running the integration tests. Called once
            // per test assembly via a SetUpFixture so parallel fixtures don't race on the
            // shared file.

            var contentDirectory = BuildInfo.IsDebug
                ? Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "_output", "UI", "Content")
                : Path.Combine(TestContext.CurrentContext.TestDirectory, "bin", "UI", "Content");
            var stylesPath = Path.Combine(contentDirectory, "styles.css");

            Directory.CreateDirectory(contentDirectory);

            if (!File.Exists(stylesPath))
            {
                File.WriteAllText(stylesPath, ".test { display: flex; }");
            }
        }
    }
}
