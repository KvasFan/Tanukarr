using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MetadataSource.TvMaze
{
    public class TvMazeException : NzbDroneException
    {
        public TvMazeException(string message)
            : base(message)
        {
        }
    }
}
