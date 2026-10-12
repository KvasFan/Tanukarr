using NzbDrone.Core.Annotations;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public enum JellyfinRefreshMode
    {
        [FieldOption(Label = "NotificationsJellyfinSettingsEachImport")]
        EachImport = 0,

        [FieldOption(Label = "NotificationsJellyfinSettingsInterval")]
        Interval = 1,

        [FieldOption(Label = "NotificationsJellyfinSettingsEachImportAndInterval")]
        EachImportAndInterval = 2
    }
}
