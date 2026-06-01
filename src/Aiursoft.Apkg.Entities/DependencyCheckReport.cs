using System.ComponentModel.DataAnnotations;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>DependencyCheckReport</b> — Result of a repository dependency completeness check.</para>
///
/// <para><b>Design purpose:</b> Verifies that every package's Depends (and other dependency
/// fields) can be satisfied by packages available in the same repository's current bucket.
/// This is a manually triggered audit, not a continuous guard — it helps admins catch
/// broken dependencies before users do.</para>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Manually triggered via the /Jobs admin page → RepositoryDependencyCheckJob.</item>
///   <item>Job iterates all AptPackages in the repo's Primary Bucket, resolving each
///   Depends/Recommends/Provides against the same bucket.</item>
///   <item>Results stored as a JSON array in DetailsJson for flexible consumption.</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item><b>Created</b>: Status = "Running". Job starts processing.</item>
///   <item><b>Completed</b>: Status = "Completed". TotalPackages, ProblematicPackages,
///   and DetailsJson populated.</item>
///   <item><b>Failed</b>: Status = "Failed". ErrorMessage set.</item>
///   <item><b>Expired</b>: ExpireAt = CreatedAt + 72h. Old reports are not
///   automatically deleted, but UI may filter them out.</item>
/// </list>
///
/// <para><b>Relationships:</b> Belongs to <see cref="AptRepository"/> via RepositoryId
/// (many-to-one).</para>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic. DetailsJson stores a JSON array as a
///   single string with MaxLength = int.MaxValue — a controlled denormalization for
///   semi-structured data whose schema may evolve.</item>
///   <item><b>2NF ✅</b> — PK is Id. All fields depend on the whole PK.</item>
///   <item><b>3NF ✅</b> — ProblematicPackages is a summary count derived from
///   DetailsJson at write time — a deliberate caching optimization for fast UI
///   filtering without parsing the full JSON. Not a transitive dependency issue.</item>
/// </list>
///
/// <para><b>DetailsJson format:</b></para>
/// <code>
/// [{
///   "Package": "foo",
///   "Version": "1.0",
///   "Architecture": "amd64",
///   "MissingDeps": [{
///     "Name": "bar",
///     "Required": "&gt;= 1.3.0",
///     "Available": "1.2.0"
///   }]
/// }]
/// </code>
/// </summary>
public class DependencyCheckReport
{
    [Key]
    public int Id { get; set; }

    public int RepositoryId { get; set; }
    public AptRepository Repository { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Reports expire 72 hours after creation.
    /// </summary>
    public DateTime ExpireAt { get; set; } = DateTime.UtcNow.AddHours(72);

    /// <summary>
    /// Total number of packages checked (including virtual packages).
    /// </summary>
    public int TotalPackages { get; set; }

    /// <summary>
    /// Number of packages with unmet dependencies.
    /// </summary>
    public int ProblematicPackages { get; set; }

    /// <summary>
    /// JSON array of detailed dependency issues.
    /// </summary>
    [MaxLength(int.MaxValue)]
    public string? DetailsJson { get; set; }

    /// <summary>
    /// Check execution status: "Running", "Completed", or "Failed".
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Running";

    /// <summary>
    /// Error message if Status is "Failed".
    /// </summary>
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
}
