namespace Sonarr.Api.V5.Qualities;

public class QualityCodecFactorResource
{
    public required string Codec { get; set; }

    // Null when quality profiles disagree on this codec family's factor.
    public decimal? Factor { get; set; }
    public bool IsMixed { get; set; }
    public decimal DefaultFactor { get; set; }
}
