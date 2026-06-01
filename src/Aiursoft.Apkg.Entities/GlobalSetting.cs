using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>GlobalSetting</b> — Simple key-value store for application-wide runtime configuration.</para>
///
/// <para><b>Design purpose:</b> Stores settings like maintenance mode flags, feature toggles,
/// default values. Not for per-user or per-entity settings — those belong on their respective entities.</para>
///
/// <para><b>Usage:</b> Read by services and controllers at runtime. Updated via the admin
/// settings Web UI. Cached in <c>GlobalSettingsCache</c> for performance.</para>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item>Seeded at startup (<c>Program.SeedAsync</c>) with default values.</item>
///   <item>Read by services/controllers at runtime.</item>
///   <item>Updated via admin settings UI (instant or on next cache refresh).</item>
///   <item>Survives until explicitly deleted.</item>
/// </list>
///
/// <para><b>Relationships:</b> None. Standalone lookup table.</para>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — Key is a single string, Value is atomic in intent.</item>
///   <item><b>2NF ✅</b> — Single-column PK (Key). No partial dependencies possible.</item>
///   <item><b>3NF ✅</b> — No transitive dependencies in a two-column table.</item>
/// </list>
/// <para>This is a deliberate key-value pattern for flexible runtime configuration,
/// not a traditional normalized entity.</para>
/// </summary>
[ExcludeFromCodeCoverage]
public class GlobalSetting
{
    [Key]
    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public required string Key { get; set; }

    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string? Value { get; set; }
}
