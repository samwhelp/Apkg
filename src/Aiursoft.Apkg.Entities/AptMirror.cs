using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>AptMirror</b> — Upstream APT repository mirror. The data ingress point of the system.</para>
///
/// <para><b>Design purpose:</b> Represents an upstream APT repository (e.g., Ubuntu archive at
/// <c>http://archive.ubuntu.com/ubuntu</c>) from which Apkg pulls package metadata.
/// The <c>MirrorSyncJob</c> periodically fetches Packages.gz from the upstream and writes
/// structured records into <see cref="AptBucket"/>s containing <see cref="AptPackage"/>s.</para>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Admin creates a mirror via Web UI, specifying upstream URL, distro, suite,
///   components, and architectures.</item>
///   <item>MirrorSyncJob runs (every 6h): creates a new SecondaryBucket, pulls all
///   Packages.gz from upstream, populates AptPackages.</item>
///   <item>On completion, SecondaryBucket is promoted to Primary. The old Primary
///   moves to Secondary to protect in-flight RepositorySyncJob reads.</item>
/// </list>
///
/// <para><b>Dual-bucket pattern (see docs/design.md §15.4):</b></para>
/// <list type="bullet">
///   <item><b>PrimaryBucketId</b> — The currently live snapshot served to
///   RepositorySyncJob consumers (and visible in the Mirror UI).</item>
///   <item><b>SecondaryBucketId</b> — The snapshot being built by MirrorSyncJob.
///   Protected from GarbageCollectionJob until the job completes.</item>
/// </list>
/// <para>This avoids in-place mutation: APT clients and downstream jobs always see
/// a consistent snapshot; any step can crash and retry without corruption.</para>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item><b>Created</b> by admin (Web UI).</item>
///   <item><b>MirrorSyncJob</b> runs, creates new SecondaryBucket, populates packages.</item>
///   <item><b>Primary swapped</b>: Secondary → Primary, old Primary → Secondary (protects
///   in-flight RepositorySyncJob reads from truncated cursors).</item>
///   <item><b>Orphaned buckets</b> (no longer Primary or Secondary) → deleted by
///   GarbageCollectionJob.</item>
/// </list>
///
/// <para><b>Relationships:</b></para>
/// <list type="bullet">
///   <item>Has <b>PrimaryBucket</b> and <b>SecondaryBucket</b> (both → <see cref="AptBucket"/>).
///   These are nullable FK pointers — a new mirror starts with both null.</item>
///   <item>Referenced by <see cref="AptRepository"/> via MirrorId (one mirror feeds many repos).</item>
/// </list>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic.</item>
///   <item><b>2NF ✅</b> — PK is Id. All fields (Distro, BaseUrl, Suite, Components,
///   Architecture) depend on the full PK.</item>
///   <item><b>3NF ✅</b> — LastPullTime, LastPullSuccess, LastPullResult, LastVerifyLog,
///   LastContentHash are monitoring fields about the mirror's sync state — they describe
///   the mirror entity, not each other. No transitive dependency.</item>
/// </list>
///
/// <para><b>Key invariants (see docs/design.md §3.3):</b></para>
/// <list type="bullet">
///   <item>Only MirrorSyncJob writes PrimaryBucketId.</item>
///   <item>SecondaryBucket is the staging area, never visible to end-users.</item>
///   <item>Both Primary and Secondary are in GC's active set (protected from deletion).</item>
///   <item>Old Primary stays in Secondary after rotation — protects RepositorySyncJob
///   reads from being truncated by GC.</item>
/// </list>
/// </summary>
[ExcludeFromCodeCoverage]
public class AptMirror
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Operating system family (e.g., "ubuntu", "debian", "anduinos").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Distro { get; set; } = "ubuntu";

    /// <summary>
    /// Upstream APT repository base URL (e.g., "http://archive.ubuntu.com/ubuntu").
    /// </summary>
    [Required]
    [MaxLength(255)]
    public required string BaseUrl { get; set; }

    /// <summary>
    /// APT suite/release codename (e.g., "noble", "jammy").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public required string Suite { get; set; }

    /// <summary>
    /// Space-separated component list (e.g., "main restricted universe multiverse").
    /// </summary>
    [Required]
    [MaxLength(255)]
    public required string Components { get; set; }

    /// <summary>
    /// Space-separated architecture list (e.g., "amd64 arm64 i386").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public required string Architecture { get; set; }

    /// <summary>
    /// GPG key fingerprint used to verify the upstream Release file signature.
    /// </summary>
    public string? SignedBy { get; set; }

    /// <summary>
    /// If true, skip upstream Release file GPG signature verification.
    /// </summary>
    public bool AllowInsecure { get; set; }

    /// <summary>
    /// FK to the currently live, serving bucket snapshot.
    /// </summary>
    public int? PrimaryBucketId { get; set; }

    [ForeignKey(nameof(PrimaryBucketId))]
    public AptBucket? PrimaryBucket { get; set; }

    /// <summary>
    /// FK to the bucket being built by MirrorSyncJob (staging area).
    /// Protected from GC even when incomplete.
    /// </summary>
    public int? SecondaryBucketId { get; set; }

    [ForeignKey(nameof(SecondaryBucketId))]
    public AptBucket? SecondaryBucket { get; set; }

    // ── Monitoring fields (set by MirrorSyncJob after each run) ──

    public DateTime? LastPullTime { get; set; }

    public bool? LastPullSuccess { get; set; }

    public string? LastPullResult { get; set; }

    public string? LastPullErrorStack { get; set; }

    /// <summary>
    /// GPG verification output from the last successful upstream Release check.
    /// </summary>
    public string? LastVerifyLog { get; set; }

    /// <summary>
    /// Hash of the last pulled content for change detection (skip re-sync if unchanged).
    /// </summary>
    public string? LastContentHash { get; set; }

    public DateTime? LastPrimaryReplacedAt { get; set; }
}
