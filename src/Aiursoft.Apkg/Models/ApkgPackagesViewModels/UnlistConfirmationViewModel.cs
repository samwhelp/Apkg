using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class UnlistConfirmationViewModel : UiStackLayoutViewModel
{
    public UnlistConfirmationViewModel()
    {
        PageTitle = "Confirm Unlist";
    }

    public required ApkgRevision Revision { get; init; }
    public required ApkgPackage Package { get; init; }
    public required List<UnlistImpactItem> Impacts { get; init; }
}

public class UnlistImpactItem
{
    /// <summary>The deb being unlisted.</summary>
    public required ApkgDebPackage ReplacedDeb { get; init; }

    /// <summary>Human-readable repository description.</summary>
    public required string RepositoryDescription { get; init; }

    /// <summary>The version that will replace this one, or null if no replacement exists (package disappears entirely from this repo).</summary>
    public string? ReplacementVersion { get; init; }

    /// <summary>True when the replacement has a LOWER version — this is a downgrade, potentially dangerous.</summary>
    public bool IsDowngrade { get; init; }
}
