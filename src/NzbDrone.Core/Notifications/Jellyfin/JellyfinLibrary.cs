using Newtonsoft.Json;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public class JellyfinLibrary
    {
        [JsonProperty("ItemId")]
        public string ItemId { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; }

        [JsonProperty("CollectionType")]
        public string CollectionType { get; set; }

        [JsonProperty("Locations")]
        public string[] Locations { get; set; }
    }
}
