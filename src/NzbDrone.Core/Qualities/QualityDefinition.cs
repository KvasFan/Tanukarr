using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Qualities
{
    public class QualityDefinition : ModelBase
    {
        public Quality Quality { get; set; }

        public string Title { get; set; }

        public QualitySource? Source { get; set; }
        public int? Resolution { get; set; }
        public string Codec { get; set; } = string.Empty;
        public string TitlePattern { get; set; } = string.Empty;
        public bool IsRetired { get; set; }

        public string GroupName { get; set; }
        public int Weight { get; set; }

        public double? MinSize { get; set; }
        public double? MaxSize { get; set; }
        public double? PreferredSize { get; set; }

        public QualityDefinition()
        {
        }

        public QualityDefinition(Quality quality)
        {
            Quality = quality;
            Title = quality.Name;
        }

        public override string ToString()
        {
            return Quality.Name;
        }
    }
}
