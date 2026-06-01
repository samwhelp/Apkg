using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>AptBucket</b> — Immutable versioned snapshot of package metadata at a point in time.</para>
///
/// <para><b>Design purpose:</b> A bucket holds the complete Release/InRelease content plus the
/// full list of <see cref="AptPackage"/> records for one snapshot. The dual-bucket pattern
/// (Primary + Secondary) prevents APT clients from ever seeing half-written state — the server
/// builds into a Secondary, then atomically swaps it to Primary with a single FK update.</para>
///
/// <para><b>Why not update in-place (see docs/design.md §15.4):</b> Directly modifying a Primary
/// bucket's content would expose APT clients to half-written Packages.gz or unsigned InRelease
/// during <c>apt update</c>. Dual-bucket + atomic swap guarantees consistent signed snapshots.</para>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Created by MirrorSyncJob or RepositorySyncJob.</item>
///   <item>Populated with thousands of AptPackage rows.</item>
///   <item>ReleaseContent set when package discovery is complete.</item>
///   <item>InReleaseContent + SignedAt set by RepositorySignJob after GPG signing.</item>
///   <item>Promoted to Primary via FK pointer swap in Mirror/AptRepository.</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item><b>Created</b> by sync job (ReleaseContent = null during construction).</item>
///   <item><b>Populated</b> with AptPackages (one bucket → thousands of packages).</item>
///   <item><b>Release built</b>: ReleaseContent is set.</item>
///   <item><b>Signed</b> (repos only): InReleaseContent set + SignedAt timestamp.</item>
///   <item><b>Promoted</b> to Primary via FK pointer swap.</item>
///   <item><b>Orphaned</b> (no longer referenced by any mirror/repo Primary or Secondary)
///   → deleted by GarbageCollectionJob.</item>
/// </list>
///
/// <para><b>Relationships:</b></para>
/// <list type="bullet">
///   <item>Contains <see cref="AptPackage"/>s (one-to-many, via BucketId FK).</item>
///   <item>Referenced by <see cref="AptMirror"/> (Primary/Secondary) and
///   <see cref="AptRepository"/> (Primary/Secondary). The bucket has no FK back to
///   its owner — ownership is determined by who points to it. GC follows these
///   references to build the active set (see docs/design.md §3.3 invariant 3).</item>
/// </list>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic. InReleaseContent is a single string
///   (the full clearsigned message).</item>
///   <item><b>2NF ✅</b> — PK is Id. CreatedAt, ReleaseContent, InReleaseContent,
///   SignedAt all depend on the full PK.</item>
///   <item><b>3NF ✅</b> — No transitive dependencies. ReleaseContent and InReleaseContent
///   are independent representations (unsigned vs signed) of the same bucket metadata.
///   SignedAt describes when signing happened.</item>
/// </list>
///
/// <para><b>Guard:</b> RepositorySignJob refuses to promote a bucket whose ReleaseContent
/// is null (still building), preventing half-constructed snapshots from going live.</para>
/// </summary>
[ExcludeFromCodeCoverage]
public class AptBucket
{
    [Key]
    public int Id { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// GPG clearsigned InRelease content. Null until signed by RepositorySignJob.
    /// </summary>
    public string? InReleaseContent { get; set; }

    /// <summary>
    /// Unsigned Release file content. Set after sync completes package discovery.
    /// Contains header fields (Origin/Label/Suite/Codename/Date/Architectures/Components)
    /// + SHA256 checksum list of all Packages files in this bucket.
    /// </summary>
    public string? ReleaseContent { get; set; }

    /// <summary>
    /// Set by <c>RepositorySignJob</c> when the bucket's Release file is GPG-signed.
    /// Null means the bucket has not been signed yet.
    /// </summary>
    public DateTime? SignedAt { get; set; }

    public List<AptPackage> Packages { get; set; } = [];
}
