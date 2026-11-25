using System.Text.Json;

namespace ScreenCheat;

/// <summary>User settings, stored in %AppData%\ScreenCheat\settings.json.</summary>
internal sealed class Settings
{
    // Relaxed escaping keeps "Ctrl+Alt+F9" readable for hand editing instead of "Ctrl+Alt+F9".
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Toggle hotkey, e.g. "Ctrl+Alt+F9".</summary>
    public string Hotkey { get; set; } = KeyCombo.Default.ToString();

    /// <summary>Device names of the monitors to cover (\\.\DISPLAY1); empty means all of them.</summary>
    public List<string> Monitors { get; set; } = [];

    /// <summary>Turn protection on as soon as the app starts (overridden by --on / --off).</summary>
    public bool EnableOnStart { get; set; } = true;

    /// <summary>Draw the frame and badge that only the user can see while protection is on.</summary>
    public bool ShowIndicator { get; set; } = true;

    /// <summary>Show the hacker desktop pet while protection is on.</summary>
    public bool ShowPet { get; set; } = true;

    /// <summary>Where the user last dropped the pet (top-left, physical pixels); null = bottom-right of the primary screen.</summary>
    public int? PetX { get; set; }

    public int? PetY { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenCheat", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable settings file falls back to defaults rather than blocking startup.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings still apply for this run; they are just not remembered.
        }
    }
}
