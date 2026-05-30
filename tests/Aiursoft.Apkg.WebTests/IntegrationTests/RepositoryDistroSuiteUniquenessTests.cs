using System.Net;
using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Tests that enforce (Distro, Suite) uniqueness on AptRepository.
/// APT identifies a repo by (base URL, distro, suite) — two repos with the same
/// (Distro, Suite) share the same InRelease URL and collide. The controller must
/// reject Create/Edit that would produce a collision.
/// </summary>
[TestClass]
public class RepositoryDistroSuiteUniquenessTests : TestBase
{
    private ApkgDbContext _db = null!;
    private AptRepository _repo = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        await LoginAsAdmin();

        _db = GetService<ApkgDbContext>();
        _repo = _db.AptRepositories.First();
    }

    /// <summary>
    /// Helper: POST to /Repositories/Create with the given form values.
    /// </summary>
    private Task<HttpResponseMessage> PostCreateRepo(
        string distro,
        string name,
        string suite,
        string components = "main",
        string architecture = "amd64",
        bool enableGpgSign = false)
    {
        return PostForm("/Repositories/Create", new Dictionary<string, string>
        {
            { "Distro", distro },
            { "Name", name },
            { "Suite", suite },
            { "Components", components },
            { "Architecture", architecture },
            { "EnableGpgSign", enableGpgSign.ToString() }
        });
    }

    /// <summary>
    /// Helper: POST to /Repositories/Edit with the given form values.
    /// </summary>
    private Task<HttpResponseMessage> PostEditRepo(
        int id,
        string distro,
        string name,
        string suite,
        string components = "main",
        string architecture = "amd64",
        bool enableGpgSign = false)
    {
        return PostForm("/Repositories/Edit", new Dictionary<string, string>
        {
            { "Id", id.ToString() },
            { "Distro", distro },
            { "Name", name },
            { "Suite", suite },
            { "Components", components },
            { "Architecture", architecture },
            { "EnableGpgSign", enableGpgSign.ToString() }
        }, tokenUrl: $"/Repositories/Edit/{id}");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Create — duplicate (Distro, Suite) rejected
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Create_DuplicateDistroSuite_SameArch_Rejected()
    {
        // The seed creates a repo with Distro="anduinos", Suite="questing".
        // Try to create another one with the exact same Distro+Suite.
        var response = await PostCreateRepo(
            distro: _repo.Distro,
            name: "Duplicate Repo Same Arch",
            suite: _repo.Suite,
            architecture: "amd64");

        // Should NOT redirect (form re-renders with error)
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Creating a repo with duplicate (Distro, Suite) must return the form with an error, not redirect.");

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("already exists"),
            "Error message about duplicate (Distro, Suite) should appear in the response.");
    }

    [TestMethod]
    public async Task Create_DuplicateDistroSuite_DifferentArch_Rejected()
    {
        // This is the exact bug scenario: same Distro+Suite, different Architecture.
        var response = await PostCreateRepo(
            distro: _repo.Distro,
            name: "ARM64 Duplicate Repo",
            suite: _repo.Suite,
            architecture: "arm64");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Creating a repo with same (Distro, Suite) but different arch must be rejected.");

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("already exists"),
            "Error message should explain the (Distro, Suite) collision.");
        Assert.IsTrue(html.Contains("amd64,arm64") || html.Contains("amd64, arm64"),
            "Error message should suggest using comma-separated architectures.");
    }

    [TestMethod]
    public async Task Create_DuplicateDistroSuite_DifferentComponent_Rejected()
    {
        // Same Distro+Suite, different Component — still collides on InRelease URL.
        var response = await PostCreateRepo(
            distro: _repo.Distro,
            name: "Different Component Repo",
            suite: _repo.Suite,
            components: "contrib");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Same (Distro, Suite) with different Components must also be rejected.");

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("already exists"),
            "Error message should appear for component-only difference too.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Create — allowed cases (different distro or suite)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Create_SameDistroDifferentSuite_Allowed()
    {
        var response = await PostCreateRepo(
            distro: _repo.Distro,
            name: "Different Suite Repo",
            suite: "completely-different-suite");

        // Should redirect to Index (success)
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Same Distro but different Suite should be allowed.");
    }

    [TestMethod]
    public async Task Create_SameSuiteDifferentDistro_Allowed()
    {
        var response = await PostCreateRepo(
            distro: "anduinos-ports",
            name: "Ports Repo",
            suite: _repo.Suite);

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Same Suite but different Distro should be allowed.");
    }

    [TestMethod]
    public async Task Create_BothDifferent_Allowed()
    {
        var response = await PostCreateRepo(
            distro: "debian",
            name: "Debian Bookworm Repo",
            suite: "bookworm");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Completely different Distro and Suite should be allowed.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Edit — changing to a conflicting (Distro, Suite) rejected
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Edit_ChangeToConflictingDistroSuite_Rejected()
    {
        // First, create a second repo with different suite
        var createResponse = await PostCreateRepo(
            distro: _repo.Distro,
            name: "Second Repo",
            suite: "unique-edit-test-suite");
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);

        // Find the new repo
        var secondRepo = _db.AptRepositories.First(r => r.Suite == "unique-edit-test-suite");

        // Now try to edit it to have the same Suite as the original _repo
        var editResponse = await PostEditRepo(
            id: secondRepo.Id,
            distro: _repo.Distro,
            name: secondRepo.Name,
            suite: _repo.Suite);

        Assert.AreEqual(HttpStatusCode.OK, editResponse.StatusCode,
            "Editing a repo to collide with another's (Distro, Suite) must be rejected.");

        var html = await editResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("already exists"),
            "Error message should appear when editing creates a (Distro, Suite) collision.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Edit — keeping own (Distro, Suite) allowed (self-exclusion)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Edit_KeepSameDistroSuite_Allowed()
    {
        // Edit the existing repo, changing only Architecture — should succeed
        // because the check excludes self (r.Id != model.Id).
        var editResponse = await PostEditRepo(
            id: _repo.Id,
            distro: _repo.Distro,
            name: _repo.Name,
            suite: _repo.Suite,
            architecture: "amd64,arm64");

        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode,
            "Editing a repo without changing its (Distro, Suite) must be allowed.");

        // Verify the architecture was actually updated
        _db.Entry(_repo).Reload();
        Assert.AreEqual("amd64,arm64", _repo.Architecture,
            "Architecture should have been updated to amd64,arm64.");
    }

    [TestMethod]
    public async Task Edit_KeepSameDistroSuite_ChangeComponents_Allowed()
    {
        // Edit the existing repo, changing only Components — should succeed.
        var editResponse = await PostEditRepo(
            id: _repo.Id,
            distro: _repo.Distro,
            name: _repo.Name,
            suite: _repo.Suite,
            components: "main,contrib");

        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode,
            "Editing a repo to change Components while keeping (Distro, Suite) must be allowed.");

        _db.Entry(_repo).Reload();
        Assert.AreEqual("main,contrib", _repo.Components,
            "Components should have been updated.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Error message quality
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Create_DuplicateDistroSuite_ErrorMentionsAlternatives()
    {
        var response = await PostCreateRepo(
            distro: _repo.Distro,
            name: "Yet Another Repo",
            suite: _repo.Suite,
            architecture: "arm64");

        var html = await response.Content.ReadAsStringAsync();

        // The error message should mention the InRelease URL collision
        Assert.IsTrue(html.Contains("InRelease"),
            "Error should mention InRelease to explain the collision.");

        // The error message should suggest alternatives
        Assert.IsTrue(html.Contains("anduinos-ports") || html.Contains("different Distro name"),
            "Error should suggest using a different Distro name.");
    }
}
