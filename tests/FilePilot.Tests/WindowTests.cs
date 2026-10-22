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
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                // Exercise the native HWND/DPI path: layout-only tests cannot catch height replacement.
                window.ShowPopup();
                Assert.True(window.IsVisible);
                Assert.Equal(640, window.Height);
                ((MainViewModel)window.DataContext).ClearSelection("Choose files");
                window.ShowPopup();
                Assert.Equal(420, window.Height);
                var settings = new SettingsWindow(new(), _ => { });
                settings.Measure(new Size(460, 520));
                settings.Arrange(new Rect(0, 0, 460, 520));
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
