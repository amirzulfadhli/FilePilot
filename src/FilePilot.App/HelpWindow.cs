using System.Windows;
using System.Windows.Controls;

namespace FilePilot.App;

internal sealed class HelpWindow : Window
{
    internal HelpWindow(string shortcut, bool resident)
    {
        Title = "FilePilot help";
        Width = 420; Height = 460; MinWidth = 320; MinHeight = 280;
        FontSize = 14; UseLayoutRounding = true; SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var layout = new DockPanel { Margin = new Thickness(20) };
        Content = layout;
        var close = new Button { Content = "_Close help", IsCancel = true, IsDefault = true, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);
        var content = new StackPanel();
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true };
        scroll.SetResourceReference(FocusVisualStyleProperty, "HelpFocusVisual");
        System.Windows.Automation.AutomationProperties.SetName(scroll, "FilePilot quick start and keyboard shortcuts");
        layout.Children.Add(scroll);
        Add("Quick file actions", true);
        Add(resident ? $"Select files in Explorer, then press {shortcut}. If a conversion is running, the shortcut reopens it without changing the selection."
            : "This popup closes after use. Launch FilePilot without a file path to keep its shortcut available in the notification area.");
        Add("Keyboard shortcuts", true);
        Add("Ctrl+O — choose files when Explorer selection is unavailable.\nCtrl+Shift+C — copy selected absolute paths.\nTab / Shift+Tab — move between controls.\nF1 — open this help.\nEscape — close an open list first, then hide the widget or close a one-shot popup.");
        Add("Safe conversions", true);
        Add("PNG, JPEG and WebP images can be converted in batches of up to 100. Choose a destination and format; JPEG uses a white background unless you change it. Originals stay unchanged and existing output files are never replaced.");
        Add("DOCX → PDF needs a trusted local LibreOffice engine selected in Settings. Export is single-document only. Check the resulting layout; personal-property removal is not complete anonymization.");
        Add("Move, rename and favorites", true);
        Add("Move and rename act on one selected file. Rename keeps the extension unless you explicitly change it. Add favorite folders and change the shortcut in Settings.");
        Add("While work is running", true);
        Add("Use Cancel for conversion and wait for safe cleanup. " +
            (resident ? "Hiding the widget keeps work running; reopen it with the shortcut or notification icon. "
                : "This one-shot popup stays open until work finishes. Use the taskbar to restore it if minimized. ") +
            "Finish or cancel operations before exiting. All file processing stays local and offline.");
        Loaded += (_, _) => { Widget.PopupPlacement.Place(this); scroll.Focus(); };

        void Add(string text, bool heading = false) => content.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap,
            FontSize = heading ? 18 : 14,
            FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, heading ? 12 : 6, 0, 0)
        });
    }
}
