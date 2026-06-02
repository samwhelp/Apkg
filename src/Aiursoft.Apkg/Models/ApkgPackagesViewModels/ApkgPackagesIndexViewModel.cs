using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public enum UploadSyncStatus
{
    /// <summary>Upload is unlisted and hidden from APT clients.</summary>
    Unlisted,
    /// <summary>Unlisted but packages are still live in the primary bucket (pending next sync).</summary>
    Unlisting,
    /// <summary>Published; packages are waiting for the RepositorySyncJob.</summary>
    Syncing,
    /// <summary>Published; packages are in a SecondaryBucket awaiting signing.</summary>
    Signing,
    /// <summary>At least one package is live in a PrimaryBucket.</summary>
    Live,
    /// <summary>All packages were superseded by a newer version.</summary>
    Superseded
}

public class ApkgPackagesIndexViewModel : UiStackLayoutViewModel
{
    public ApkgPackagesIndexViewModel()
    {
        PageTitle = "My Packages (APKG)";
    }

    public required List<ApkgPackageIndexItem> Packages { get; init; }
    public bool IsAdmin { get; init; }
}
