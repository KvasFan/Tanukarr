using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;
using MonoTorrent.Client;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Download.MagnetMetadata
{
    public class MagnetMetadataResolver : IMagnetMetadataResolver
    {
        internal const int MaximumMetadataSize = 16 * 1024 * 1024;
        internal const int MaximumConcurrentResolutions = 2;
        internal static readonly TimeSpan ResolutionTimeout = TimeSpan.FromSeconds(30);
        private static readonly SemaphoreSlim ResolutionSlots = new SemaphoreSlim(MaximumConcurrentResolutions, MaximumConcurrentResolutions);
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MagnetMetadataResolver(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public async Task<MagnetMetadataResolutionResult> ResolveAsync(MagnetLink resolutionMagnet, CancellationToken cancellationToken)
        {
            if (_configService.ProxyEnabled)
            {
                return MagnetMetadataResolutionResult.Uninspectable(MagnetMetadataUninspectableReason.ProxyConfigured);
            }

            var sanitizedMagnet = resolutionMagnet == null ? null : CreateResolutionMagnet(resolutionMagnet);
            if (sanitizedMagnet == null || sanitizedMagnet.AnnounceUrls.Count == 0)
            {
                return MagnetMetadataResolutionResult.Uninspectable(MagnetMetadataUninspectableReason.NoAllowedTrackers);
            }

            await ResolutionSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(ResolutionTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var cacheDirectory = Path.Combine(Path.GetTempPath(), "sonarr-magnet-metadata", Guid.NewGuid().ToString("N"));

            ClientEngine engine = null;
            MagnetMetadataResolutionResult result = null;
            var cleanupFailed = false;

            try
            {
                Directory.CreateDirectory(cacheDirectory);
                var engineSettings = CreateEngineSettings(cacheDirectory);
                var torrentSettings = CreateTorrentSettings();

                engine = new ClientEngine(engineSettings);
                await engine.StartAllAsync().ConfigureAwait(false);
                var metadata = await engine.DownloadMetadataAsync(sanitizedMagnet, torrentSettings, linked.Token).ConfigureAwait(false);

                if (metadata.IsEmpty)
                {
                    result = MagnetMetadataResolutionResult.Uninspectable(MagnetMetadataUninspectableReason.NoPeers);
                }
                else
                {
                    result = MagnetMetadataResolutionResult.Resolved(metadata.ToArray());
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = MagnetMetadataResolutionResult.Uninspectable(MagnetMetadataUninspectableReason.Timeout);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Magnet metadata resolution failed");
                result = MagnetMetadataResolutionResult.Uninspectable(MagnetMetadataUninspectableReason.EngineFailure);
            }
            finally
            {
                if (engine != null)
                {
                    try
                    {
                        await engine.StopAllAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        cleanupFailed = true;
                        _logger.Warn(ex, "Magnet metadata resolver engine cleanup failed");
                    }

                    try
                    {
                        engine.Dispose();
                    }
                    catch (Exception ex)
                    {
                        cleanupFailed = true;
                        _logger.Warn(ex, "Magnet metadata resolver engine disposal failed");
                    }
                }

                try
                {
                    if (Directory.Exists(cacheDirectory))
                    {
                        Directory.Delete(cacheDirectory, true);
                    }
                }
                catch (Exception ex)
                {
                    cleanupFailed = true;
                    _logger.Warn(ex, "Magnet metadata resolver temporary directory cleanup failed");
                }

                ResolutionSlots.Release();
            }

            if (cleanupFailed && result?.IsResolved == true)
            {
                return MagnetMetadataResolutionResult.Uninspectable(MagnetMetadataUninspectableReason.EngineFailure);
            }

            return result ?? MagnetMetadataResolutionResult.Uninspectable(MagnetMetadataUninspectableReason.EngineFailure);
        }

        public static MagnetLink CreateResolutionMagnet(MagnetLink originalMagnet)
        {
            var allowedTrackers = originalMagnet.AnnounceUrls
                .Where(IsAllowedTracker)
                .ToList();

            return new MagnetLink(originalMagnet.InfoHashes, originalMagnet.Name, allowedTrackers, Array.Empty<string>(), originalMagnet.Size);
        }

        internal static EngineSettings CreateEngineSettings(string cacheDirectory)
        {
            return new EngineSettingsBuilder
            {
                AllowLocalPeerDiscovery = false,
                AllowPortForwarding = false,
                AutoSaveLoadDhtCache = false,
                AutoSaveLoadFastResume = false,
                AutoSaveLoadMagnetLinkMetadata = false,
                CacheDirectory = cacheDirectory,
                DhtEndPoint = null,
                DiskCacheBytes = 0,
                ListenEndPoints = new Dictionary<string, IPEndPoint>(),
                MaximumMetadataSize = MaximumMetadataSize
            }.ToSettings();
        }

        internal static TorrentSettings CreateTorrentSettings()
        {
            return new TorrentSettingsBuilder
            {
                AllowDht = false,
                AllowPeerExchange = false,
                CreateContainingDirectory = false
            }.ToSettings();
        }

        private static bool IsAllowedTracker(string tracker)
        {
            return Uri.TryCreate(tracker, UriKind.Absolute, out var uri) &&
                   (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }
    }
}
