using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.Models.LocalPackagesViewModels;

public enum LocalPackageStatus
{
    /// <summary>
    /// Package is disabled by user.
    /// </summary>
    Disabled,

    /// <summary>
    /// Package is enabled but not yet picked up by RepositorySyncJob.
    /// It is waiting for the next sync cycle (up to 20 minutes).
    /// </summary>
    PendingSync,

    /// <summary>
    /// Package is picked up by RepositorySyncJob and included in a SecondaryBucket.
    /// It is now waiting for RepositorySignJob to sign and promote it to Primary (up to 5 minutes).
    /// </summary>
    StagedForSigning,

    /// <summary>
    /// Package is in the PrimaryBucket and available for APT clients.
    /// </summary>
    Live,

    /// <summary>
    /// A different (newer) version of this package is live in the PrimaryBucket.
    /// This specific version will not be picked up by future syncs.
    /// </summary>
    Superseded
}

public class PackageStatusInfo
{
    public required LocalPackage Package { get; set; }
    public LocalPackageStatus Status { get; set; }
    public string? StatusMessage { get; set; }
    public int? LivePackageId { get; set; }
}
