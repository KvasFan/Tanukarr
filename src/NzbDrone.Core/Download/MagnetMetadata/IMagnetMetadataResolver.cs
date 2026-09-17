using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;

namespace NzbDrone.Core.Download.MagnetMetadata
{
    public interface IMagnetMetadataResolver
    {
        Task<MagnetMetadataResolutionResult> ResolveAsync(MagnetLink resolutionMagnet, CancellationToken cancellationToken);
    }
}
