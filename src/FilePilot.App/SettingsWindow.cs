using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FilePilot.App;

internal sealed class SettingsWindow : Window
{
    internal SettingsWindow(WidgetSettings settings, Action<WidgetSettings> save)
    {
        Title = "FilePilot settings";
        Width = 460; Height = 520; MinWidth = 320; MinHeight = 280; FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var layout = new DockPanel { LastChildFill = true };
        Content = layout;
        var footer = new StackPanel { Margin = new Thickness(20, 8, 20, 16) };
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        var panel = new StackPanel { Margin = new Thickness(20, 20, 20, 8) };
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = "Widget shortcut", FontSize = 20, Margin = new Thickness(0, 0, 0, 10) });
        var shortcut = new TextBox { Text = settings.Shortcut };
        System.Windows.Automation.AutomationProperties.SetName(shortcut, "Global keyboard shortcut");
        panel.Children.Add(shortcut);
        panel.Children.Add(new TextBlock { Text = "Example: Ctrl+Alt+F. Requires Ctrl or Alt.\nThe app must be running for the shortcut to work.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        Loaded += (_, _) => { Widget.PopupPlacement.Place(this); shortcut.Focus(); shortcut.SelectAll(); };
        panel.Children.Add(new TextBlock { Text = "Favorite folders (up to 20)", Margin = new Thickness(0, 0, 0, 6) });
        var favorites = new System.Collections.ObjectModel.ObservableCollection<string>(settings.FavoriteFolders ?? []);
        var list = new ListBox { ItemsSource = favorites, Height = 130 };
        list.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        list.SetResourceReference(ForegroundProperty, "TextBrush");
        System.Windows.Automation.AutomationProperties.SetName(list, "Favorite folders");
        panel.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(buttons);
        var add = new Button { Content = "_Add folder…" };
        var remove = new Button { Content = "_Remove", Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(add); buttons.Children.Add(remove);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        add.Click += (_, _) =>
        {
            if (favorites.Count >= 20) { status.Text = "The limit is 20 favorites."; return; }
            var picker = new OpenFolderDialog { Title = "Add favorite folder" };
            if (picker.ShowDialog(this) != true) return;
            try
            {
                var folder = FilePilot.Core.LocalPathPolicy.Validate(picker.FolderName);
                if (!Directory.Exists(folder)) throw new ArgumentException("Choose an existing folder.");
                if (!favorites.Contains(folder, StringComparer.OrdinalIgnoreCase)) favorites.Add(folder);
            }
            catch (Exception ex) when (MainViewModel.IsExpected(ex)) { status.Text = ex.Message; }
        };
        remove.Click += (_, _) => { if (list.SelectedItem is string folder) favorites.Remove(folder); };
        var dark = new CheckBox { Content = "Dark theme", IsChecked = settings.DarkTheme };
        dark.SetResourceReference(ForegroundProperty, "TextBrush");
        panel.Children.Add(dark);
        panel.Children.Add(new TextBlock { Text = "Optional DOCX → PDF engine", FontSize = 16, Margin = new Thickness(0, 14, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "Select program/soffice.com from a trusted LibreOffice installation. The engine stays separate from FilePilot.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        var enginePath = new TextBox { Text = settings.LibreOfficePath ?? "", MaxLength = 1024 };
        System.Windows.Automation.AutomationProperties.SetName(enginePath, "LibreOffice executable path");
        panel.Children.Add(enginePath);
        var engineButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var chooseEngine = new Button { Content = "Choose engine…" };
        var clearEngine = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0) };
        engineButtons.Children.Add(chooseEngine); engineButtons.Children.Add(clearEngine); panel.Children.Add(engineButtons);
        chooseEngine.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Choose LibreOffice program/soffice.com", Filter = "LibreOffice CLI (soffice.com)|soffice.com", CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            try { enginePath.Text = FilePilot.Core.Documents.LibreOfficePdfEngine.ValidateExecutable(picker.FileName); }
            catch (Exception ex) when (MainViewModel.IsExpected(ex)) { status.Text = ex.Message; }
        };
        clearEngine.Click += (_, _) => enginePath.Clear();
        footer.Children.Add(new ScrollViewer { Content = status, MaxHeight = 72, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var actions = new DockPanel { LastChildFill = true };
        footer.Children.Add(actions);
        var cancel = new Button { Content = "_Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(cancel, Dock.Right);
        actions.Children.Add(cancel);
        var apply = new Button { Content = "_Save settings", IsDefault = true };
        actions.Children.Add(apply);
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "beta";
        footer.Children.Add(new TextBlock { Text = $"FilePilot {version} · Local and offline", FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
        apply.Click += (_, _) =>
        {
            try { save(new(shortcut.Text.Trim(), favorites.ToArray(), dark.IsChecked == true, string.IsNullOrWhiteSpace(enginePath.Text) ? null : enginePath.Text.Trim())); DialogResult = true; }
            catch (Exception ex) when (MainViewModel.IsExpected(ex) || ex is System.ComponentModel.Win32Exception)
            { status.Text = ex.Message; }
        };
    }
}
