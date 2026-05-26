using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Integration tests for POST /api/packages/apkg-upload.
///
/// This endpoint accepts a full .apkg bundle (tar.gz containing manifest.xml + .deb files)
/// uploaded by an authenticated client (typically the APKG CLI).
/// Tests cover authentication, missing-file, and invalid-payload paths.
/// </summary>
[TestClass]
public class ApiPackagesApkgUploadTests : TestBase
{
    private ApkgDbContext _db = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        _db = GetService<ApkgDbContext>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────────────────────────

    private async Task<string> CreateApiKeyAsync(bool withManageRepos = false)
    {
        var userManager = GetService<UserManager<User>>();
        var email = $"apkgupload-{Guid.NewGuid():N}@test.com";
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "APKG Upload Test User"
        };
        var result = await userManager.CreateAsync(user, "Test@123456!");
        Assert.IsTrue(result.Succeeded, $"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        if (withManageRepos)
        {
            await userManager.AddClaimAsync(user,
                new Claim(AppPermissions.Type, AppPermissionNames.CanManageRepositories));
            await userManager.AddClaimAsync(user,
                new Claim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories));
        }

        var rawKey = $"apkgkey{Guid.NewGuid():N}";
        _db.UserApiKeys.Add(new UserApiKey
        {
            UserId = user.Id,
            Name = "APKG Upload Test Key",
            KeyHash = ApiKeyAuthenticationHandler.ComputeSha256Hex(rawKey),
            KeyPrefix = rawKey[..8]
        });
        await _db.SaveChangesAsync();
        return rawKey;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Authentication
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ApkgUpload_NoAuth_Returns401()
    {
        using var form = new MultipartFormDataContent();
        var response = await Http.PostAsync("/api/packages/apkg-upload", form);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode,
            "An unauthenticated request to apkg-upload must be rejected with 401.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Payload validation
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ApkgUpload_Authenticated_NoFile_Returns400()
    {
        var rawKey = await CreateApiKeyAsync();

        // POST with authentication but no multipart content at all
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/packages/apkg-upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
        request.Content = new MultipartFormDataContent();

        var response = await Http.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "Uploading with no file attached must return 400.");
    }

    [TestMethod]
    public async Task ApkgUpload_Authenticated_RandomBytes_Returns400()
    {
        var rawKey = await CreateApiKeyAsync();

        // Send random bytes that are not a valid .apkg (gzipped tar)
        var garbage = new byte[128];
        new Random(42).NextBytes(garbage);

        using var fileContent = CreateOctetStreamContent(garbage);
        using var form = new MultipartFormDataContent();
        form.Add(fileContent, "apkg", "garbage.apkg");

        using var request = CreateAuthedUploadRequest(rawKey, form);

        var response = await Http.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "An invalid (non-tar.gz) .apkg payload must return 400.");
    }

    [TestMethod]
    public async Task ApkgUpload_Authenticated_EmptyTarGz_Returns400()
    {
        var rawKey = await CreateApiKeyAsync();

        // Create a minimal valid gzip stream (empty tar archive: 2 × 512-byte EOF blocks)
        var emptyTarGz = CreateEmptyTarGz();
        using var fileContent = CreateOctetStreamContent(emptyTarGz);
        using var form = new MultipartFormDataContent();
        form.Add(fileContent, "apkg", "empty.apkg");

        using var request = CreateAuthedUploadRequest(rawKey, form);

        var response = await Http.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "An .apkg archive with no manifest.xml must return 400.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal gzip-compressed tar archive that contains no entries
    /// (two 512-byte zero EOF blocks, which is the standard tar end-of-archive marker).
    /// </summary>
    private static byte[] CreateEmptyTarGz()
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            // Standard tar EOF: two 512-byte blocks of zeros
            gz.Write(new byte[1024]);
        }
        return ms.ToArray();
    }

    private static ByteArrayContent CreateOctetStreamContent(byte[] data)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static HttpRequestMessage CreateAuthedUploadRequest(string apiKey, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/packages/apkg-upload")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }
}
