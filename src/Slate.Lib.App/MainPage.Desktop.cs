using Microsoft.Maui.Controls.Shapes;
namespace Slate.Lib.App;

public partial class MainPage
{
#if WINDOWS
    private void RegisterDesktopShortcuts(Microsoft.UI.Xaml.UIElement host)
    {
        host.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        // XAML accelerators also run when the WebView owns keyboard focus.
        void Add(Windows.System.VirtualKey key, string command, bool shift = false)
        {
            var shortcut = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = key,
                Modifiers = Windows.System.VirtualKeyModifiers.Control |
                    (shift ? Windows.System.VirtualKeyModifiers.Shift : Windows.System.VirtualKeyModifiers.None)
            };
            shortcut.Invoked += (_, args) =>
            {
                args.Handled = true;
                Dispatcher.Dispatch(async () =>
                {
                    try { await HandleReaderCommandAsync(command); }
                    catch (Exception exception) { ShowOperationError(exception, "The command could not be completed."); }
                });
            };
            host.KeyboardAccelerators.Add(shortcut);
        }
        Add(Windows.System.VirtualKey.K, "link-or-search");
        Add(Windows.System.VirtualKey.K, "wiki", true);
        Add(Windows.System.VirtualKey.F, "search", true);
        Add(Windows.System.VirtualKey.P, "commands", true);
        Add(Windows.System.VirtualKey.N, "note");
        Add(Windows.System.VirtualKey.N, "folder", true);
        Add(Windows.System.VirtualKey.S, "save");
        Add(Windows.System.VirtualKey.W, "close");
        Add(Windows.System.VirtualKey.E, "mode");
        Add(Windows.System.VirtualKey.Tab, "next-tab");
        Add(Windows.System.VirtualKey.Tab, "previous-tab", true);
    }

    private async Task HandleReaderCommandAsync(string command)
    {
        if (DesktopDialogs.IsOpen(this) || SearchPanel.IsVisible) return;
        switch (command)
        {
            case "link-or-search": if (MarkdownEditor.IsFocused) ApplyMarkdownCommand("link"); else OpenSearch(); break;
            case "wiki": if (currentNote is not null) ApplyMarkdownCommand("wiki"); break;
            case "search": OpenSearch(); break;
            case "commands": CommandPaletteClicked(this, EventArgs.Empty); break;
            case "note": await NewNoteAsync(TargetFolder()); break;
            case "folder": await NewFolderAsync(TargetFolder()); break;
            case "save": await SaveAsync(); break;
            case "close": if (currentNote is not null) await CloseTabAsync(currentNote.Id); break;
            case "mode": SetMode(mode == ViewMode.Preview ? ViewMode.Write : ViewMode.Preview); break;
            case "previous-tab": await CycleTabAsync(-1); break;
            case "next-tab": await CycleTabAsync(1); break;
        }
    }
#endif
    private async Task CycleTabAsync(int direction)
    {
        if (openTabs.Count < 2 || !await ResolveUnsavedAsync()) return;
        var index = openTabs.FindIndex(x => x.Id == currentNote?.Id);
        var next = openTabs[(index + direction + openTabs.Count) % openTabs.Count];
        await OpenNote(token => api.ReadAsync(next.Id, token));
    }

    private async Task OpenSettingsAsync()
    {
#if WINDOWS
        if (DesktopDialogs.IsOpen(this)) return;
        ConnectionPanel.IsVisible = false;
        var server = new Entry { Text = ServerEntry.Text, Placeholder = "https://your-server" };
        var token = new Entry { Text = TokenEntry.Text, Placeholder = "Device token", IsPassword = true };
        var body = new VerticalStackLayout { Spacing = 12 };
        var subtitle = DesktopTheme.Label("Connect your private library", 13); subtitle.TextColor = DesktopTheme.Muted; body.Add(subtitle);
        body.Add(DesktopTheme.Label("Server", 12, true)); body.Add(DesktopTheme.Field(server));
        body.Add(DesktopTheme.Label("Device token", 12, true)); body.Add(DesktopTheme.Field(token));
        var version = DesktopTheme.Label("Slate " + AppInfo.Current.VersionString + "  ·  Windows", 12); version.TextColor = DesktopTheme.Muted;
        body.Add(new BoxView { Color = DesktopTheme.Divider, HeightRequest = 1, Margin = new Thickness(0, 8) });
        body.Add(version);
        var result = await DesktopDialogs.ShowAsync(this, "Settings", "settings_20_regular.png", body, finish =>
        {
            var footer = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 8 };
            var update = DesktopTheme.Button("Check for updates"); update.Clicked += (_, _) => finish("updates");
            var cancel = DesktopTheme.Button("Done"); cancel.Clicked += (_, _) => finish(null);
            var connect = DesktopTheme.Button("Connect", true); connect.Clicked += (_, _) => finish("connect");
            footer.Add(update); footer.Add(cancel, 1); footer.Add(connect, 2); return footer;
        }, 560);
        if (result == "updates") await UpdateCoordinator.CheckAsync(this, api, true, connection.Token);
        if (result == "connect" && await ResolveUnsavedAsync())
        {
            ServerEntry.Text = server.Text; TokenEntry.Text = token.Text;
            await ConnectAsync(true);
        }
#else
        await Task.CompletedTask;
        ConnectionPanel.IsVisible = !ConnectionPanel.IsVisible;
#endif
    }

    private void DismissFeedbackClicked(object? sender, EventArgs e) => FeedbackBanner.IsVisible = false;

    private void UpdateNavigationControls()
    {
        BackButton.IsEnabled = navigationIndex > 0;
        ForwardButton.IsEnabled = navigationIndex >= 0 && navigationIndex < navigationHistory.Count - 1;
    }
}

