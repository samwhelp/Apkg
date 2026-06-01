using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>AptRepository</b> — An APT repository served to end-user clients. The data egress point.</para>
///
/// <para><b>Design purpose:</b> This is what APT clients connect to via <c>apt update</c>.
/// Each repository has a (Distro, Suite, Components, Architecture) configuration that
/// determines which packages it serves and at what URL paths.</para>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>APT clients access it at <c>/artifacts/{distro}/dists/{suite}/...</c>.</item>
///   <item>Can be linked to an <see cref="AptMirror"/> (to pull upstream packages)
///   or standalone (ApkgDebPackages only, no upstream).</item>
///   <item>Can be linked to an <see cref="AptCertificate"/> for GPG signing.</item>
/// </list>
///
/// <para><b>Dual-bucket pattern:</b></para>
/// <list type="bullet">
///   <item><b>PrimaryBucketId</b> — The currently signed, live snapshot that APT
///   clients read. Only RepositorySignJob writes this. Always has InReleaseContent.</item>
///   <item><b>SecondaryBucketId</b> — The newly built but unsigned snapshot. Set by
///   RepositorySyncJob, promoted to Primary by RepositorySignJob after GPG signing.</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item><b>Created</b> by admin (Web UI), optionally linked to Mirror and Certificate.</item>
///   <item><b>RepositorySyncJob</b> (every 4h): copies Mirror.Primary → applies
///   ApkgDebPackage overrides (matching Package + Architecture) → builds Release/Packages.gz
///   → sets SecondaryBucketId.</item>
///   <item><b>RepositorySignJob</b> (every 5min): GPG-signs SecondaryBucket's Release
///   → atomically promotes Secondary to Primary.</item>
///   <item><b>APT clients</b> always read Primary only. Secondary is invisible to them.</item>
/// </list>
///
/// <para><b>Relationships:</b></para>
/// <list type="bullet">
///   <item>Has <b>PrimaryBucket</b> and <b>SecondaryBucket</b> (→ <see cref="AptBucket"/>).</item>
///   <item>Has <b>Certificate</b> (→ <see cref="AptCertificate"/>). Null = passthrough signing
///   (<c>EnableGpgSign=false</c>, clients must use <c>[trusted=yes]</c>).</item>
///   <item>Has <b>Mirror</b> (→ <see cref="AptMirror"/>). Null = standalone repo.</item>
///   <item>Has <b>ApkgDebPackages</b> (one-to-many): user-uploaded override .debs.</item>
/// </list>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic.</item>
///   <item><b>2NF ✅</b> — PK is Id. All fields depend on the whole PK.</item>
///   <item><b>3NF ✅</b> — No transitive dependencies. AllowAnyoneToUpload,
///   EnableGpgSign, Components, Architecture are independent attributes.</item>
/// </list>
///
/// <para><b>Key invariants:</b></para>
/// <list type="bullet">
///   <item>Only RepositorySignJob writes PrimaryBucketId.</item>
///   <item>SecondaryBucket is invisible to APT clients regardless of state.</item>
///   <item>GC protects both Primary and Secondary buckets.</item>
///   <item>Repo can operate standalone (MirrorId=null) with only ApkgDebPackages.</item>
/// </list>
/// </summary>
[ExcludeFromCodeCoverage]
public class AptRepository
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Operating system family (e.g., "ubuntu", "anduinos"). Determines URL routing
    /// at <c>/artifacts/{distro}/</c>.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Distro { get; set; } = "ubuntu";

    /// <summary>
    /// Human-readable name used in URL segments (e.g., "ubuntu", "anduinos-official").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public required string Name { get; set; }

    /// <summary>
    /// APT suite/release codename (e.g., "noble", "noble-updates", "questing").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public required string Suite { get; set; }

    /// <summary>
    /// Space-separated component list (e.g., "main restricted"). Default: "main".
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Components { get; set; } = "main";

    /// <summary>
    /// Space-separated architecture list (e.g., "amd64 arm64"). Default: "amd64".
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Architecture { get; set; } = "amd64";

    public int? CertificateId { get; set; }

    [ForeignKey(nameof(CertificateId))]
    public AptCertificate? Certificate { get; set; }

    /// <summary>
    /// If false, GPG signing is skipped (passthrough signing). Clients must use
    /// <c>[trusted=yes]</c> in sources.list. See docs/design.md §7.
    /// </summary>
    public bool EnableGpgSign { get; set; } = true;

    public int? MirrorId { get; set; }

    [ForeignKey(nameof(MirrorId))]
    public AptMirror? Mirror { get; set; }

    /// <summary>
    /// FK to the currently signed, live bucket snapshot served to APT clients.
    /// Only <c>RepositorySignJob</c> writes this.
    /// </summary>
    public int? PrimaryBucketId { get; set; }

    [ForeignKey(nameof(PrimaryBucketId))]
    public AptBucket? PrimaryBucket { get; set; }

    /// <summary>
    /// FK to the bucket that has been built and is awaiting GPG signing before
    /// being promoted to <see cref="PrimaryBucketId"/>.
    /// Set by <c>RepositorySyncJob</c>; cleared and promoted by <c>RepositorySignJob</c>.
    /// </summary>
    public int? SecondaryBucketId { get; set; }

    [ForeignKey(nameof(SecondaryBucketId))]
    public AptBucket? SecondaryBucket { get; set; }

    /// <summary>
    /// If true, any authenticated user can upload to this repository.
    /// If false, only users with the <c>CanUploadToRestrictedRepositories</c> permission.
    /// </summary>
    public bool AllowAnyoneToUpload { get; set; }
}
