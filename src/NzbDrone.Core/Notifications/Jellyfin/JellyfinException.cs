using System;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public class JellyfinException : Exception
    {
        public JellyfinException(string propertyName, string message)
            : base(message)
        {
            PropertyName = propertyName;
        }

        public string PropertyName { get; }
    }
}
