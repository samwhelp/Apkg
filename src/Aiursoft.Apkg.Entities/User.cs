using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>User</b> — User account. Extends ASP.NET Core Identity's <c>IdentityUser</c>
/// with a display name and avatar path.</para>
///
/// <para><b>Design purpose:</b> Represents a person or service account. Used for
/// authentication (via cookies or API keys), authorization (via roles/permissions),
/// and ownership tracking (ApkgPackage owner, uploader on revisions and ApkgDebPackages).</para>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Created via registration (Local auth) or first OIDC login.</item>
///   <item>Admin user seeded at startup (<c>Program.SeedAsync</c>) with default credentials.</item>
///   <item>Roles assigned via admin UI; permissions checked by controllers.</item>
///   <item>Ownership of ApkgPackages is enforced at upload time.</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item>Created via registration or OIDC login.</item>
///   <item>Admin user seeded at startup (<c>ProgramExtends.cs:57</c>).</item>
///   <item>Active until deleted by admin. Can hold roles and permissions.</item>
/// </list>
///
/// <para><b>Relationships:</b></para>
/// <list type="bullet">
///   <item>Owns <see cref="UserApiKey"/>s (one-to-many).</item>
///   <item>Owns <see cref="ApkgPackage"/>s via OwnerUserId.</item>
///   <item>Referenced as uploader by <see cref="ApkgRevision"/> and <see cref="ApkgDebPackage"/>.</item>
/// </list>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic. IdentityUser base follows standard normalization.</item>
///   <item><b>2NF ✅</b> — Single-column PK (Id, from IdentityUser). Non-key fields depend on whole PK.</item>
///   <item><b>3NF ✅</b> — No transitive dependencies. DisplayName and AvatarRelativePath
///   depend directly on the user, not on each other.</item>
/// </list>
/// </summary>
[ExcludeFromCodeCoverage]
public class User : IdentityUser
{
    /// <summary>
    /// Default avatar path used when the user hasn't uploaded a custom avatar.
    /// </summary>
    public const string DefaultAvatarPath = "avatar/default-avatar.jpg";

    [MaxLength(30)]
    [MinLength(2)]
    public required string DisplayName { get; set; }

    [MaxLength(150)][MinLength(2)] public string AvatarRelativePath { get; set; } = DefaultAvatarPath;

    /// <summary>
    /// Set once at user creation and never changed.
    /// </summary>
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
