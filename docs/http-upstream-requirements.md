# HTTP upstream download — integration requirements

## Background

The apt-get pipeline was replaced with direct HTTP (AptClient). The code
compiles and all 326 unit tests pass, but **no upstream-derived package
can be built** against `mirror.aiursoft.com`. Unit tests mock HTTP,
so the integration path has never been exercised against a real mirror.

## Acceptance criteria

The following packages must build successfully with `apkg publish`:

| Package | UpstreamArch | TargetArch | Upstream repo | Why it matters |
|---|---|---|---|---|
| `base-files` | amd64 | all | mirror.aiursoft.com | Simplest case: arch-specific upstream, all-arch target |
| `firmware-sof-anduinos` | all | all | mirror.aiursoft.com | Double-all: the hardest case. Previously broken by `[arch=all]` in apt-get |
| `plymouth-anduinos` | $(Arch) | amd64 arm64 | mirror.aiursoft.com (amd64) / ports.ubuntu.com (arm64) | Conditional UpstreamUrl |
| `anduinos-software-properties-common` | amd64 | all | mirror.aiursoft.com | Has UpstreamSignedBy |
| `firefox-anduinos` | amd64 | amd64 arm64 | packages.mozilla.org | Non-Ubuntu upstream |

## Debugging hints

The current failure mode is:

```
Downloading <pkg> from https://mirror.aiursoft.com/ubuntu (noble)...
System.InvalidOperationException: Package '<pkg>' not found in
  https://mirror.aiursoft.com/ubuntu suite noble component main.
```

The mirror **is fine** — `curl -s https://mirror.aiursoft.com/ubuntu/dists/noble/InRelease`
returns 200 with valid GPG-signed data, and `main/binary-amd64/Packages.gz`
is available. The issue is in the AptClient → AptRepository →
AptPackageSource chain.

Suspect areas:
1. `AptRepository.EnsureVerifiedAsync()` — InRelease parsing may not
   populate `_trustedHashes` correctly for all Packages indices
2. `AptPackageSource.FetchPackagesAsync()` — the supported-files check
   may skip Packages.gz because it's not in the hash list, or the
   arch handling is wrong
3. `DebianPackageParser.ParseInRelease()` — may not extract file paths
   in the expected format
4. `DebianPackageParser.MapToPackage()` — may filter packages by
   architecture incorrectly

## mirror.aiursoft.com quirks

This mirror (ASP.NET Kestrel with auto-mirror caching) has a few
properties worth knowing:

- **HEAD requests don't trigger auto-mirror** — the middleware only
  kicks in for GET. HEAD returns 404 for uncached files.
- **No binary-all index** — `main/binary-all/Packages.gz` returns 404.
  `Architecture: all` packages (like `firmware-sof-signed`) are listed
  inside `main/binary-amd64/Packages.gz`.
- **No Packages.xz** — only `.gz` is available.
- **Reasonable first-GET latency** — the first GET for an uncached
  file takes ~1s (upstream fetch from archive.ubuntu.com). Subsequent
  requests are instant (cached).

## Related fix already applied

`DebBuilder.DownloadUpstreamDebAsync()` now falls back to host CPU arch
when the build target arch is `"all"` (commit `78af4c9`):

```csharp
var hostArch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
var effectiveArch = arch == "all" ? hostArch : arch;
```

Without this, `searchArches` would be `["all", "all"]` — both invalid
for `AptPackageSource`.
