using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>AptCertificate</b> — GPG signing key pair for an APT repository.</para>
///
/// <para><b>Design purpose:</b> Each <see cref="AptRepository"/> can have its own GPG certificate.
/// The <c>RepositorySignJob</c> uses the private key to generate clearsigned InRelease files.
/// APT clients verify these signatures using the public key to establish the trust chain:
/// <c>GPG pubkey → InRelease → Packages.gz → .deb</c> (see docs/design.md §6.2).</para>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Associated with one or more <see cref="AptRepository"/>s via CertificateId.</item>
///   <item>Public key served at <c>/artifacts/certs/{name}</c> for client import.</item>
///   <item>Private key used exclusively by RepositorySignJob to sign InRelease.</item>
///   <item>Trust model: users trust the Apkg server's key, not the upstream key
///   (because Override rules change package content, invalidating upstream signatures).</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item>Seeded at startup (<c>Program.SeedMirrorsAsync</c>) or created manually by admin.</item>
///   <item>Associated with repositories.</item>
///   <item>Used by RepositorySignJob every ~5 minutes.</item>
///   <item>Survives until explicitly deleted (and all referring repos are reassigned).</item>
/// </list>
///
/// <para><b>Relationships:</b> Referenced by <see cref="AptRepository"/> via CertificateId
/// (one-to-many: one cert can sign many repos).</para>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic. Name uses regex validation: ^[a-z0-9]+$.</item>
///   <item><b>2NF ✅</b> — Single-column PK (Id). All certificate fields depend on Id.</item>
///   <item><b>3NF ✅</b> — No transitive dependencies. PublicKey, PrivateKey, FriendlyName,
///   and Fingerprint independently describe the same key.</item>
/// </list>
///
/// <para><b>Security note:</b> The private key is stored in the database. Production
/// deployments should use external HSMs (HashiCorp Vault, Azure Key Vault) — not yet
/// implemented (see docs/design.md §7).</para>
/// </summary>
[ExcludeFromCodeCoverage]
public class AptCertificate
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Unique lowercase identifier used in URLs and API routes (e.g., "anduinos").
    /// </summary>
    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[a-z0-9]+$", ErrorMessage = "Only lowercase letters and numbers are allowed.")]
    public required string Name { get; set; }

    [Required]
    [MaxLength(100)]
    public required string FriendlyName { get; set; }

    [Required]
    public required string PublicKey { get; set; }

    [Required]
    public required string PrivateKey { get; set; }

    [Required]
    [MaxLength(50)]
    public required string Fingerprint { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
