namespace NzbDrone.Core.Download.MagnetMetadata
{
    public enum MagnetMetadataUninspectableReason
    {
        ProxyConfigured,
        NoAllowedTrackers,
        Timeout,
        NoPeers,
        TrackerFailure,
        InvalidMetadata,
        OversizedMetadata,
        InfoHashMismatch,
        EngineFailure
    }

    public sealed class MagnetMetadataResolutionResult
    {
        private MagnetMetadataResolutionResult(byte[] metadata, MagnetMetadataUninspectableReason? reason)
        {
            Metadata = metadata;
            Reason = reason;
        }

        public bool IsResolved => Metadata != null;

        public byte[] Metadata { get; }

        public MagnetMetadataUninspectableReason? Reason { get; }

        public static MagnetMetadataResolutionResult Resolved(byte[] metadata)
        {
            return new MagnetMetadataResolutionResult(metadata, null);
        }

        public static MagnetMetadataResolutionResult Uninspectable(MagnetMetadataUninspectableReason reason)
        {
            return new MagnetMetadataResolutionResult(null, reason);
        }
    }
}
