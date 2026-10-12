using System;
using System.Threading;

namespace NzbDrone.Core.AniList
{
    public interface IAniListRequestDelay
    {
        DateTime UtcNow { get; }
        void Wait(TimeSpan delay);
    }

    public class AniListRequestDelay : IAniListRequestDelay
    {
        public DateTime UtcNow => DateTime.UtcNow;

        public void Wait(TimeSpan delay)
        {
            Thread.Sleep(delay);
        }
    }
}
