using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public class JellyfinItems
    {
        [JsonProperty("Items")]
        public List<JellyfinItem> Items { get; set; }

        [JsonProperty("TotalRecordCount")]
        public int TotalRecordCount { get; set; }
    }

    public class JellyfinItem
    {
        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("Path")]
        public string Path { get; set; }

        [JsonProperty("Type")]
        public string Type { get; set; }
    }
}
