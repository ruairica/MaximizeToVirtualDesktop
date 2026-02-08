using System.Diagnostics;
using System.Text.Json;

namespace MaximizeToVirtualDesktop;

/// <summary>
/// Persists tracker state to disk so orphaned virtual desktops can be cleaned up
/// after a crash or forced kill. File lives in %LOCALAPPDATA%\MaximizeToVirtualDesktop.
/// </summary>
internal static class TrackerPersistence
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MaximizeToVirtualDesktop", "tracker.json");

    private static readonly object _fileLock = new();

    internal record PersistedEntry(Guid TempDesktopId, string? ProcessName, DateTime CreatedAt);

    public static void Save(List<PersistedEntry> entries)
    {
        lock (_fileLock)
        {
            try
            {
                if (entries.Count == 0)
                {
                    Delete();
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var json = JsonSerializer.Serialize(entries);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"TrackerPersistence: Save failed: {ex.Message}");
            }
        }
    }

    public static List<PersistedEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<PersistedEntry>>(json) ?? [];
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TrackerPersistence: Load failed: {ex.Message}");
            return [];
        }
    }

    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch { }
    }
}
