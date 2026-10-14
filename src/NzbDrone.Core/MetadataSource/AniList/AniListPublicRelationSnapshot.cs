using System.Collections.Generic;
using System.Text;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.MetadataSource.Grouping;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public sealed class AniListPublicRelationSnapshot
    {
        private readonly Dictionary<int, AnimeRelationNode> _nodes;

        private AniListPublicRelationSnapshot(Dictionary<int, AnimeRelationNode> nodes)
        {
            _nodes = nodes;
            PayloadBytes = Encoding.UTF8.GetByteCount(Json.ToJson(nodes));
        }

        public long PayloadBytes { get; }

        public static AniListPublicRelationSnapshot From(IEnumerable<MediaInfo> media)
        {
            var nodes = new Dictionary<int, AnimeRelationNode>();
            foreach (var item in media)
            {
                var resource = new AniListMediaResource
                {
                    Id = item.Id,
                    Type = item.Type,
                    Format = item.Format,
                    Title = new AniListTitleResource
                    {
                        UserPreferred = item.Title?.UserPreferred,
                        Romaji = item.Title?.UserRomaji,
                        English = item.Title?.English,
                        Native = item.Title?.Native
                    },
                    Relations = item.Relations
                };
                var node = AniListProxy.MapRelations(resource, item.Id);
                if (node?.Title.IsNotNullOrWhiteSpace() == true)
                {
                    nodes.Add(item.Id, node);
                }
            }

            return new AniListPublicRelationSnapshot(nodes);
        }

        public bool TryGet(int id, out AnimeRelationNode node)
        {
            if (_nodes.TryGetValue(id, out var saved))
            {
                // The scope retains public graph facts only, never list names,
                // memberships, progress, owner information or mutable caller nodes.
                node = Json.Deserialize<AnimeRelationNode>(Json.ToJson(saved));
                return true;
            }

            node = null;
            return false;
        }
    }
}
