using System.Windows;

namespace FilePilot.App;

// One cached dictionary per palette; the application styles remain in the root dictionary.
internal sealed class ThemeResources(ResourceDictionary resources)
{
    private readonly Dictionary<string, ResourceDictionary> palettes = new();
    internal string? Current { get; private set; }

    internal void Apply(bool dark, bool highContrast)
    {
        var name = highContrast ? "HighContrast" : dark ? "Dark" : "Light";
        if (Current == name) return;
        if (!palettes.TryGetValue(name, out var palette))
        {
            palette = new ResourceDictionary
            { Source = new Uri($"/FilePilot;component/Themes/{name}.xaml", UriKind.Relative) };
            palettes.Add(name, palette);
        }
        resources.MergedDictionaries[0] = palette;
        Current = name;
    }
}
