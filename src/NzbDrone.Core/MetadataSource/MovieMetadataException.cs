using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MetadataSource
{
    public class MovieMetadataException : NzbDroneException
    {
        public MovieMetadataException(string message)
            : base(message)
        {
        }
    }
}
