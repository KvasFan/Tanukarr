using System;
using System.Collections.Generic;
using System.Threading;
using NzbDrone.Core.Indexers.Exceptions;

namespace NzbDrone.Core.MetadataSource.Jikan
{
    public interface IJikanRateLimiter
    {
        void WaitForSlot();
        void BackOff(TimeSpan duration);
    }

    /// <summary>
    /// Keeps requests inside Jikan's published limits of 3 per second and 60 per minute.
    /// <para>
    /// Jikan is a free public mirror with no key, so exceeding the limits earns a block for
    /// everyone behind the same address rather than a per-account penalty. The limiter is
    /// therefore a sliding window over both intervals rather than a simple delay.
    /// </para>
    /// </summary>
    public class JikanRateLimiter : IJikanRateLimiter
    {
        private const int PerSecond = 3;
        private const int PerMinute = 60;

        private readonly Queue<DateTime> _requests = new Queue<DateTime>();
        private readonly object _lock = new object();
        private DateTime _notBefore;

        public void BackOff(TimeSpan duration)
        {
            lock (_lock)
            {
                var until = DateTime.UtcNow.Add(duration);
                if (until > _notBefore)
                {
                    _notBefore = until;
                }
            }
        }

        public void WaitForSlot()
        {
            while (true)
            {
                TimeSpan wait;

                lock (_lock)
                {
                    var now = DateTime.UtcNow;

                    if (_notBefore - now > TimeSpan.FromSeconds(30))
                    {
                        throw new RequestLimitReachedException("Jikan is cooling down after an HTTP 429 response.", _notBefore - now);
                    }

                    while (_requests.Count > 0 && now - _requests.Peek() > TimeSpan.FromMinutes(1))
                    {
                        _requests.Dequeue();
                    }

                    var inLastSecond = 0;

                    foreach (var timestamp in _requests)
                    {
                        if (now - timestamp <= TimeSpan.FromSeconds(1))
                        {
                            inLastSecond++;
                        }
                    }

                    if (_notBefore <= now && inLastSecond < PerSecond && _requests.Count < PerMinute)
                    {
                        _requests.Enqueue(now);

                        return;
                    }

                    // Wait only until the oldest relevant request ages out, so the caller
                    // resumes as soon as a slot frees rather than after a fixed delay.
                    wait = _notBefore > now ? _notBefore - now : inLastSecond >= PerSecond
                        ? TimeSpan.FromSeconds(1) - (now - PeekOldestWithin(now, TimeSpan.FromSeconds(1)))
                        : TimeSpan.FromMinutes(1) - (now - _requests.Peek());
                }

                if (wait > TimeSpan.Zero)
                {
                    Thread.Sleep(wait);
                }
            }
        }

        private DateTime PeekOldestWithin(DateTime now, TimeSpan window)
        {
            foreach (var timestamp in _requests)
            {
                if (now - timestamp <= window)
                {
                    return timestamp;
                }
            }

            return now;
        }
    }
}
