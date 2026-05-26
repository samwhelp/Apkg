using System.Net;
using System.Text.Json;
using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Integration tests for GET /api/sources/{id}.
/// The endpoint is [AllowAnonymous] and returns a JSON source-config object used by the
/// APKG CLI to configure an APT source on the local machine.
/// </summary>
[TestClass]
public class ApiSourcesControllerTests : TestBase
{
    private ApkgDbContext _db = null!;
    private AptRepository _repo = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        _db = GetService<ApkgDbContext>();
        _repo = _db.AptRepositories.First();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Anonymous access
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetSourceConfig_Anonymous_ExistingRepo_Returns200()
    {
        // No auth — endpoint is [AllowAnonymous]
        var response = await Http.GetAsync($"/api/sources/{_repo.Id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Anonymous requests to GET /api/sources/{id} must be allowed.");
    }

    [TestMethod]
    public async Task GetSourceConfig_NonExistentId_Returns404()
    {
        var response = await Http.GetAsync("/api/sources/999999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "A repo ID that does not exist must return 404.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // JSON field correctness
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetSourceConfig_ExistingRepo_JsonContainsExpectedFields()
    {
        var response = await Http.GetAsync($"/api/sources/{_repo.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual(_repo.Id, root.GetProperty("id").GetInt32(), "id must match repository ID");
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("name").GetString()), "name must not be blank");
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("distro").GetString()), "distro must not be blank");
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("suite").GetString()), "suite must not be blank");
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("components").GetString()), "components must not be blank");
        Assert.IsTrue(root.TryGetProperty("aptBaseUrl", out var aptBaseUrl), "aptBaseUrl field must exist");
        Assert.IsTrue(aptBaseUrl.GetString()!.StartsWith("http"), "aptBaseUrl must be an absolute URL");
        Assert.IsTrue(root.TryGetProperty("sourcesFileName", out var sf), "sourcesFileName must exist");
        Assert.AreEqual($"apkg-{_repo.Id}.sources", sf.GetString(), "sourcesFileName must follow the apkg-{id}.sources convention");
    }

    [TestMethod]
    public async Task GetSourceConfig_RepoWithGpgSign_HasKeyUrlAndKeyFileName()
    {
        _repo.EnableGpgSign = true;
        var cert = _db.AptCertificates.FirstOrDefault();
        if (cert != null) _repo.CertificateId = cert.Id;
        _db.SaveChanges();

        var response = await Http.GetAsync($"/api/sources/{_repo.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.GetProperty("enableGpgSign").GetBoolean(), "enableGpgSign must be true");
        if (cert != null)
        {
            var keyUrl = root.GetProperty("keyUrl").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(keyUrl), "keyUrl must be set when GPG signing is enabled");
            Assert.IsTrue(keyUrl!.Contains("/artifacts/certs/"), "keyUrl must point to the cert endpoint");

            var keyFileName = root.GetProperty("keyFileName").GetString();
            Assert.IsTrue(keyFileName!.EndsWith("-archive-keyring.gpg"), "keyFileName must end with -archive-keyring.gpg");
        }
    }

    [TestMethod]
    public async Task GetSourceConfig_RepoWithoutGpgSign_KeyFieldsAreNull()
    {
        _repo.EnableGpgSign = false;
        _repo.CertificateId = null;
        _db.SaveChanges();

        var response = await Http.GetAsync($"/api/sources/{_repo.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsFalse(root.GetProperty("enableGpgSign").GetBoolean(), "enableGpgSign must be false");
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("keyUrl").ValueKind, "keyUrl must be null when GPG is disabled");
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("keyFileName").ValueKind, "keyFileName must be null when GPG is disabled");
    }
}
