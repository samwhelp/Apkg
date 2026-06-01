using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Aiursoft.AptClient.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>AptPackage</b> — A single package record inside an <see cref="AptBucket"/>.</para>
///
/// <para><b>Design purpose:</b> Inherits from <see cref="DebianPackage"/> which carries all
/// standard APT metadata fields from a Packages.gz file. Extends it with bucket membership
/// (BucketId), component classification (Component), lazy-download tracking (IsVirtual),
/// and upstream URL (RemoteUrl).</para>
///
/// <para><b>Why a structured table, not raw text (see docs/design.md §15.3):</b></para>
/// <list type="bullet">
///   <item>ApkgDebPackage override needs precise (Package, Architecture) matching.</item>
///   <item>IsVirtual lazy-download tracking needs per-package SHA256 state.</item>
///   <item>GarbageCollection needs per-package SHA256 reference counting.</item>
///   <item>None of this is safe or efficient with raw Packages.gz text parsing.</item>
/// </list>
///
/// <para><b>IsVirtual lifecycle (see docs/design.md §3.5):</b></para>
/// <list type="number">
///   <item><b>Virtual</b> (<c>IsVirtual=true</c>): Created during sync. Metadata exists
///   but the .deb binary is not yet in local CAS storage.</item>
///   <item><b>First download</b>: APT client requests the .deb → server fetches from
///   RemoteUrl → writes to CAS at <c>Objects/{sha256[..2]}/{sha256}.deb</c>
///   → sets IsVirtual=false, clears RemoteUrl.</item>
///   <item><b>Subsequent requests</b>: Fast path — served directly from CAS.</item>
///   <item><b>Re-sync</b>: If SHA256 is unchanged and the binary was previously downloaded,
///   IsVirtual stays false (protected by <c>previouslyRealHashes</c> set in RepositorySyncJob).</item>
/// </list>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Created en masse by MirrorSyncJob or RepositorySyncJob during bucket construction.</item>
///   <item>Read by AptMirrorController to generate Packages.gz responses.</item>
///   <item>Read by <c>GetLocalPoolPath</c> to serve .deb binaries from CAS.</item>
///   <item>SHA256 is the primary key for CAS storage and GC reference counting.</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item>Created during sync (IsVirtual=true, RemoteUrl set).</item>
///   <item>Downloaded on first request → IsVirtual=false, RemoteUrl=null.</item>
///   <item>Survives re-syncs if SHA256 unchanged.</item>
///   <item>Deleted when parent bucket is GC'd.</item>
/// </list>
///
/// <para><b>Relationships:</b> Belongs to <see cref="AptBucket"/> via BucketId (many-to-one).</para>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic. <c>Extras</c> (inherited from DebianPackage)
///   is stored as a serialized JSON string with a custom <c>ValueComparer</c> for EF Core
///   change detection — a deliberate denormalization for rarely-used APT fields.</item>
///   <item><b>2NF ✅</b> — PK is Id. All fields depend on the whole PK.</item>
///   <item><b>3NF ✅</b> — Component is stored here (not derived from Bucket) because a
///   single bucket can contain packages from multiple components. IsVirtual depends on
///   CAS file existence (external state), not on other columns.</item>
/// </list>
///
/// <para><b>Indexes:</b></para>
/// <list type="bullet">
///   <item><c>(BucketId)</c> — fast lookup of all packages in a bucket.</item>
///   <item><c>(Package, Version, Architecture, Component)</c> — slot dedup/lookup.</item>
///   <item><c>(Filename)</c> — pool path resolution.</item>
///   <item><c>(SHA256)</c> — CAS reference counting for GC.</item>
/// </list>
/// </summary>
[ExcludeFromCodeCoverage]
[Index(nameof(BucketId))]
[Index(nameof(Package), nameof(Version), nameof(Architecture), nameof(Component))]
[Index(nameof(Filename))]
[Index(nameof(SHA256))]
public class AptPackage : DebianPackage
{
    [Key]
    public int Id { get; set; }

    public int BucketId { get; set; }

    [ForeignKey(nameof(BucketId))]
    public AptBucket? Bucket { get; set; }

    /// <summary>
    /// APT component this package belongs to (e.g., "main", "restricted").
    /// Stored here (not derived from Bucket) because a bucket contains
    /// packages from all components.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public required string Component { get; set; }

    /// <summary>
    /// True means the .deb binary is not yet in local CAS storage (lazy download).
    /// False means the binary is available at <c>Objects/{sha256[..2]}/{sha256}.deb</c>.
    /// </summary>
    public bool IsVirtual { get; set; } = true;

    /// <summary>
    /// Upstream URL to fetch the .deb binary when IsVirtual is true.
    /// Cleared (set to null) after successful download into CAS.
    /// </summary>
    public string? RemoteUrl { get; set; }
}
