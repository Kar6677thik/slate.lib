using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Slate.Lib.Core;

namespace Slate.Lib.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain")]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "image/*")]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "application/pdf")]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "application/octet-stream")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        StageShare(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is null) return;
        Intent = intent;
        StageShare(intent);
    }

    private void StageShare(Intent? intent)
    {
        if (intent?.Action != Android.Content.Intent.ActionSend) return;
        try
        {
            if (string.Equals(intent.Type, "text/plain", StringComparison.OrdinalIgnoreCase))
            {
                var text = intent.GetStringExtra(Android.Content.Intent.ExtraText);
                var payload = SharedCaptureInput.Parse(intent.Action, intent.Type, text);
                IncomingShareStore.SaveAsync(payload).GetAwaiter().GetResult();
            }
            else
            {
#pragma warning disable CS0618
#pragma warning disable CA1422
                var uri = intent.GetParcelableExtra(Android.Content.Intent.ExtraStream) as Android.Net.Uri;
#pragma warning restore CA1422
#pragma warning restore CS0618
                if (uri is null) return;
                var filename = "attachment";
                var resolver = ContentResolver ?? throw new InvalidOperationException("Android content resolver is unavailable.");
                using (var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null))
                    if (cursor?.MoveToFirst() == true)
                    {
                        var index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                        if (index >= 0) filename = cursor.GetString(index) ?? filename;
                    }
                using var stream = resolver.OpenInputStream(uri);
                if (stream is null) return;
                IncomingShareStore.SaveAssetAsync(stream, filename, intent.Type ?? "application/octet-stream").GetAwaiter().GetResult();
            }
            MainThread.BeginInvokeOnMainThread(ShareSignals.Notify);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { }
    }
}
