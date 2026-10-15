using System;
using System.Threading;

namespace NzbDrone.Core.MetadataSource.TvMaze
{
    public interface ITvMazeRateLimiter
    {
        bool TryWaitForSlot(TimeSpan maxWait);
        void BackOff(TimeSpan duration);
    }

    public class TvMazeRateLimiter : ITvMazeRateLimiter
    {
        private readonly object _lock = new object();
        private DateTime _notBefore;

        public bool TryWaitForSlot(TimeSpan maxWait)
        {
            if (maxWait < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWait));
            }

            var deadline = DateTime.UtcNow.Add(maxWait);
            while (true)
            {
                TimeSpan wait;
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    if (_notBefore <= now)
                    {
                        // Below TVmaze's public minimum of 20 calls per 10 seconds.
                        _notBefore = now.AddMilliseconds(600);
                        return true;
                    }

                    wait = _notBefore - now;
                    if (_notBefore > deadline)
                    {
                        return false;
                    }
                }

                Thread.Sleep(wait);
            }
        }

        public void BackOff(TimeSpan duration)
        {
            lock (_lock)
            {
                var bounded = duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(5);
                var until = DateTime.UtcNow.Add(bounded > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : bounded);
                if (until > _notBefore)
                {
                    _notBefore = until;
                }
            }
        }
    }
}
