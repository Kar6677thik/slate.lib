using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class DeviceTokenOperationsTests
{
    [Fact]
    public void CreateListAuthenticateRevokeAndRotateAreIndependentAndSecretSafe()
    {
        using var fixture = new TestLibrary();
        var tokens = new DeviceTokens(fixture.DeviceFile);
        var windows = tokens.Create("Windows Laptop");
        var android = tokens.Create("Android Phone");
        Assert.True(tokens.Accepts(windows)); Assert.True(tokens.Accepts(android));
        var listed = tokens.Load();
        Assert.Equal(2, listed.Count); Assert.All(listed, device => Assert.Equal("Active", device.Status));
        var listingJson = JsonSerializer.Serialize(listed);
        Assert.DoesNotContain(windows, listingJson); Assert.DoesNotContain(android, listingJson); Assert.DoesNotContain("tokenHash", listingJson, StringComparison.OrdinalIgnoreCase);

        tokens.Revoke("Android Phone");
        Assert.False(tokens.Accepts(android)); Assert.True(tokens.Accepts(windows));
        var replacement = tokens.Rotate("Android Phone");
        Assert.False(tokens.Accepts(android)); Assert.True(tokens.Accepts(replacement)); Assert.True(tokens.Accepts(windows));
        Assert.Equal(3, tokens.Load().Count);
    }

    [Fact]
    public void LegacyHashFileMigratesWithoutRevealingTokens()
    {
        using var fixture = new TestLibrary();
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        File.WriteAllText(fixture.DeviceFile, JsonSerializer.Serialize(new Dictionary<string, string> { ["Legacy"] = hash }));
        var tokens = new DeviceTokens(fixture.DeviceFile);
        Assert.True(tokens.Accepts(token));
        Assert.Equal("Legacy", Assert.Single(tokens.Load()).Label);
        Assert.DoesNotContain(token, File.ReadAllText(fixture.DeviceFile));
    }
}

public sealed class UpdateLogicTests
{
    [Theory]
    [InlineData("0.4.0", "0.4.1", true)]
    [InlineData("0.4.0", "0.4.0", false)]
    [InlineData("0.5.0", "0.4.9", false)]
    public void ComparesReleaseVersions(string current, string candidate, bool expected) =>
        Assert.Equal(expected, ReleaseVersions.IsNewer(current, candidate));

    [Theory]
    [InlineData("v0.4.0")]
    [InlineData("0.4")]
    [InlineData("0.4.0-beta")]
    [InlineData("latest")]
    public void RejectsMalformedVersions(string version) => Assert.Throws<InvalidDataException>(() => ReleaseVersions.Parse(version));

    [Fact]
    public async Task VerifiesReleaseSizeAndChecksum()
    {
        using var fixture = new TestLibrary();
        var path = Path.Combine(fixture.DerivedRoot, "release.bin"); Directory.CreateDirectory(fixture.DerivedRoot);
        var bytes = "release bytes"u8.ToArray(); await File.WriteAllBytesAsync(path, bytes);
        var artifact = new ReleaseArtifact("release.bin", "/v1/releases/release.bin", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        await ReleaseVersions.VerifyFileAsync(path, artifact);
        await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseVersions.VerifyFileAsync(path, artifact with { Sha256 = new string('0', 64) }));
    }

    [Fact]
    public void RejectsMalformedReleaseManifest()
    {
        using var fixture = new TestLibrary(); Directory.CreateDirectory(fixture.DerivedRoot);
        var manifest = Path.Combine(fixture.DerivedRoot, "manifest.json"); File.WriteAllText(manifest, "{\"schemaVersion\":1,\"latestVersion\":\"bad\"}");
        var catalog = new UpdateCatalog(Options.Create(new UpdateOptions { ManifestPath = manifest, ArtifactRoot = fixture.DerivedRoot }));
        Assert.ThrowsAny<Exception>(() => catalog.ReadManifest());
    }
}

public sealed class Phase3DOperationalApiTests
{
    private sealed class Factory(TestLibrary fixture, string releaseRoot, string manifestPath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Library:RootPath", fixture.Root); builder.UseSetting("Authentication:DeviceFile", fixture.DeviceFile);
            builder.UseSetting("Data:DerivedPath", Path.Combine(fixture.DerivedRoot, "derived")); builder.UseSetting("Data:AssetsPath", Path.Combine(fixture.DerivedRoot, "assets"));
            builder.UseSetting("Git:StatePath", Path.Combine(fixture.DerivedRoot, "state")); builder.UseSetting("Git:SyncIntervalSeconds", "3600");
            builder.UseSetting("Updates:ManifestPath", manifestPath); builder.UseSetting("Updates:ArtifactRoot", releaseRoot);
            builder.UseSetting("Backup:StatusFile", Path.Combine(fixture.DerivedRoot, "state", "backup-status.json"));
        }
    }

    [Fact]
    public async Task LivenessIsAnonymousAndReadinessIgnoresRemoteGitButChecksDurableStorage()
    {
        using var fixture = new TestLibrary(); _ = new DeviceTokens(fixture.DeviceFile).Create("test");
        var releases = Path.Combine(fixture.DerivedRoot, "releases"); Directory.CreateDirectory(releases);
        using var factory = new Factory(fixture, releases, Path.Combine(releases, "release-manifest.json")); using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/ready")).StatusCode);
        Directory.Delete(Path.Combine(fixture.DerivedRoot, "assets"), true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedStatusIsUsefulAndDoesNotLeakSecretsOrPhysicalPaths()
    {
        using var fixture = new TestLibrary(); var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        var releases = Path.Combine(fixture.DerivedRoot, "releases"); Directory.CreateDirectory(releases);
        using var factory = new Factory(fixture, releases, Path.Combine(releases, "release-manifest.json")); using var http = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/status"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request); var json = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("serverVersion", json); Assert.Contains("linkVersion", json);
        Assert.DoesNotContain(token, json); Assert.DoesNotContain(fixture.Root, json); Assert.DoesNotContain(fixture.DerivedRoot, json);
    }

    [Fact]
    public async Task UpdateEndpointDetectsNewerReleaseDownloadsVerifiedBytesAndOutageDoesNotBlockLibrary()
    {
        using var fixture = new TestLibrary(); var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        var releases = Path.Combine(fixture.DerivedRoot, "releases"); Directory.CreateDirectory(releases);
        var windowsBytes = "signed-msix"u8.ToArray(); var androidBytes = "signed-apk"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(releases, "Slate-0.5.0.msix"), windowsBytes);
        await File.WriteAllBytesAsync(Path.Combine(releases, "Slate-0.5.0.apk"), androidBytes);
        var manifestPath = Path.Combine(releases, "release-manifest.json");
        var manifest = new ReleaseManifest(1, "0.5.0", DateTimeOffset.UtcNow, "Release notes",
            Artifact("Slate-0.5.0.msix", windowsBytes), Artifact("Slate-0.5.0.apk", androidBytes));
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var factory = new Factory(fixture, releases, manifestPath); using var http = factory.CreateClient();
        var api = new LibraryApiClient(http); api.Connect(http.BaseAddress!.ToString(), token);
        var update = await api.CheckForUpdateAsync("windows", "0.4.0", default); Assert.True(update.UpdateAvailable);
        Assert.False((await api.CheckForUpdateAsync("windows", "0.5.0", default)).UpdateAvailable);
        Assert.False((await api.CheckForUpdateAsync("windows", "0.6.0", default)).UpdateAvailable);
        var downloaded = Path.Combine(fixture.DerivedRoot, "download.msix"); await api.DownloadReleaseAsync(update, downloaded, default);
        Assert.Equal(windowsBytes, await File.ReadAllBytesAsync(downloaded));
        File.Delete(manifestPath);
        await Assert.ThrowsAsync<HttpRequestException>(() => api.CheckForUpdateAsync("windows", "0.4.0", default));
        Assert.Equal(fixture.NoteId, (await api.ReadAsync(fixture.NoteId, default)).Id);
    }

    private static ReleaseArtifact Artifact(string name, byte[] bytes) =>
        new(name, "/v1/releases/" + Uri.EscapeDataString(name), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
}
