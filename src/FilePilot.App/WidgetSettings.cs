using System.IO;
using System.Text.Json;

namespace FilePilot.App;

public sealed record WidgetSettings(string Shortcut = "Ctrl+Alt+F", string[]? FavoriteFolders = null, bool DarkTheme = false, string? LibreOfficePath = null);

public sealed class SettingsStore(string path)
{
    public static SettingsStore Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilePilot", "settings.json"));
    public WidgetSettings Load()
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 32768) throw new IOException("FilePilot settings exceed the size limit.");
        var settings = JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(path)) ?? throw new IOException("FilePilot settings are empty.");
        Validate(settings);
        return settings;
    }
    public void Save(WidgetSettings settings)
    {
        Validate(settings);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 32768) throw new ArgumentException("Favorite settings exceed the size limit. Use fewer or shorter paths.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void Validate(WidgetSettings settings)
    {
        HotkeyGesture.Parse(settings.Shortcut);
        if (settings.LibreOfficePath is not null && (settings.LibreOfficePath.Length > 1024 || !Path.IsPathFullyQualified(settings.LibreOfficePath) || !Path.GetFileName(settings.LibreOfficePath).Equals("soffice.com", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose a full path to a trusted LibreOffice program/soffice.com, or clear the optional engine.");
        if ((settings.FavoriteFolders?.Length ?? 0) > 20 || (settings.FavoriteFolders ?? []).Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 1024))
            throw new ArgumentException("Keep at most 20 favorite folders, each with a valid path.");
    }
}

public sealed record HotkeyGesture(uint Modifiers, uint Key)
{
    public static HotkeyGesture Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter a shortcut such as Ctrl+Alt+F.");
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        foreach (var part in parts.SkipLast(1))
        {
            uint flag = part.ToUpperInvariant() switch { "CTRL" => 2, "ALT" => 1, "SHIFT" => 4, _ => 0 };
            if (flag == 0 || (modifiers & flag) != 0) throw new ArgumentException("Use Ctrl, Alt, and optionally Shift, once each.");
            modifiers |= flag;
        }
        if ((modifiers & 3) == 0) throw new ArgumentException("The shortcut must include Ctrl or Alt.");
        var key = parts[^1].ToUpperInvariant();
        uint code = key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) ? key[0] :
            key.StartsWith('F') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 11 ? (uint)(111 + f) : 0;
        if (code == 0) throw new ArgumentException("Use a letter, digit, or F1–F11 with Ctrl or Alt. F12 is reserved by Windows.");
        return new(modifiers, code);
    }
}
