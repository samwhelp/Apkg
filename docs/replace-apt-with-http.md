# Replace `apt-get download` with HTTP for upstream package derivation

## 1. Background — the `apt-get` misery chain

`DebBuilder.DownloadUpstreamDebAsync()` currently uses an **isolated apt pipeline**
to download a single upstream `.deb`:

```
write temporary sources.list
  → apt-get update       (download Packages indices for all registered archs)
  → apt-get download     (download the .deb)
```

This was simple to implement but has caused **three cascading bugs** in production:

| # | Bug | Root cause |
|---|---|---|
| 1 | `binary-arm64/Packages 404` on amd64-only mirrors (issue #multi-arch) | Host has `arm64` registered via `dpkg --add-architecture` for cross-compilation; apt fetches indices for every registered arch regardless of what is being built |
| 2 | `[arch=all]` is not a valid apt arch qualifier | `TargetArchitectures=all` packages like `firmware-sof-anduinos` pass `arch="all"` to the qualifier; apt rejects it, so the download fails silently |
| 3 | Mirror-specific package availability | Running `apt-get update` against a mirror that doesn't carry a particular package (e.g. `firmware-sof-signed` missing from `mirror.aiursoft.com`) fails with no clear error message |

All three bugs share a common root: **we are using an APT package manager pipeline to download a single known .deb file**.  APT was designed to resolve complex dependency graphs across multiple repositories — we are using it as a glorified `wget`.

## 2. The real requirement

Given:
- An APT repository base URL (e.g. `https://archive.ubuntu.com/ubuntu`)
- A suite name (e.g. `noble`)
- A component (e.g. `main`)
- A package name (e.g. `firmware-sof-signed`)  
- An architecture (e.g. `all` or `amd64`)

We need to:
1. Download the **single** `.deb` file for that package from that repository
2. Verify it hasn't been tampered with (GPG signature or checksum)
3. Write it to a temporary path for extraction

This is **one HTTP GET** against a well-known URL:
```
{baseUrl}/dists/{suite}/{component}/binary-{arch}/Packages.gz   ← resolve → get Filename
{baseUrl}/{Filename}                                              ← download .deb
```

## 3. Reusable building blocks already in the codebase

The APKG solution **already contains** the components we need:

### 3.1 `AptPackageIndexClient` (`Aiursoft.Apkg.Sdk.Services`)

- Already used by the **dependency validator** (`AosprojDependencyValidator`)
- Downloads `Packages.gz` via HTTP, parses it, returns a set of available package names
- Handles 404 gracefully (some mirrors omit `binary-all` — it's optional)
- Cache layer: `ConcurrentDictionary<string, Task<IReadOnlySet<string>>>`

**What it currently lacks**: full package metadata (Filename, SHA256, Version).
It only extracts `Package:` and `Provides:` lines.

### 3.2 `AptPackageSource` (`Aiursoft.AptClient`)

- Downloads & parses `Packages.xz` / `Packages.gz` into full `DebianPackage` objects
- `DebianPackage` has: `Filename`, `SHA256`, `Size`, `Version`, `Architecture`, `Depends`, etc.
- `DebianPackageParser.Parse(Stream)` → `List<Dictionary<string,string>>`
- `DebianPackageParser.MapToPackage(Dictionary)` → `DebianPackage`
- `DownloadPackageAsync(package, destinationPath)`:
  - HTTP GET `{baseUrl}{package.Filename}`
  - Streams to temp file
  - Verifies SHA256 against `package.SHA256`
  - Moves to final destination on success; deletes temp file on failure

### 3.3 `AptRepository` (`Aiursoft.AptClient.Abstractions`)

- Wraps a base URL + suite, provides `GetValidatedStreamAsync()`
- Manages GPG signature verification of Release/InRelease files
- Supports `InRelease` (inline signature) and `Release` + `Release.gpg` (detached signature)

### 3.4 `DebianPackageParser` (`Aiursoft.AptClient.Abstractions`)

- Parses RFC 822-style `Packages` format into key-value dictionaries
- Handles multi-line fields (continuation lines starting with space/tab)

## 4. Implementation plan

### 4.1 High-level flow

```
OLD:
  DownloadUpstreamDebAsync()
    → write temporary sources.list
    → apt-get update (isolated apt config)     ← arch pollution, mirror issues
    → apt-get download pkg/suite               ← can't find all-arch packages
    → returns path to .deb

NEW:
  DownloadUpstreamDebAsync()
    → AptPackageIndexClient.GetPackageMetadata(pkgName, arch)   ← HTTP GET Packages.gz
    → find entry matching package name + version/arch
    → AptPackageSource.DownloadPackageAsync(package, destPath)   ← HTTP GET .deb
    → SHA256 verification                                       ← built-in
    → returns path to .deb
```

### 4.2 Step 1: Extend `AptPackageIndexClient` to return full metadata

Add a new method that returns a `DebianPackage` (or at least a record with `Filename` and `SHA256`) for a given `(packageName, arch, suite, component)`:

```csharp
public async Task<DebianPackage?> ResolvePackageAsync(
    string aptServerUrl,
    string suite,
    string component,
    string arch,       // e.g. "amd64" or "all" — "all" means search binary-all
    string packageName,
    CancellationToken ct = default)
{
    // Search binary-all first (for arch=all packages), then binary-{arch}
    foreach (var binArch in arch == "all" 
        ? new[] { "binary-all" }
        : new[] { $"binary-{arch}", "binary-all" })
    {
        var url = $"{aptServerUrl.TrimEnd('/')}/dists/{suite}/{component}/{binArch}/Packages.gz";
        var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) continue;
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var entry = FindPackage(bytes, packageName);
        if (entry != null)
            return DebianPackageParser.MapToPackage(entry, suite, component);
    }
    return null;
}
```

### 4.3 Step 2: GPG signature verification

The current code uses apt's `[signed-by=...]` option to verify the Packages index.
With HTTP, we need to verify either:
- `InRelease` (preferred — signature is inline, one request)
- `Release` + `Release.gpg` (two requests)

The `AptRepository` class already handles this via `GetValidatedStreamAsync()`.
We can either:
- **Option A**: Add `Aiursoft.AptClient.Abstractions` as a dependency of the SDK and use `AptRepository` directly (clean, reuses existing code)
- **Option B**: Implement a minimal GPG verification for InRelease in the SDK (avoids new dependency, but duplicates code)

**Recommendation: Option A.** The SDK already references `Aiursoft.AptClient.Abstractions` indirectly through other packages. Adding a direct dependency is a minor change.

### 4.4 Step 3: Replace `DownloadUpstreamDebAsync`

The new implementation:

```csharp
private async Task<string> DownloadUpstreamDebAsync(
    AosprojProject project,
    string resolvedUpstreamUrl,
    string resolvedUpstreamSuite,
    string resolvedUpstreamComponent,
    string resolvedUpstreamArch,
    string arch,          // build target arch (may be "all")
    string projectDir)
{
    var downloadDir = Path.Combine(projectDir, "obj");
    Directory.CreateDirectory(downloadDir);

    // 1. Resolve the package metadata via HTTP
    var resolvedPackage = await _indexClient.ResolvePackageAsync(
        resolvedUpstreamUrl,
        resolvedUpstreamSuite,
        resolvedUpstreamComponent,
        arch == "all" ? "all" : resolvedUpstreamArch,
        project.UpstreamPackage);

    if (resolvedPackage == null)
        throw new InvalidOperationException(
            $"Package '{project.UpstreamPackage}' not found in {resolvedUpstreamUrl} " +
            $"suite {resolvedUpstreamSuite} component {resolvedUpstreamComponent}.");

    // 2. Set up GPG-validated repository
    var repo = new AptRepository(
        resolvedUpstreamUrl,
        resolvedUpstreamSuite,
        keyringPath: keyringDest);

    // 3. Download via HTTP with SHA256 verification
    var destPath = Path.Combine(downloadDir,
        $"{project.UpstreamPackage}_{resolvedPackage.Version}_{resolvedPackage.Architecture}.deb");
    var source = new AptPackageSource(repo, resolvedUpstreamComponent, resolvedPackage.Architecture);
    await source.DownloadPackageAsync(resolvedPackage, destPath);

    return destPath;
}
```

### 4.5 What gets deleted

After this change, the following code in `DebBuilder.cs` becomes dead code and should be removed:

- The **entire** isolated apt directory setup (~30 lines: `aptTempDir`, `sourceListPath`, `listsDir`, `cacheDir`, creating dirs, writing `sources.list`)
- The `[arch=...]` qualifier logic and `AppendArchQualifier()` method (no longer needed — no apt invocation)
- The `apt-get update` invocation
- The `apt-get download` invocation
- The `BuildDownloadSpec()` method (used only for apt-get download)

That's roughly **60-80 lines of delete**.

## 5. Benefits

| Concern | Before | After |
|---|---|---|
| Arch pollution from foreign dpkg architectures | Requires `[arch=...]` hack, breaks on `arch="all"` | N/A — HTTP GET doesn't care about dpkg arch config |
| `TargetArchitectures=all` packages | `[arch=all]` is invalid; `firmware-sof-anduinos` can't build | Works trivially — just search `binary-all` in the index |
| Mirror availability | `apt-get update` fails on incomplete mirrors with opaque errors | `HttpStatusCode.NotFound` handled per-index, clear error messages |
| GPG signature verification | Relies on apt's `[signed-by=...]` option in sources.list | Explicit via `AptRepository` — testable, debuggable |
| Reproducibility / debugging | apt writes logs to system paths, stateful | Pure HTTP — logs are `ILogger` messages, no side effects |
| Build speed | `apt-get update` downloads multiple Packages indices (all archs all components) | Only the needed index is fetched; cached via `ConcurrentDictionary` |

## 6. Acceptance criteria

### 6.1 Must pass

- [ ] `firmware-sof-anduinos` (UpstreamArch=all, TargetArchitectures=all) builds successfully
  - **This is the canonical regression test.** Currently broken.
- [ ] `base-files` (UpstreamArch=$(Arch), TargetArchitectures=amd64 arm64) builds successfully for both archs
- [ ] `anduinos-software-properties-common` (UpstreamArch=amd64, TargetArchitectures=all) builds successfully
- [ ] `plymouth-anduinos` (conditional UpstreamUrl: amd64→archive, arm64→ports) builds for both archs
- [ ] GPG signature verification: building with a valid `UpstreamSignedBy` keyring succeeds
- [ ] GPG signature verification: building with an invalid/missing keyring fails with a clear error
- [ ] SHA256 mismatch on downloaded .deb fails with a clear error
- [ ] 404 on Packages.gz for a missing component returns a clear "package not found" error
- [ ] All existing unit tests pass (update tests that mock `apt-get`)

### 6.2 Nice to have

- [ ] Cached Packages.gz indices (already in `AptPackageIndexClient` via `ConcurrentDictionary`)
- [ ] Progress reporting during .deb download
- [ ] Fallback: if `Packages.xz` exists, prefer it over `Packages.gz` (better compression)

## 7. Risk assessment

| Risk | Likelihood | Mitigation |
|---|---|---|
| `AptClient` library has different `DebianPackage` schema than what's in Ubuntu's Packages files | Low | `DebianPackageParser` is already battle-tested in production (AptClient is used for the dependency validator) |
| Some mirrors require authentication | Low | We control the mirror list — all AnduinOS upstreams are public. Can add `Authorization` header support if needed |
| `Packages.gz` is large (several MB) | Medium | Already cached via `ConcurrentDictionary` in `AptPackageIndexClient`. Only fetched once per build session |
| Backwards compatibility with existing `.aosproj` files | Low | The change is internal to `DebBuilder`. `.aosproj` syntax is unchanged |

## 8. Migration path

1. Implement the new HTTP-based download path
2. Run the full integration test suite against all upstream-derived packages
3. Once all tests pass, delete the old `apt-get` codepath
4. Bump SDK version
5. All `.aosproj` files work without any changes

**No `.aosproj` syntax changes are required.** This is a pure implementation swap.
