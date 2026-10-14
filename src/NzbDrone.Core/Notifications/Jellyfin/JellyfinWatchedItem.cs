using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public class JellyfinUser
    {
        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; }
    }

    public class JellyfinWatchedItems
    {
        [JsonProperty("Items")]
        public List<JellyfinWatchedItem> Items { get; set; }

        [JsonProperty("TotalRecordCount")]
        public int? TotalRecordCount { get; set; }
    }

    public class JellyfinWatchedItem
    {
        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("Path")]
        public string Path { get; set; }

        [JsonProperty("Type")]
        public string Type { get; set; }

        [JsonProperty("UserData")]
        public JellyfinWatchedUserData UserData { get; set; }
    }

    public class JellyfinWatchedUserData
    {
        [JsonProperty("Played")]
        public bool? Played { get; set; }

        [JsonProperty("LastPlayedDate")]
        public DateTime? LastPlayedDate { get; set; }
    }
}
