using System.Reflection;
using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed record BackupStatus(string State, DateTimeOffset? CompletedAt, string? Snapshot, string? Detail = null);

public sealed class OperationalHealth
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string libraryRoot;
    private readonly string assetsRoot;
    private readonly string authenticationFile;
    private readonly string backupStatusFile;
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public string Version { get; } = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "Unknown";

    public OperationalHealth(string libraryRoot, string assetsRoot, string authenticationFile, string backupStatusFile)
    {
        this.libraryRoot = Path.GetFullPath(libraryRoot);
        this.assetsRoot = Path.GetFullPath(assetsRoot);
        this.authenticationFile = Path.GetFullPath(authenticationFile);
        this.backupStatusFile = Path.GetFullPath(backupStatusFile);
    }

    public (bool Ready, string State) Readiness()
    {
        try
        {
            var identity = Path.Combine(libraryRoot, ".slate", "library.json");
            if (!File.Exists(identity) || new FileInfo(identity).Length == 0) return (false, "Library identity is unavailable.");
            if (!File.Exists(authenticationFile)) return (false, "Authentication state is unavailable.");
            ProbeWritable(libraryRoot);
            ProbeWritable(assetsRoot);
            return (true, "Ready");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (false, "Durable storage is unavailable: " + exception.Message);
        }
    }

    public BackupStatus? ReadBackupStatus()
    {
        try
        {
            if (!File.Exists(backupStatusFile)) return null;
            return JsonSerializer.Deserialize<BackupStatus>(File.ReadAllText(backupStatusFile), Json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new("Invalid", null, null, exception.Message); }
    }

    private static void ProbeWritable(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        var probe = Path.Combine(path, ".slate-health-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
            stream.WriteByte(1); stream.Flush(true);
        }
        finally { if (File.Exists(probe)) File.Delete(probe); }
    }
}
