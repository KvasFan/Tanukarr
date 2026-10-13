using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Tmdb.List;

public class TmdbListParser : TmdbParserBase<TmdbPagedResource<TmdbMediaResource>>
{
    protected override IEnumerable<ImportListItemInfo> ParseResponse(TmdbPagedResource<TmdbMediaResource> resource)
    {
        // V4 lists mix movies and TV. A bare numeric ID cannot identify its
        // namespace, so only entries explicitly marked TV may become series.
        return resource.Results.Where(item => item?.MediaType == "tv").Select(AsImportable);
    }
}
