using System.Text.Json;

namespace Icono.IO;

/// <summary>
/// Loads/saves <see cref="IconLibraryEntry"/> records as individual JSON files in a local folder
/// — the first real content in Icono.IO (previously an empty placeholder project). Each entry is
/// its own file so a corrupt/hand-edited one can't take down the whole library on load.
/// </summary>
public sealed class IconLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _folder;

    public IconLibraryStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Icono", "Library");
    }

    /// <summary>Loads every saved entry alongside the full path of the file it came from, so a
    /// caller can hand that path straight back to <see cref="Delete"/> without the store needing
    /// to invent or track any separate ID.</summary>
    public IReadOnlyList<(string FilePath, IconLibraryEntry Entry)> LoadAll()
    {
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        var entries = new List<(string, IconLibraryEntry)>();

        foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<IconLibraryEntry>(json, JsonOptions);
                if (entry is not null)
                {
                    entries.Add((file, entry));
                }
            }
            catch (JsonException)
            {
                // Skip a corrupt/hand-edited file rather than failing the whole library load.
            }
        }

        return entries;
    }

    public void Save(IconLibraryEntry entry)
    {
        Directory.CreateDirectory(_folder);

        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(entry.Name.Where(c => !invalid.Contains(c)).ToArray());
        if (safeName.Length == 0)
        {
            safeName = "icon";
        }

        var fileName = $"{safeName}_{Guid.NewGuid():N}.json";
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        File.WriteAllText(Path.Combine(_folder, fileName), json);
    }

    /// <summary>Removes one saved entry's file — <paramref name="filePath"/> is whatever
    /// <see cref="LoadAll"/> reported it as. A no-op (not an error) if the file is already gone,
    /// since the only caller is "delete this library item I'm already looking at".</summary>
    public void Delete(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
