using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;

namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>
/// Downloads and caches APT Packages.gz indexes from an apt server,
/// returning the set of available package names (including virtual packages via Provides:).
/// </summary>
public class AptPackageIndexClient
{
    private readonly HttpClient _http;

    // Cache key: "{aptUrl}|{suite}|{component}|{binArch}"
    private readonly ConcurrentDictionary<string, Task<IReadOnlySet<string>>> _cache = new();

    public AptPackageIndexClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Returns all package names available in the given suite on the apt server.
    /// Checks main + universe components, binary-all + binary-{arch}.
    /// Results are cached for the lifetime of this service instance.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetAvailablePackagesAsync(
        string aptServerUrl,
        string suite,
        string arch = "amd64",
        CancellationToken ct = default)
    {
        var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in new[] { "main", "universe" })
        foreach (var binArch in new[] { "binary-all", $"binary-{arch}" })
        {
            var key = $"{aptServerUrl}|{suite}|{component}|{binArch}";
            var packages = await _cache.GetOrAdd(key,
                _ => FetchPackagesAsync(aptServerUrl, suite, component, binArch, ct));
            foreach (var p in packages)
                combined.Add(p);
        }

        return combined;
    }

    private async Task<IReadOnlySet<string>> FetchPackagesAsync(
        string aptServerUrl, string suite, string component, string binArch, CancellationToken ct)
    {
        var url = $"{aptServerUrl.TrimEnd('/')}/dists/{suite}/{component}/{binArch}/Packages.gz";
        var response = await _http.GetAsync(url, ct);

        // 404 means this arch/component combination simply doesn't exist on this mirror
        // (e.g. some mirrors omit binary-all since arch=all packages are already in binary-amd64).
        // Treat as empty rather than an error.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new HashSet<string>();

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return ParsePackagesGz(bytes);
    }

    private static IReadOnlySet<string> ParsePackagesGz(byte[] gzippedData)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var ms = new MemoryStream(gzippedData);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("Package: ", StringComparison.Ordinal))
            {
                packages.Add(line[9..].Trim());
            }
            else if (line.StartsWith("Provides: ", StringComparison.Ordinal))
            {
                // "Provides: gsettings-backend, foo (= 1.0), bar"
                foreach (var token in line[10..].Split(','))
                {
                    var name = token.Trim();
                    var parenIdx = name.IndexOf('(');
                    if (parenIdx > 0)
                        name = name[..parenIdx].Trim();
                    if (!string.IsNullOrEmpty(name))
                        packages.Add(name);
                }
            }
        }

        return packages;
    }
}
