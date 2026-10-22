using System.Runtime.ExceptionServices;
using System.Windows;
using FilePilot.App;
using Xunit;

namespace FilePilot.Tests;

public sealed class WindowTests
{
    [Fact]
    public void WindowAndBothThemeDictionariesLoadOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new FilePilot.App.App();
                application.InitializeComponent();
                var window = new MainWindow();
                var fixture = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilePilot-layout-" + Guid.NewGuid().ToString("N") + ".png");
                System.IO.File.WriteAllText(fixture, "Metadata-only selection for layout test.");
                try { ((MainViewModel)window.DataContext).LoadFile(fixture); }
                finally { System.IO.File.Delete(fixture); }
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    var resources = new ResourceDictionary
                    {
                        Source = new Uri($"/FilePilot;component/Themes/{theme}.xaml", UriKind.Relative)
                    };
                    Assert.True(resources.Contains("TextBrush"));
                    window.Resources.MergedDictionaries.Clear();
                    window.Resources.MergedDictionaries.Add(resources);
                    window.Measure(new Size(480, 640));
                    window.Arrange(new Rect(0, 0, 480, 640));
                }
                Assert.IsType<MainViewModel>(window.DataContext);
                Assert.Single(window.InputBindings);
                Assert.Single(window.CommandBindings);
                Assert.Equal(System.Windows.Input.ApplicationCommands.Open, ((System.Windows.Input.CommandBinding)window.CommandBindings[0]).Command);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                // Exercise the native HWND/DPI path: layout-only tests cannot catch height replacement.
                window.ShowPopup();
                Assert.True(window.IsVisible);
                Assert.Equal(640, window.Height);
                ((MainViewModel)window.DataContext).ClearSelection("Choose files");
                window.ShowPopup();
                Assert.Equal(420, window.Height);
                var docFixture = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilePilot-doc-layout-" + Guid.NewGuid().ToString("N") + ".docx");
                System.IO.File.WriteAllText(docFixture, "Metadata-only document fixture");
                try
                {
                    var model = (MainViewModel)window.DataContext;
                    model.LoadFile(docFixture);
                    Assert.True(model.IsDocumentSelected);
                    Assert.False(model.CanConvertDocument);
                    window.ShowPopup();
                    Assert.Equal(640, window.Height);
                }
                finally { System.IO.File.Delete(docFixture); }
                var settings = new SettingsWindow(new(), _ => { });
                settings.Measure(new Size(460, 520));
                settings.Arrange(new Rect(0, 0, 460, 520));
                var settingsLayout = Assert.IsType<System.Windows.Controls.DockPanel>(settings.Content);
                var settingsFooter = Assert.IsType<System.Windows.Controls.StackPanel>(settingsLayout.Children[0]);
                Assert.Equal(System.Windows.Controls.Dock.Bottom, System.Windows.Controls.DockPanel.GetDock(settingsFooter));
                var settingsActions = Assert.IsType<System.Windows.Controls.DockPanel>(settingsFooter.Children[1]);
                Assert.Contains(settingsActions.Children.Cast<System.Windows.UIElement>(), child => child is System.Windows.Controls.Button { IsCancel: true });
                Assert.Contains(settingsActions.Children.Cast<System.Windows.UIElement>(), child => child is System.Windows.Controls.Button { IsDefault: true });
                settings.Close();
                window.Close();
                application.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF window loading timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
