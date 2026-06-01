using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.ApkgDebPackagesViewModels;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class ApkgPackagesDetailsViewModel : UiStackLayoutViewModel
{
    public ApkgPackagesDetailsViewModel()
    {
        PageTitle = "APKG Package Details";
    }

    public required ApkgPackage Package { get; init; }
    public required List<PackageStatusInfo> EffectivePackages { get; init; }
    public required List<PackageStatusInfo> AllPackageStatuses { get; init; }
    public required List<UploadHistoryItem> UploadHistory { get; init; }
    public required string ActiveTab { get; init; } = "overview";
    public string VersionsFilter { get; init; } = "latest";
    public bool IsAdmin { get; init; }
    public bool IsOwner { get; init; }
}

public class UploadHistoryItem
{
    public required ApkgRevision Revision { get; init; }
    public required List<PackageStatusInfo> DebStatuses { get; init; }
}
