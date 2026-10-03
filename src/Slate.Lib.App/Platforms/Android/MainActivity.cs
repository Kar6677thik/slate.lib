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
[IntentFilter([Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
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

    private async void StageShare(Intent? intent)
    {
        if (intent?.Action is not (Intent.ActionSend or Intent.ActionSendMultiple)) return;
        try
        {
            var resolver = ContentResolver ?? throw new InvalidOperationException("Android content resolver is unavailable.");
            var uris = new List<Android.Net.Uri>();
#pragma warning disable CS0618
#pragma warning disable CA1422
            if (intent.Action == Intent.ActionSendMultiple)
                foreach (var value in intent.GetParcelableArrayListExtra(Intent.ExtraStream) ?? new System.Collections.ArrayList())
                    if (value is Android.Net.Uri uri && !uris.Any(x => x.ToString() == uri.ToString())) uris.Add(uri);
            else if (intent.GetParcelableExtra(Intent.ExtraStream) is Android.Net.Uri single) uris.Add(single);
#pragma warning restore CA1422
#pragma warning restore CS0618
            if (intent.ClipData is { } clips)
                for (var i = 0; i < clips.ItemCount; i++)
                    if (clips.GetItemAt(i)?.Uri is { } uri && !uris.Any(x => x.ToString() == uri.ToString())) uris.Add(uri);
            if (uris.Count > 8) throw new ArgumentException("Share at most eight files at once.");
            var text = intent.GetStringExtra(Intent.ExtraText);
            SharedCaptureInput? payload = string.IsNullOrWhiteSpace(text) ? null : SharedCaptureInput.Parse(Intent.ActionSend, "text/plain", text);
            if (payload is null && uris.Count == 0) return;
            await Task.Run(async () =>
            {
                var share = await IncomingShareStore.Inbox.BeginAsync(payload);
                try
                {
                    foreach (var uri in uris)
                    {
                        var filename = "attachment";
                        using (var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null))
                            if (cursor?.MoveToFirst() == true && cursor.GetColumnIndex(IOpenableColumns.DisplayName) is var index && index >= 0) filename = cursor.GetString(index) ?? filename;
                        using var stream = resolver.OpenInputStream(uri) ?? throw new IOException("A shared file could not be read.");
                        share = await IncomingShareStore.Inbox.AddFileAsync(share, stream, filename, resolver.GetType(uri) ?? "application/octet-stream");
                    }
                    await IncomingShareStore.Inbox.SaveAsync(share with { Complete = true });
                }
                catch (Exception)
                {
                    await IncomingShareStore.Inbox.SaveAsync(share with { Complete = true, Error = "Some shared files could not be copied. The files already saved remain available; share the missing files again." });
                }
            });
            MainThread.BeginInvokeOnMainThread(ShareSignals.Notify);
        }
        catch (Exception exception)
        {
            Android.Widget.Toast.MakeText(this, "Unable to accept this share: " + exception.Message, Android.Widget.ToastLength.Long)?.Show();
        }
    }
}
