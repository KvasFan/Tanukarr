namespace Sonarr.Api.V3.Qualities
{
    public class QualityCodecFactorResource
    {
        public string Codec { get; set; }

        // Null when quality profiles disagree on this codec family's factor.
        public decimal? Factor { get; set; }
        public bool IsMixed { get; set; }
        public decimal DefaultFactor { get; set; }
    }
}
