using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>UserApiKey</b> — API key for CLI/CI authentication (e.g., <c>apkg push</c>).</para>
///
/// <para><b>Design purpose:</b> Allows automated tools to authenticate without interactive login.
/// Only the SHA-256 hash of the key is stored — the raw key is shown once at creation
/// and never persisted, so a database leak does not expose usable credentials.</para>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>User creates a key via Web UI (POST). Raw key returned once in response.</item>
///   <item>Client sends raw key in <c>Authorization</c> header of <c>apkg push</c>.</item>
///   <item>Server hashes the incoming key and compares against stored KeyHash.</item>
///   <item>LastUsedAt is updated on each successful authentication.</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item><b>Created</b>: Raw key generated, SHA-256 hashed, hash + prefix stored.</item>
///   <item><b>Active</b>: Used via Authorization header. LastUsedAt tracks usage.</item>
///   <item><b>Expired</b>: ExpiresAt passes → IsExpired = true → authentication rejected.
///   Null ExpiresAt means the key never expires.</item>
///   <item><b>Revoked</b>: Deleted by user or admin from the DB.</item>
/// </list>
///
/// <para><b>Relationships:</b> Belongs to <see cref="User"/> via UserId (many-to-one).</para>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic. KeyHash and KeyPrefix serve different purposes
///   (auth verification vs. display) and are independent.</item>
///   <item><b>2NF ✅</b> — Single-column PK (Id). All attributes depend on the full PK.</item>
///   <item><b>3NF ✅</b> — IsExpired is a computed property (not a stored column),
///   derived from ExpiresAt and the current time. No transitive dependency.</item>
/// </list>
///
/// <para><b>Security invariants:</b></para>
/// <list type="bullet">
///   <item>KeyHash: SHA-256 hex digest (64 chars). Irreversible one-way hash.</item>
///   <item>KeyPrefix: First 8 chars of raw key. Safe to store — too short to reconstruct.</item>
///   <item>KeyHash has a unique index — prevents identical key values from being reused.</item>
/// </list>
/// </summary>
[ExcludeFromCodeCoverage]
[Index(nameof(KeyHash), IsUnique = true)]
[Index(nameof(UserId))]
public class UserApiKey
{
    [Key]
    public int Id { get; set; }

    [Required]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>
    /// A human-readable label for this key, e.g. "CI/CD Pipeline" or "Dev Machine".
    /// </summary>
    [Required]
    [MaxLength(100)]
    public required string Name { get; set; }

    /// <summary>
    /// SHA-256 hex digest of the raw API key. The raw key is shown once at creation and never stored.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public required string KeyHash { get; set; }

    /// <summary>
    /// First 8 characters of the raw key, kept for display purposes (e.g. "ab12cd34...").
    /// Safe to store — too short to reconstruct the full key.
    /// </summary>
    [Required]
    [MaxLength(8)]
    public required string KeyPrefix { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// When this key expires. Null means it never expires.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}
