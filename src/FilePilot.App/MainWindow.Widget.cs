using System.Windows;
using System.Windows.Input;
using FilePilot.App.Widget;

namespace FilePilot.App;

public partial class MainWindow
{
    private bool resident;
    private bool exiting;
    private WidgetSettings settings = new();
    internal WidgetHost? Host { get; set; }
    internal bool IsWorking => Model.IsBusy;
    internal bool IsDialogOpen { get; private set; }
    internal void Configure(bool stayResident, WidgetSettings options)
    {
        resident = stayResident;
        // Keep the visible popup discoverable through Alt+Tab; hiding removes its taskbar button.
        ShowInTaskbar = true;
        settings = options;
        ApplySettings();
    }
    private void ApplySettings()
    {
        Favorites.ItemsSource = settings.FavoriteFolders ?? [];
        if (Favorites.Items.Count > 0) Favorites.SelectedIndex = 0;
        darkTheme = settings.DarkTheme;
        ApplyTheme();
        ShortcutHint.Text = resident ? settings.Shortcut + " · ESC TO HIDE" : "ESC TO CLOSE";
    }
    private void ApplyTheme()
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        { Source = new Uri($"Themes/{(darkTheme ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        ThemeButton.Content = darkTheme ? "_Light" : "_Dark";
    }
    internal void ShowPopup(NativeMethods.Point? cursor = null)
    {
        if (exiting) return;
        PopupPlacement.Place(this, cursor);
        Show();
        Activate();
        PopupPlacement.Place(this, cursor);
        MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
    internal void ExitWidget()
    {
        if (Model.IsBusy) { Model.SetStatus("Wait for the current operation to finish, or cancel conversion before exiting."); ShowPopup(); return; }
        if (IsDialogOpen) return;
        exiting = true;
        Close();
    }
    private void ExitApplication(object sender, RoutedEventArgs e) => ExitWidget();
    private void DismissPopup(object sender, RoutedEventArgs e) => Dismiss();
    private void Dismiss()
    {
        if (IsDialogOpen) return;
        if (resident) Hide(); else Close();
    }
    private void PopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); }
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBlock && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void OpenSettings(object sender, RoutedEventArgs e) => ShowSettings();
    internal void ShowSettings()
    {
        if (Model.IsBusy || IsDialogOpen) return;
        IsDialogOpen = true;
        try
        {
            new SettingsWindow(settings, updated =>
            {
                Host?.SetShortcut(updated.Shortcut);
                try { SettingsStore.Default.Save(updated); }
                catch
                {
                    try { Host?.SetShortcut(settings.Shortcut); }
                    catch (System.ComponentModel.Win32Exception) { Model.SetStatus("Settings could not be saved and the old shortcut could not be restored. Open Settings to choose a shortcut."); }
                    throw;
                }
                settings = updated;
                ApplySettings();
                Model.SetStatus(resident ? "Settings saved. The shortcut is ready." : "Settings saved. Launch FilePilot without a path to enable the resident shortcut.");
            }) { Owner = this }.ShowDialog();
        }
        finally { IsDialogOpen = false; }
    }
    private bool? ShowPicker(Func<bool?> show)
    {
        IsDialogOpen = true;
        try { return show(); }
        finally { IsDialogOpen = false; }
    }
    private async void RenameFile(object sender, RoutedEventArgs e) => await Model.RenameAsync();
    private void FavoriteOutput(object sender, RoutedEventArgs e)
    {
        if (Favorites.SelectedItem is string folder) Model.SetDestinationFolder(folder);
        else Model.SetStatus("Add a favorite folder in Settings first.");
    }
    private async void FavoriteMove(object sender, RoutedEventArgs e)
    {
        if (!Model.CanMove) return;
        if (Favorites.SelectedItem is not string folder) { Model.SetStatus("Add a favorite folder in Settings first."); return; }
        IsDialogOpen = true;
        bool confirmed;
        try { confirmed = MessageBox.Show(this, $"Move {Model.FileName} to:\n{folder}?", "Move file", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK; }
        finally { IsDialogOpen = false; }
        if (confirmed) await Model.MoveAsync(folder);
    }
}
