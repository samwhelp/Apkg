using System.Formats.Tar;
using System.IO.Compression;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.ApkgUploadsViewModels;
using Aiursoft.Apkg.Models.LocalPackagesViewModels;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Controllers;

[Authorize]
public class ApkgUploadsController(
    ApkgDbContext db,
    ManifestSerializer manifestSerializer,
    StorageService storageService,
    DebUploadService debUploadService,
    FeatureFoldersProvider folders,
    UserManager<User> userManager,
    AptVersionComparisonService versionComparer,
    ILogger<ApkgUploadsController> logger) : Controller
{
    [HttpGet]
    [RenderInNavBar(
        NavGroupName = "MyPackages",
        NavGroupOrder = 25,
        CascadedLinksGroupName = "MyPackagesSub",
        CascadedLinksIcon = "package",
        CascadedLinksOrder = 10,
        LinkText = "Apkg Hybrid",
        LinkOrder = 2)]
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var query = db.ApkgUploads
            .Include(u => u.UploadedByUser)
            .Include(u => u.Packages)
            .AsQueryable();

        if (!isAdmin)
            query = query.Where(u => u.UploadedByUserId == userId && u.IsListed);

        var allUploads = await query
            .OrderByDescending(u => u.UploadedAt)
            .ToListAsync();

        // Group by (Package, Distro, Component) — the unique package identity
        var groups = allUploads
            .GroupBy(u => (u.Package, u.Distro, u.Component))
            .OrderByDescending(g => g.Max(u => u.UploadedAt))
            .ToList();

        // Collect all packages across all uploads for live-status computation
        var allPackages = allUploads.SelectMany(u => u.Packages).ToList();
        var allRepoIds = allPackages.Select(p => p.RepositoryId).Distinct().ToList();

        var repoPrimaryBuckets = await db.AptRepositories
            .Where(r => allRepoIds.Contains(r.Id) && r.PrimaryBucketId != null)
            .Select(r => new { r.Id, r.PrimaryBucketId })
            .ToListAsync();
        var repoPrimaryMap = repoPrimaryBuckets.ToDictionary(r => r.Id, r => r.PrimaryBucketId!.Value);

        var allPrimaryBucketIds = repoPrimaryBuckets
            .Select(r => r.PrimaryBucketId!.Value)
            .Distinct()
            .ToList();

        var liveKeySet = new HashSet<(int BucketId, string Package, string Version, string Architecture)>();
        if (allPrimaryBucketIds.Count > 0)
        {
            var aptPackages = await db.AptPackages
                .Where(p => allPrimaryBucketIds.Contains(p.BucketId))
                .Select(p => new { p.BucketId, p.Package, p.Version, p.Architecture })
                .ToListAsync();
            foreach (var ap in aptPackages)
                liveKeySet.Add((ap.BucketId, ap.Package, ap.Version, ap.Architecture));
        }

        var indexItems = new List<ApkgUploadIndexItem>();

        foreach (var group in groups)
        {
            var orderedUploads = group.OrderByDescending(u => u.UploadedAt).ToList();
            var latestUpload = orderedUploads.First();

            // Find the latest published upload that has live packages
            ApkgUpload? liveUpload = null;
            foreach (var upload in orderedUploads.Where(u => u.IsPublished))
            {
                var hasLive = upload.Packages.Any(lp =>
                    lp.IsEnabled &&
                    repoPrimaryMap.TryGetValue(lp.RepositoryId, out var bucketId) &&
                    liveKeySet.Contains((bucketId, lp.Package, lp.Version, lp.Architecture)));
                if (hasLive)
                {
                    liveUpload = upload;
                    break;
                }
            }

            // If no live upload, use the latest published upload (or the latest draft for the owner)
            var displayUpload = liveUpload ?? orderedUploads.FirstOrDefault(u => u.IsPublished) ?? latestUpload;

            var totalCount = displayUpload.Packages.Count;
            var livePackages = displayUpload.Packages
                .Where(lp =>
                    lp.IsEnabled &&
                    repoPrimaryMap.TryGetValue(lp.RepositoryId, out var bucketId) &&
                    liveKeySet.Contains((bucketId, lp.Package, lp.Version, lp.Architecture)))
                .ToList();

            int? nextVersionUploadId = null;
            string? nextVersionSummary = null;
            if (liveUpload != null && latestUpload.Id != liveUpload.Id && latestUpload.IsPublished)
            {
                nextVersionUploadId = latestUpload.Id;
                nextVersionSummary = latestUpload.Packages
                    .Select(p => p.Version)
                    .Distinct()
                    .FirstOrDefault();
            }

            indexItems.Add(new ApkgUploadIndexItem
            {
                Upload = displayUpload,
                PublishedCount = livePackages.Count,
                TotalPackageCount = totalCount,
                LiveVersions = livePackages.Select(lp => lp.Version).Distinct().ToList(),
                SyncStatus = liveUpload != null ? UploadSyncStatus.Live
                    : displayUpload.IsPublished ? UploadSyncStatus.Syncing
                    : UploadSyncStatus.Draft,
                NextVersionUploadId = nextVersionUploadId,
                NextVersionSummary = nextVersionSummary
            });
        }

        return this.StackView(new ApkgUploadsIndexViewModel
        {
            Uploads = indexItems,
            IsAdmin = isAdmin
        });
    }

    [HttpGet]
    public async Task<IActionResult> PackageHistory(string name, string? distro = null, string? component = null, string? versionsFilter = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest();

        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var query = db.ApkgUploads
            .Include(u => u.UploadedByUser)
            .Include(u => u.Packages)
                .ThenInclude(p => p.Repository)
            .Where(u => u.Package == name)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(distro))
            query = query.Where(u => u.Distro == distro);
        if (!string.IsNullOrWhiteSpace(component))
            query = query.Where(u => u.Component == component);

        if (!isAdmin)
            query = query.Where(u => u.UploadedByUserId == userId && u.IsListed);

        var uploads = await query
            .OrderByDescending(u => u.UploadedAt)
            .ToListAsync();

        if (uploads.Count == 0)
            return NotFound();

        var allPackages = uploads.SelectMany(u => u.Packages).ToList();
        var allPackageStatuses = await BuildPackageStatusAsync(allPackages);

        int? latestVersionId = uploads.FirstOrDefault(u => u.IsPublished)?.Id;
        var normalizedFilter = versionsFilter?.ToLowerInvariant() switch
        {
            "live" => "live",
            "all"  => "all",
            _      => "latest"
        };

        return this.StackView(new ApkgUploadsPackageHistoryViewModel
        {
            PackageName = name,
            Uploads = uploads,
            AllPackageStatuses = allPackageStatuses,
            IsAdmin = isAdmin,
            LatestVersionId = latestVersionId,
            VersionsFilter = normalizedFilter
        });
    }

    [HttpGet]
    public IActionResult Upload()
    {
        return this.StackView(new ApkgUploadsUploadViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(ApkgUploadsUploadViewModel model)
    {
        if (!ModelState.IsValid)
            return this.StackView(model);

        string physicalPath;
        try
        {
            physicalPath = storageService.GetFilePhysicalPath(model.ApkgFilePath!, isVault: true);
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        if (!System.IO.File.Exists(physicalPath))
        {
            ModelState.AddModelError(nameof(model.ApkgFilePath), "File upload failed or missing. Please re-upload.");
            return this.StackView(model);
        }

        ApkgPackageManifest manifest;
        try
        {
            manifest = await ReadManifestAsync(physicalPath)
                ?? throw new InvalidOperationException("manifest.xml was not found in the .apkg archive.");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(model.ApkgFilePath), $"Failed to parse .apkg file: {ex.Message}");
            return this.StackView(model);
        }

        if (manifest.Entries.Count == 0)
            ModelState.AddModelError(nameof(model.ApkgFilePath), "manifest.xml: at least one <Entry> is required.");

        var distro = manifest.Distro.Trim().ToLowerInvariant();
        var component = manifest.Component.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(distro))
            ModelState.AddModelError(nameof(model.ApkgFilePath), "manifest.xml: <Distro> is required.");
        if (string.IsNullOrWhiteSpace(component))
            ModelState.AddModelError(nameof(model.ApkgFilePath), "manifest.xml: <Component> is required.");

        if (!ModelState.IsValid)
            return this.StackView(model);

        var userId = userManager.GetUserId(User)!;
        await EnsurePackageOwnershipAsync(manifest.Name, distro, component, userId);

        var pendingUpload = await db.ApkgUploads
            .FirstOrDefaultAsync(u => u.UploadedByUserId == userId
                                      && u.VaultPath == model.ApkgFilePath
                                      && !u.IsPublished);

        var fileName = Path.GetFileName(model.ApkgFilePath!);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "upload.apkg";
        if (pendingUpload == null)
        {
            pendingUpload = new ApkgUpload
            {
                UploadedByUserId = userId,
                FileName = fileName,
                Package = manifest.Name,
                Distro = distro,
                Component = component,
                Description = NullIfEmpty(manifest.Description),
                Maintainer = NullIfEmpty(manifest.Maintainer),
                Homepage = NullIfEmpty(manifest.Homepage),
                VaultPath = model.ApkgFilePath,
                IsPublished = false,
                IsListed = true
            };
            db.ApkgUploads.Add(pendingUpload);
        }
        else
        {
            pendingUpload.FileName = fileName;
            pendingUpload.Package = manifest.Name;
            pendingUpload.Distro = distro;
            pendingUpload.Component = component;
            pendingUpload.Description = NullIfEmpty(manifest.Description);
            pendingUpload.Maintainer = NullIfEmpty(manifest.Maintainer);
            pendingUpload.Homepage = NullIfEmpty(manifest.Homepage);
            pendingUpload.UploadedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        return RedirectToAction(nameof(Preview), new { vaultPath = model.ApkgFilePath });
    }

    [HttpGet]
    public async Task<IActionResult> Preview(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return BadRequest();

        string physicalPath;
        try
        {
            physicalPath = storageService.GetFilePhysicalPath(vaultPath, isVault: true);
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        if (!System.IO.File.Exists(physicalPath))
            return NotFound();

        ApkgPackageManifest manifest;
        try
        {
            manifest = await ReadManifestAsync(physicalPath)
                ?? throw new InvalidOperationException("manifest.xml was not found in the .apkg archive.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load preview for APKG {VaultPath}.", vaultPath);
            return BadRequest();
        }

        var userId = userManager.GetUserId(User)!;
        var upload = await db.ApkgUploads
            .FirstOrDefaultAsync(u => u.UploadedByUserId == userId
                                      && u.VaultPath == vaultPath
                                      && !u.IsPublished);

        var fileName = upload?.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = Path.GetFileName(vaultPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "upload.apkg";

        var previewModel = await BuildPreviewModelAsync(manifest, vaultPath, fileName);
        return this.StackView(previewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string vaultPath, string fileName)
    {
        fileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(vaultPath))
            return BadRequest();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = Path.GetFileName(vaultPath);

        string physicalPath;
        try
        {
            physicalPath = storageService.GetFilePhysicalPath(vaultPath, isVault: true);
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        if (!System.IO.File.Exists(physicalPath))
            return NotFound();

        var extractedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ApkgPackageManifest manifest;
        try
        {
            manifest = await ExtractApkgAsync(physicalPath, extractedEntries)
                ?? throw new InvalidOperationException("manifest.xml was not found in the .apkg archive.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish APKG from {VaultPath}.", vaultPath);
            return BadRequest();
        }

        var distro = manifest.Distro.Trim().ToLowerInvariant();
        var component = manifest.Component.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(distro) || string.IsNullOrWhiteSpace(component) || manifest.Entries.Count == 0)
            return BadRequest();

        var userId = userManager.GetUserId(User)!;
        await EnsurePackageOwnershipAsync(manifest.Name, distro, component, userId);

        var upload = await db.ApkgUploads
            .Include(u => u.Packages)
            .FirstOrDefaultAsync(u => u.UploadedByUserId == userId
                                      && u.VaultPath == vaultPath
                                      && !u.IsPublished);

        if (upload == null)
        {
            upload = new ApkgUpload
            {
                UploadedByUserId = userId,
                FileName = fileName,
                Package = manifest.Name,
                Distro = distro,
                Component = component,
                Description = NullIfEmpty(manifest.Description),
                Maintainer = NullIfEmpty(manifest.Maintainer),
                Homepage = NullIfEmpty(manifest.Homepage),
                VaultPath = vaultPath,
                IsPublished = false,
                IsListed = true
            };
            db.ApkgUploads.Add(upload);
            await db.SaveChangesAsync();
        }

        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        var canUploadRestricted = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories);
        var skippedRepos = new List<string>();

        try
        {
            foreach (var entry in manifest.Entries)
            {
                var archiveDebPath = NormalizeArchiveEntryName(entry.DebFile);
                if (!extractedEntries.TryGetValue(archiveDebPath, out var extractedDebSource))
                {
                    ModelState.AddModelError(string.Empty, $"Archive entry '{entry.DebFile}' was not found for target {distro} {entry.Suite} {entry.Architecture}.");
                    var previewModel = await BuildPreviewModelAsync(manifest, vaultPath, fileName);
                    return this.StackView(previewModel, "Preview");
                }

                // KEEP IN SYNC with ArchitectureMatches helper below and ApiPackagesController line 165
                var matchingRepositories = (await db.AptRepositories
                        .Where(r => r.Distro == distro
                                    && r.Suite == entry.Suite)
                        .ToListAsync())
                    .Where(r => r.Components
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(component, StringComparer.OrdinalIgnoreCase)
                        && (r.Architecture
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Any(a => string.Equals(a, entry.Architecture, StringComparison.OrdinalIgnoreCase))
                            || string.Equals(entry.Architecture, "all", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (matchingRepositories.Count == 0)
                {
                    logger.LogWarning(
                        "No repository found for {Distro} {Suite} {Architecture} with component {Component}.",
                        distro,
                        entry.Suite,
                        entry.Architecture,
                        component);
                    continue;
                }

                foreach (var repo in matchingRepositories)
                {
                    if (!CanUploadToRepository(repo, isAdmin, canUploadRestricted))
                    {
                        var repoName = DebUploadService.GetRepositoryDisplayName(repo);
                        logger.LogWarning(
                            "Skipping repository {Repository} because user {UserId} cannot upload to it.",
                            repoName,
                            userId);
                        skippedRepos.Add(repoName);
                        continue;
                    }

                    var uploadTempPath = CreateWorkspaceTempFilePath(".deb");
                    try
                    {
                        await using (var source = new FileStream(extractedDebSource, FileMode.Open, FileAccess.Read, FileShare.Read))
                        await using (var destination = System.IO.File.Create(uploadTempPath))
                            await source.CopyToAsync(destination);

                        var result = await debUploadService.UploadDebToRepositoryAsync(repo, component, uploadTempPath, userId, upload.Id);
                        if (result.Package != null)
                            continue;

                        ModelState.AddModelError(string.Empty, result.Error ?? $"Upload failed for repository {DebUploadService.GetRepositoryDisplayName(repo)}.");
                        var previewModel = await BuildPreviewModelAsync(manifest, vaultPath, fileName);
                        return this.StackView(previewModel, "Preview");
                    }
                    finally
                    {
                        DeleteIfExists(uploadTempPath);
                    }
                }
            }

            DeleteIfExists(physicalPath);

            upload.FileName = fileName;
            upload.Package = manifest.Name;
            upload.Component = component;
            upload.Description = NullIfEmpty(manifest.Description);
            upload.Maintainer = NullIfEmpty(manifest.Maintainer);
            upload.Homepage = NullIfEmpty(manifest.Homepage);
            upload.VaultPath = null;
            upload.IsPublished = true;
            upload.IsListed = true;
            await db.SaveChangesAsync();

            if (skippedRepos.Count > 0)
                TempData["SkippedRepoWarnings"] = string.Join("|", skippedRepos.Distinct());

            return RedirectToAction(nameof(Details), new { id = upload.Id });
        }
        finally
        {
            foreach (var extractedEntry in extractedEntries.Values)
                DeleteIfExists(extractedEntry);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, string? tab = null, string? versionsFilter = null)
    {
        var upload = await db.ApkgUploads
            .Include(u => u.UploadedByUser)
            .Include(u => u.Packages)
                .ThenInclude(p => p.Repository)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (upload == null)
            return NotFound();

        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        var isOwner = upload.UploadedByUserId == userId;
        if (!isAdmin && !isOwner)
            return Forbid();

        var historyQuery = db.ApkgUploads
            .Include(u => u.UploadedByUser)
            .Include(u => u.Packages)
                .ThenInclude(p => p.Repository)
            .Where(u => u.Package == upload.Package && u.Distro == upload.Distro && u.Component == upload.Component)
            .AsQueryable();

        if (!isAdmin)
            historyQuery = historyQuery.Where(u => u.UploadedByUserId == userId && u.IsListed);

        var versionHistory = await historyQuery
            .OrderByDescending(v => v.UploadedAt)
            .ToListAsync();

        // Use the most recent *published* upload as "latest" so that Draft uploads
        // don't cause the Latest filter to return an empty table.
        int? latestVersionId = versionHistory.FirstOrDefault(v => v.IsPublished)?.Id;

        // Gather ALL packages across all uploads for the Versions tab
        var allHistoryPackages = versionHistory.SelectMany(v => v.Packages).ToList();

        // Always include current upload's packages (handles unlisted edge-case where
        // the current upload may not appear in historyQuery results)
        var historyPackageIds = allHistoryPackages.Select(p => p.Id).ToHashSet();
        foreach (var pkg in upload.Packages)
        {
            if (!historyPackageIds.Contains(pkg.Id))
                allHistoryPackages.Add(pkg);
        }

        var allPackageStatuses = await BuildPackageStatusAsync(allHistoryPackages);

        // Overview tab needs only this upload's packages
        var currentPackageIds = upload.Packages.Select(p => p.Id).ToHashSet();
        var packages = allPackageStatuses
            .Where(ps => currentPackageIds.Contains(ps.Package.Id))
            .ToList();

        var normalizedFilter = versionsFilter?.ToLowerInvariant() switch
        {
            "live" => "live",
            "all"  => "all",
            _      => "latest"
        };

        var activeTab = tab == "versions" ? "versions" : "overview";

        return this.StackView(new ApkgUploadsDetailsViewModel
        {
            Upload = upload,
            Packages = packages,
            AllPackageStatuses = allPackageStatuses,
            VersionHistory = versionHistory,
            LatestVersionId = latestVersionId,
            ActiveTab = activeTab,
            IsAdmin = isAdmin,
            IsOwner = isOwner,
            VersionsFilter = normalizedFilter
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlist(int id)
    {
        var upload = await db.ApkgUploads
            .Include(u => u.Packages)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (upload == null)
            return NotFound();

        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        if (!isAdmin && upload.UploadedByUserId != userId)
            return Forbid();

        upload.IsListed = false;
        foreach (var package in upload.Packages)
            package.IsEnabled = false;

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Relist(int id)
    {
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        if (!isAdmin)
            return Forbid();

        var upload = await db.ApkgUploads
            .Include(u => u.Packages)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (upload == null)
            return NotFound();

        upload.IsListed = true;
        foreach (var package in upload.Packages)
            package.IsEnabled = true;

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DiscardDraft(string vaultPath)
    {
        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var draft = await db.ApkgUploads
            .Include(u => u.Packages)
            .FirstOrDefaultAsync(u => u.VaultPath == vaultPath && !u.IsPublished);

        if (draft == null)
            return RedirectToAction(nameof(Upload));

        if (draft.UploadedByUserId != userId && !isAdmin)
            return Forbid();

        if (!string.IsNullOrWhiteSpace(draft.VaultPath))
        {
            try
            {
                var physicalPath = storageService.GetFilePhysicalPath(draft.VaultPath, isVault: true);
                DeleteIfExists(physicalPath);
            }
            catch (ArgumentException) { }
        }

        db.LocalPackages.RemoveRange(draft.Packages);
        db.ApkgUploads.Remove(draft);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Upload));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        if (!isAdmin)
            return Forbid();

        var upload = await db.ApkgUploads
            .Include(u => u.Packages)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (upload == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(upload.VaultPath))
        {
            try
            {
                var physicalPath = storageService.GetFilePhysicalPath(upload.VaultPath, isVault: true);
                DeleteIfExists(physicalPath);
            }
            catch (ArgumentException)
            {
            }
        }

        db.LocalPackages.RemoveRange(upload.Packages);
        db.ApkgUploads.Remove(upload);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<ApkgUploadsPreviewViewModel> BuildPreviewModelAsync(ApkgPackageManifest manifest, string vaultPath, string fileName)
    {
        var targets = await BuildTargetInfosAsync(manifest);
        return new ApkgUploadsPreviewViewModel
        {
            VaultPath = vaultPath,
            FileName = fileName,
            Manifest = manifest,
            Targets = targets
        };
    }

    private async Task<List<ApkgPreviewTargetInfo>> BuildTargetInfosAsync(ApkgPackageManifest manifest)
    {
        var distro = manifest.Distro.Trim().ToLowerInvariant();
        var component = manifest.Component.Trim().ToLowerInvariant();
        var targets = new List<ApkgPreviewTargetInfo>();
        foreach (var entry in manifest.Entries)
        {
            var matchingRepos = (await db.AptRepositories
                    .Where(r => r.Distro == distro
                                && r.Suite == entry.Suite)
                    .ToListAsync())
                .Where(r => r.Components
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(component, StringComparer.OrdinalIgnoreCase)
                    && (r.Architecture
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Any(a => string.Equals(a, entry.Architecture, StringComparison.OrdinalIgnoreCase))
                        || string.Equals(entry.Architecture, "all", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            targets.Add(new ApkgPreviewTargetInfo
            {
                Entry = entry,
                MatchingRepositories = matchingRepos
            });
        }

        return targets;
    }

    private static bool CanUploadToRepository(AptRepository repo, bool isAdmin, bool canUploadRestricted)
    {
        return repo.AllowAnyoneToUpload || isAdmin || canUploadRestricted;
    }

    private async Task<ApkgPackageManifest?> ReadManifestAsync(string apkgPath)
    {
        await using var fileStream = new FileStream(apkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            if (entry.DataStream == null)
                continue;

            var entryName = NormalizeArchiveEntryName(entry.Name);
            if (!string.Equals(entryName, "manifest.xml", StringComparison.OrdinalIgnoreCase))
                continue;

            using var reader = new StreamReader(entry.DataStream);
            var manifestXml = await reader.ReadToEndAsync();
            return manifestSerializer.DeserializePackageManifest(manifestXml);
        }

        return null;
    }

    private async Task<ApkgPackageManifest?> ExtractApkgAsync(string apkgPath, Dictionary<string, string> extractedEntries)
    {
        await using var fileStream = new FileStream(apkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        ApkgPackageManifest? manifest = null;
        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            if (entry.DataStream == null)
                continue;

            var entryName = NormalizeArchiveEntryName(entry.Name);
            if (string.IsNullOrWhiteSpace(entryName))
                continue;

            var tempEntryPath = CreateWorkspaceTempFilePath(Path.GetExtension(entryName));
            await using (var tempStream = System.IO.File.Create(tempEntryPath))
                await entry.DataStream.CopyToAsync(tempStream);

            if (extractedEntries.Remove(entryName, out var oldEntryPath))
                DeleteIfExists(oldEntryPath);

            extractedEntries[entryName] = tempEntryPath;

            if (string.Equals(entryName, "manifest.xml", StringComparison.OrdinalIgnoreCase))
            {
                var manifestXml = await System.IO.File.ReadAllTextAsync(tempEntryPath);
                manifest = manifestSerializer.DeserializePackageManifest(manifestXml);
            }
        }

        return manifest;
    }

    private string CreateWorkspaceTempFilePath(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".bin";
        if (!extension.StartsWith('.'))
            extension = $".{extension}";

        return Path.Combine(folders.GetWorkspaceFolder(), $"apkg-upload-{Guid.NewGuid()}{extension}");
    }

    private async Task<List<PackageStatusInfo>> BuildPackageStatusAsync(List<LocalPackage> packages)
    {
        if (packages.Count == 0)
            return [];

        var repoIds = packages.Select(p => p.RepositoryId).Distinct().ToList();
        var repoBuckets = await db.AptRepositories
            .Where(r => repoIds.Contains(r.Id))
            .Select(r => new { r.Id, r.PrimaryBucketId, r.SecondaryBucketId })
            .ToListAsync();

        var allRelevantBucketIds = repoBuckets
            .SelectMany(r => new[] { r.PrimaryBucketId, r.SecondaryBucketId })
            .Where(b => b.HasValue)
            .Select(b => b!.Value)
            .Distinct()
            .ToList();

        var existingInBuckets = await db.AptPackages
            .Where(ap => allRelevantBucketIds.Contains(ap.BucketId))
            .Select(ap => new { ap.BucketId, ap.Package, ap.Version, ap.Architecture, ap.Id })
            .ToListAsync();

        var bucketLookup = existingInBuckets
            .GroupBy(x => x.BucketId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => (x.Package, x.Version, x.Architecture))
                    .ToDictionary(x => x.Key, x => x.First().Id));

        // Secondary lookup: for each bucket, map (Package, Architecture) → live Version.
        // Used to compare versions for superseded vs pending-sync detection.
        var anyVersionLookup = existingInBuckets
            .GroupBy(x => x.BucketId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => (x.Package, x.Architecture))
                    .ToDictionary(x => x.Key, x => x.First().Version));

        var repoLookup = repoBuckets.ToDictionary(r => r.Id, r => r);

        return packages.Select(lp =>
        {
            var status = LocalPackageStatus.PendingSync;
            var message = "Waiting for the next Repository Sync job (runs every 20 minutes).";
            int? liveId = null;

            if (!lp.IsEnabled)
            {
                status = LocalPackageStatus.Disabled;
                message = "This package is disabled and will not be included in future syncs.";
            }
            else
            {
                var repoInfo = repoLookup.GetValueOrDefault(lp.RepositoryId);
                var primaryBucketPackages = repoInfo?.PrimaryBucketId != null
                    ? bucketLookup.GetValueOrDefault(repoInfo.PrimaryBucketId.Value)
                    : null;

                if (primaryBucketPackages?.TryGetValue((lp.Package, lp.Version, lp.Architecture), out var foundId) == true)
                {
                    status = LocalPackageStatus.Live;
                    message = "Package is live and available for APT clients.";
                    liveId = foundId;
                }
                else
                {
                    var secondaryBucketPackages = repoInfo?.SecondaryBucketId != null
                        ? bucketLookup.GetValueOrDefault(repoInfo.SecondaryBucketId.Value)
                        : null;

                    if (secondaryBucketPackages?.ContainsKey((lp.Package, lp.Version, lp.Architecture)) == true)
                    {
                        status = LocalPackageStatus.StagedForSigning;
                        message = "Included in a pending bucket. Waiting for signing (up to 5 minutes).";
                    }
                    else if (repoInfo?.PrimaryBucketId != null
                             && anyVersionLookup.TryGetValue(repoInfo.PrimaryBucketId.Value, out var primaryByArch)
                             && primaryByArch.TryGetValue((lp.Package, lp.Architecture), out var liveVersion))
                    {
                        var cmp = versionComparer.Compare(lp.Version, liveVersion);
                        if (cmp > 0)
                        {
                            status = LocalPackageStatus.PendingSync;
                            message = $"Newer version waiting to replace {liveVersion}. Waiting for the next Repository Sync job (runs every 20 minutes).";
                        }
                        else
                        {
                            status = LocalPackageStatus.Superseded;
                            message = $"A newer version ({liveVersion}) is live. This version will not be synced.";
                        }
                    }
                }
            }

            return new PackageStatusInfo
            {
                Package = lp,
                Status = status,
                StatusMessage = message,
                LivePackageId = liveId
            };
        }).ToList();
    }

    private static string NormalizeArchiveEntryName(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task EnsurePackageOwnershipAsync(string package, string distro, string component, string userId)
    {
        var ownedByOther = await db.ApkgUploads
            .AnyAsync(u => u.Package == package && u.Distro == distro && u.Component == component
                           && u.UploadedByUserId != userId);
        if (ownedByOther)
            throw new InvalidOperationException(
                $"Package '{package}' for distro '{distro}' component '{component}' is already owned by another user. Only the original uploader may add versions to this package.");
    }

    // KEEP IN SYNC with inline conditions and ApiPackagesController.ArchitectureMatches.
    // EF can't translate this helper to SQL, so queries duplicate the logic inline.
    // Any change must be mirrored to all locations AND ApiPackagesController.ArchitectureMatches.
    internal static bool ArchitectureMatches(string repoArchitecture, string entryArchitecture)
    {
        if (string.Equals(entryArchitecture, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        return repoArchitecture
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(a => string.Equals(a, entryArchitecture, StringComparison.OrdinalIgnoreCase));
    }
}
