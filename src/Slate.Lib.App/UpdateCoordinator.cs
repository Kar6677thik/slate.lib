using Slate.Lib.Core;

namespace Slate.Lib.App;

public static class UpdateCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task CheckAsync(Page page, LibraryApiClient api, bool manual, CancellationToken token = default)
    {
        if (!manual && DateTimeOffset.TryParse(Preferences.Get("last-update-check", ""), out var checkedAt) &&
            DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromHours(24)) return;
        if (!await Gate.WaitAsync(0, token)) return;
        try
        {
            var platform = DeviceInfo.Platform == DevicePlatform.Android ? "android" : "windows";
            var current = AppInfo.Current.VersionString;
            var update = await api.CheckForUpdateAsync(platform, current, token);
            Preferences.Set("last-update-check", DateTimeOffset.UtcNow.ToString("O"));
            if (!update.UpdateAvailable)
            {
                if (manual) await SlateDialogs.AlertAsync(page, "Slate is current", $"Version {current} is installed.", "OK");
                return;
            }
            if (!manual && Preferences.Get("dismissed-update", "") == update.LatestVersion) return;
            var install = await SlateDialogs.AlertAsync(page, $"Slate {update.LatestVersion} is available",
                update.ReleaseNotes + "\n\nThe package checksum will be verified before the normal system installer opens.", "Update", "Later");
            if (!install) { Preferences.Set("dismissed-update", update.LatestVersion); return; }
            var directory = Path.Combine(FileSystem.CacheDirectory, "slate.lib", "updates", update.LatestVersion);
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, update.Artifact.FileName);
            await api.DownloadReleaseAsync(update, destination, token);
            await Launcher.Default.OpenAsync(new OpenFileRequest(update.Artifact.FileName, new ReadOnlyFile(destination)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (manual) await SlateDialogs.AlertAsync(page, "Update check unavailable", exception is HttpRequestException
                ? "Slate is still usable. The release service could not be reached." : exception.Message, "OK");
        }
        finally { Gate.Release(); }
    }
}
