using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FilePilot.App;

internal sealed class SettingsWindow : Window
{
    internal SettingsWindow(WidgetSettings settings, Action<WidgetSettings> save)
    {
        Title = "FilePilot settings";
        Width = 460; Height = 520; MinWidth = 400; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Widget shortcut", FontSize = 20, Margin = new Thickness(0, 0, 0, 10) });
        var shortcut = new TextBox { Text = settings.Shortcut };
        System.Windows.Automation.AutomationProperties.SetName(shortcut, "Global keyboard shortcut");
        panel.Children.Add(shortcut);
        panel.Children.Add(new TextBlock { Text = "Example: Ctrl+Alt+F. Requires Ctrl or Alt.\nThe app must be running for the shortcut to work.", Margin = new Thickness(0, 6, 0, 12) });
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
        panel.Children.Add(status);
        var apply = new Button { Content = "_Save settings", IsDefault = true };
        panel.Children.Add(apply);
        apply.Click += (_, _) =>
        {
            try { save(new(shortcut.Text.Trim(), favorites.ToArray(), dark.IsChecked == true)); DialogResult = true; }
            catch (Exception ex) when (MainViewModel.IsExpected(ex) || ex is System.ComponentModel.Win32Exception)
            { status.Text = ex.Message; }
        };
    }
}
