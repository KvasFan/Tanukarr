using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.LibraryTools
{
    public static class CompanionAddSeriesPolicy
    {
        public static void Validate(Series series, IEnumerable<RootFolder> rootFolders)
        {
            var errors = new List<ValidationFailure>();

            if (series.Path.IsNotNullOrWhiteSpace())
            {
                errors.Add(new ValidationFailure(nameof(Series.Path), "Companion keys must use the generated series folder within a registered root folder."));
            }

            var rootFolder = series.RootFolderPath.IsPathValid(PathValidationType.CurrentOs)
                ? rootFolders.FirstOrDefault(root => root.Path.IsPathValid(PathValidationType.CurrentOs) && root.Path.PathEquals(series.RootFolderPath))
                : null;

            if (rootFolder == null)
            {
                errors.Add(new ValidationFailure(nameof(Series.RootFolderPath), "Companion keys must select a registered root folder."));
            }

            if (series.AddOptions?.SearchForMissingEpisodes == true)
            {
                errors.Add(new ValidationFailure($"AddOptions.{nameof(AddSeriesOptions.SearchForMissingEpisodes)}", "Companion keys cannot trigger searches when adding series."));
            }

            if (series.AddOptions?.SearchForCutoffUnmetEpisodes == true)
            {
                errors.Add(new ValidationFailure($"AddOptions.{nameof(AddSeriesOptions.SearchForCutoffUnmetEpisodes)}", "Companion keys cannot trigger searches when adding series."));
            }

            if (errors.Count > 0)
            {
                throw new ValidationException(errors);
            }

            // Use the stored root rather than a caller-supplied equivalent path.
            series.RootFolderPath = rootFolder.Path;
        }
    }
}
