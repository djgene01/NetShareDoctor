using System.IO;
using System.Text.Json;
using NetShareDoctor.Models;

namespace NetShareDoctor.Services;

/// <summary>Saved connections live in %AppData%\NetShareDoctor\connections.json. Passwords never go here.</summary>
public static class ConnectionStore
{
    public static string FilePath { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetShareDoctor", "connections.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Raised after every save, so pages showing the list can reload it.</summary>
    public static event Action? Changed;

    public static List<SavedConnection> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<SavedConnection>>(File.ReadAllText(FilePath), Options) ?? [];
        }
        catch { return []; }
    }

    public static void Save(IEnumerable<SavedConnection> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(items.ToList(), Options));
        File.Move(tmp, FilePath, overwrite: true);
        Changed?.Invoke();
    }
}
