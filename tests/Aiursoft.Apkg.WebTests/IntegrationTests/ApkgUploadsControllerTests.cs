using System.Net;
using Aiursoft.Apkg.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Integration tests for the <see cref="Aiursoft.Apkg.Controllers.ApkgUploadsController"/>.
///
/// Covers: Index, Upload (GET), Details, Unlist, Relist, Delete.
/// The Upload (POST) → Preview → Publish flow requires a real .apkg file and is tested
/// separately via end-to-end scenarios.  These tests verify auth gates, ownership guards,
/// and admin-only restrictions.
/// </summary>
[TestClass]
public class ApkgUploadsControllerTests : TestBase
{
    private ApkgDbContext _db = null!;
    private string _adminUserId = null!;
    private HttpClient _anonHttp = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        await LoginAsAdmin();

        _db = GetService<ApkgDbContext>();

        var userManager = GetService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("admin@default.com");
        _adminUserId = admin!.Id;

        _anonHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = Http.BaseAddress
        };
    }

    public override void CleanTestContext()
    {
        _anonHttp.Dispose();
        base.CleanTestContext();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private ApkgUpload AddApkgUpload(
        string? userId = null,
        bool isPublished = true,
        bool isListed = true,
        string? vaultPath = null)
    {
        var upload = new ApkgUpload
        {
            UploadedByUserId = userId ?? _adminUserId,
            FileName = "test.apkg",
            Package = $"test-pkg-{Guid.NewGuid():N}",
            Version = "1.0.0",
            Component = "main",
            Description = "Test package",
            IsPublished = isPublished,
            IsListed = isListed,
            VaultPath = vaultPath
        };
        _db.ApkgUploads.Add(upload);
        _db.SaveChanges();
        return upload;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Index
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Index_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.GetAsync("/ApkgUploads");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Index must redirect (to login).");
        Assert.IsTrue(
            response.Headers.Location?.OriginalString.Contains("Account/Login", StringComparison.OrdinalIgnoreCase) == true
            || response.Headers.Location?.OriginalString.Contains("login", StringComparison.OrdinalIgnoreCase) == true,
            $"Redirect should point to login, but was: {response.Headers.Location}");
    }

    [TestMethod]
    public async Task Index_Authenticated_Returns200()
    {
        var response = await Http.GetAsync("/ApkgUploads");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "An authenticated user must be able to access the APKG uploads index.");
    }

    [TestMethod]
    public async Task Index_Authenticated_ShowsOwnUploads()
    {
        var upload = AddApkgUpload();
        var response = await Http.GetAsync("/ApkgUploads");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains(upload.Package),
            "Index should list the admin's own packages.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Upload (GET)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UploadGet_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.GetAsync("/ApkgUploads/Upload");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Upload page must redirect to login.");
    }

    [TestMethod]
    public async Task UploadGet_Authenticated_Returns200()
    {
        var response = await Http.GetAsync("/ApkgUploads/Upload");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "An authenticated user must see the Upload page.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Details
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Details_Anonymous_RedirectsToLogin()
    {
        var upload = AddApkgUpload();
        var response = await _anonHttp.GetAsync($"/ApkgUploads/Details/{upload.Id}");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Details must redirect to login.");
    }

    [TestMethod]
    public async Task Details_NonExistentUpload_Returns404()
    {
        var response = await Http.GetAsync("/ApkgUploads/Details/999999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Details for a non-existent upload must return 404.");
    }

    [TestMethod]
    public async Task Details_AdminCanViewAnyUpload_Returns200()
    {
        var upload = AddApkgUpload();
        var response = await Http.GetAsync($"/ApkgUploads/Details/{upload.Id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "An admin must be able to view any upload's Details.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Unlist
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Unlist_Anonymous_RedirectsToLogin()
    {
        var upload = AddApkgUpload();
        var response = await _anonHttp.PostAsync($"/ApkgUploads/Unlist/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous POST to Unlist must redirect to login.");
    }

    [TestMethod]
    public async Task Unlist_NonExistentUpload_Returns404()
    {
        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync("/ApkgUploads/Unlist/999999",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Unlisting a non-existent upload must return 404.");
    }

    [TestMethod]
    public async Task Unlist_AdminCanUnlistOwnUpload_RedirectsToIndex()
    {
        var upload = AddApkgUpload(isPublished: true, isListed: true);

        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync($"/ApkgUploads/Unlist/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        AssertRedirect(response, "/ApkgUploads");

        // Reload from DB and verify
        _db.Entry(upload).Reload();
        Assert.IsFalse(upload.IsListed, "Upload must be unlisted after the action.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Relist (admin-only)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Relist_Anonymous_RedirectsToLogin()
    {
        var upload = AddApkgUpload(isPublished: true, isListed: false);
        var response = await _anonHttp.PostAsync($"/ApkgUploads/Relist/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous relisting must redirect to login.");
    }

    [TestMethod]
    public async Task Relist_Admin_RelistsUploadAndRedirectsToDetails()
    {
        var upload = AddApkgUpload(isPublished: true, isListed: false);

        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync($"/ApkgUploads/Relist/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        AssertRedirect(response, $"/ApkgUploads/Details/{upload.Id}");

        _db.Entry(upload).Reload();
        Assert.IsTrue(upload.IsListed, "Upload must be listed again after Relist.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Delete (admin-only)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Delete_Anonymous_RedirectsToLogin()
    {
        var upload = AddApkgUpload();
        var response = await _anonHttp.PostAsync($"/ApkgUploads/Delete/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous delete must redirect to login.");

        // Verify the record still exists
        var stillExists = _db.ApkgUploads.Any(u => u.Id == upload.Id);
        Assert.IsTrue(stillExists, "Upload must NOT be deleted when the requester is anonymous.");
    }

    [TestMethod]
    public async Task Delete_Admin_DeletesUploadAndRedirectsToIndex()
    {
        var upload = AddApkgUpload();
        var uploadId = upload.Id;

        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync($"/ApkgUploads/Delete/{uploadId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        AssertRedirect(response, "/ApkgUploads");

        var deleted = _db.ApkgUploads.Any(u => u.Id == uploadId);
        Assert.IsFalse(deleted, "Upload must be deleted from the database after admin Delete.");
    }

    [TestMethod]
    public async Task Delete_NonExistentUpload_Returns404()
    {
        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync("/ApkgUploads/Delete/999999",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Deleting a non-existent upload must return 404.");
    }
}
