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
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    var resources = new ResourceDictionary
                    {
                        Source = new Uri($"/FilePilot;component/Themes/{theme}.xaml", UriKind.Relative)
                    };
                    Assert.True(resources.Contains("TextBrush"));
                    window.Resources.MergedDictionaries.Clear();
                    window.Resources.MergedDictionaries.Add(resources);
                    window.Measure(new Size(540, 460));
                    window.Arrange(new Rect(0, 0, 540, 460));
                }
                Assert.IsType<MainViewModel>(window.DataContext);
                Assert.Single(window.InputBindings);
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

