using Aiursoft.AptClient;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class DebResolutionServiceTests
{
    private readonly DebResolutionService _sut = new(new AptVersionComparisonService());

    private static AptRepository MakeRepo(int id, string suite) => new()
    {
        Id = id,
        Name = suite,
        Distro = "test",
        Suite = suite,
        Components = "main",
        Architecture = "amd64"
    };

    private static ApkgDebPackage MakeDeb(
        int id,
        string package,
        string version,
        string arch,
        int repoId,
        AptRepository repo,
        bool isEnabled = true) => new()
    {
        Id = id,
        Package = package,
        Version = version,
        Architecture = arch,
        RepositoryId = repoId,
        Repository = repo,
        IsEnabled = isEnabled,
        UploadedByUserId = "test-user",
        Maintainer = "test",
        Description = "test",
        Section = "utils",
        SHA256 = "sha256-" + id,
        Size = "100",
        Filename = $"pool/main/{package}/{package}_{version}_{arch}.deb"
    };

    [TestMethod]
    public void ResolveWinningDebs_AllEnabled_ReturnsAllDistinctSlots()
    {
        var repo = MakeRepo(1, "focal");
        var debs = new List<ApkgDebPackage>
        {
            MakeDeb(1, "pkg-a", "1.0.0", "amd64", 1, repo),
            MakeDeb(2, "pkg-a", "1.0.0", "arm64", 1, repo),
            MakeDeb(3, "pkg-b", "1.0.0", "amd64", 1, repo),
        };

        var result = _sut.ResolveWinningDebs(debs);

        Assert.AreEqual(3, result.Count, "Each (Package, Arch, Suite, RepoId) slot should have a winner.");
    }

    [TestMethod]
    public void ResolveWinningDebs_PicksMaxVersionPerSlot()
    {
        var repo = MakeRepo(1, "focal");
        var debs = new List<ApkgDebPackage>
        {
            MakeDeb(1, "pkg-a", "1.0.0", "amd64", 1, repo),
            MakeDeb(2, "pkg-a", "2.0.0", "amd64", 1, repo),
            MakeDeb(3, "pkg-a", "1.5.0", "amd64", 1, repo),
        };

        var result = _sut.ResolveWinningDebs(debs);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("2.0.0", result[0].Version);
    }

    [TestMethod]
    public void ResolveWinningDebs_DifferentSuitesAreDifferentSlots()
    {
        var focal = MakeRepo(1, "focal");
        var noble = MakeRepo(2, "noble");
        var debs = new List<ApkgDebPackage>
        {
            MakeDeb(1, "pkg-a", "1.0.0", "amd64", 1, focal),
            MakeDeb(2, "pkg-a", "1.0.0", "amd64", 2, noble),
        };

        var result = _sut.ResolveWinningDebs(debs);

        Assert.AreEqual(2, result.Count, "Same (Package, Arch) but different suites should be distinct slots.");
    }

    [TestMethod]
    public void ResolveWinningDebs_OnlyEnabledAreConsidered()
    {
        var repo = MakeRepo(1, "focal");
        var debs = new List<ApkgDebPackage>
        {
            MakeDeb(1, "pkg-a", "1.0.0", "amd64", 1, repo, isEnabled: false),
            MakeDeb(2, "pkg-b", "1.0.0", "amd64", 1, repo, isEnabled: true),
        };

        var result = _sut.ResolveWinningDebs(debs);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("pkg-b", result[0].Package);
    }

    [TestMethod]
    public void ResolveWinningDebs_AllDisabled_ReturnsEmpty()
    {
        var repo = MakeRepo(1, "focal");
        var debs = new List<ApkgDebPackage>
        {
            MakeDeb(1, "pkg-a", "1.0.0", "amd64", 1, repo, isEnabled: false),
            MakeDeb(2, "pkg-a", "1.0.0", "arm64", 1, repo, isEnabled: false),
        };

        var result = _sut.ResolveWinningDebs(debs);

        Assert.AreEqual(0, result.Count,
            "When all debs are disabled (only revision unlisted), ResolveWinningDebs must return empty — nothing goes to the repository.");
    }

    [TestMethod]
    public void ResolveWinningDebs_DisabledNewerVersion_DoesNotOverrideEnabledOldVersion()
    {
        var repo = MakeRepo(1, "focal");
        var debs = new List<ApkgDebPackage>
        {
            MakeDeb(1, "pkg-a", "1.0.0", "amd64", 1, repo, isEnabled: true),
            MakeDeb(2, "pkg-a", "2.0.0", "amd64", 1, repo, isEnabled: false),
        };

        var result = _sut.ResolveWinningDebs(debs);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("1.0.0", result[0].Version,
            "Disabled newer version must not override the enabled older version. Only enabled debs compete.");
    }

    [TestMethod]
    public void ResolveWinningDebs_RelistRestoresEligibility()
    {
        var repo = MakeRepo(1, "focal");
        // Simulate: upload, unlist, relist
        var debs = new List<ApkgDebPackage>
        {
            MakeDeb(1, "pkg-a", "1.0.0", "amd64", 1, repo, isEnabled: false),
            MakeDeb(2, "pkg-a", "1.0.0", "arm64", 1, repo, isEnabled: false),
        };

        // After unlist: nothing wins
        var unlisted = _sut.ResolveWinningDebs(debs);
        Assert.AreEqual(0, unlisted.Count);

        // After relist: set IsEnabled = true on all
        foreach (var d in debs) d.IsEnabled = true;
        var relisted = _sut.ResolveWinningDebs(debs);
        Assert.AreEqual(2, relisted.Count,
            "After relist (all IsEnabled = true), all debs should be eligible again.");
    }

    [TestMethod]
    public void ResolveWinningDebs_EmptyList_ReturnsEmpty()
    {
        var result = _sut.ResolveWinningDebs([]);
        Assert.AreEqual(0, result.Count);
    }
}
