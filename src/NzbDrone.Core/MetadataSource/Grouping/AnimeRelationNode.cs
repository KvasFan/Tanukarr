using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.Grouping
{
    public interface IAnimeRelationProvider
    {
        MetadataSourceType Source { get; }
        AnimeRelationNode GetAnimeRelations(string foreignId);
    }

    public class AnimeRelationNode
    {
        public string ForeignId { get; set; }
        public string Title { get; set; }
        public string Format { get; set; }
        public List<string> PrequelIds { get; set; } = new List<string>();
        public List<string> SequelIds { get; set; } = new List<string>();
        public Dictionary<string, string> RelationFormats { get; set; } = new Dictionary<string, string>();
    }
}
