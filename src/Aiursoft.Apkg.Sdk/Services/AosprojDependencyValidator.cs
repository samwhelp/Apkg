using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>
/// Validates that declared package dependencies exist in the apt server configured
/// via <see cref="AosprojProject.DependencyCheckUrl"/>.
///
/// Runs asynchronously (network I/O) and is intentionally separate from the
/// synchronous <see cref="AosprojLinter"/> so the static linter has no network dependency.
/// All issues are Warnings — a network failure must never block a build.
/// </summary>
public class AosprojDependencyValidator
{
    private readonly AptPackageIndexClient _indexClient;

    public record LintIssue(Severity Level, string Message);
    public enum Severity { Warning, Error }

    public AosprojDependencyValidator(AptPackageIndexClient indexClient)
    {
        _indexClient = indexClient;
    }

    /// <summary>
    /// Validates all Dependency and Recommend declarations for every target suite.
    /// Returns Warnings for packages not found in the apt index.
    /// Returns a Warning (never an Error) when the apt server is unreachable.
    /// Returns an empty list when <see cref="AosprojProject.DependencyCheckUrl"/> is empty.
    /// </summary>
    public async Task<IReadOnlyList<LintIssue>> ValidateAsync(
        AosprojProject project,
        CancellationToken ct = default)
    {
        var issues = new List<LintIssue>();

        if (string.IsNullOrWhiteSpace(project.DependencyCheckUrl))
            return issues;

        var suiteMap = project.GetDependencyCheckSuiteMap();

        // Determine target arch for Packages.gz lookup (default amd64)
        var arch = project.ArchList.FirstOrDefault(a =>
            !a.Equals("all", StringComparison.OrdinalIgnoreCase)) ?? "amd64";

        foreach (var suite in project.SuiteList)
        {
            var checkSuite = suiteMap.TryGetValue(suite, out var mapped) ? mapped : suite;

            IReadOnlySet<string> available;
            try
            {
                available = await _indexClient.GetAvailablePackagesAsync(
                    project.DependencyCheckUrl, checkSuite, arch, ct);
            }
            catch (Exception ex)
            {
                issues.Add(new LintIssue(Severity.Warning,
                    $"Could not fetch package index for suite '{checkSuite}' " +
                    $"from '{project.DependencyCheckUrl}': {ex.Message}"));
                continue;
            }

            // Validate Depends
            foreach (var dep in project.Dependencies)
                CheckEntry(issues, available, dep.Value, suite, "Dependency");

            // Validate Recommends
            foreach (var rec in project.Recommends)
                CheckEntry(issues, available, rec.Value, suite, "Recommend");
        }

        return issues;
    }

    private static void CheckEntry(
        List<LintIssue> issues,
        IReadOnlySet<string> available,
        string depValue,
        string suite,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(depValue))
            return;

        // Handle "pkg1 | pkg2 | pkg3" — pass if ANY alternative exists
        var alternatives = depValue
            .Split('|')
            .Select(a => StripVersionConstraint(a.Trim()))
            .Where(a => !string.IsNullOrEmpty(a))
            .ToList();

        if (alternatives.Any(available.Contains))
            return;

        var pkgList = string.Join(" | ", alternatives);
        issues.Add(new LintIssue(Severity.Warning,
            $"{kind} '{pkgList}' not found in apt index for suite '{suite}'. " +
            "Verify the package name is correct for this suite."));
    }

    /// <summary>Strips "pkg (>= 1.0)" → "pkg".</summary>
    private static string StripVersionConstraint(string dep)
    {
        var idx = dep.IndexOf('(');
        return (idx > 0 ? dep[..idx] : dep).Trim();
    }
}
