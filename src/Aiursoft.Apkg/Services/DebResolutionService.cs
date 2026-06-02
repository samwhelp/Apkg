using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.Services;

/// <summary>
/// Resolves which debs "win" each slot when multiple uploads have produced
/// different versions for the same (Package, Architecture, Suite, Repository).
/// The winner is always the highest version per slot.
/// </summary>
public class DebResolutionService(AptVersionComparisonService versionComparer)
{
    /// <summary>
    /// Returns only the latest-version deb per (Package, Architecture, Suite, RepositoryId) slot.
    /// Disabled debs are excluded.
    /// </summary>
    public List<ApkgDebPackage> ResolveWinningDebs(IEnumerable<ApkgDebPackage> debs)
    {
        var enabled = debs.Where(d => d.IsEnabled).ToList();
        if (enabled.Count == 0)
            return [];

        var comparer = Comparer<string>.Create(versionComparer.Compare);

        return enabled
            .GroupBy(d => (
                d.Package,
                d.Architecture,
                d.Repository?.Suite ?? "",
                d.RepositoryId))
            .Select(g => g.OrderByDescending(d => d.Version, comparer).First())
            .ToList();
    }
}
