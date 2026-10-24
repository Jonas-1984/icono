using System.IO;

namespace Icono.App.State;

/// <summary>The "Open Recent" list, kept as a plain text file (one path per line, newest first) in %LocalAppData%\Icono.</summary>
public static class RecentFiles
{
    private const int MaxEntries = 10;

    private static readonly string ListFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Icono", "recent.txt");

    public static IReadOnlyList<string> Load()
    {
        try
        {
            return File.Exists(ListFile)
                ? File.ReadAllLines(ListFile).Where(l => !string.IsNullOrWhiteSpace(l)).Take(MaxEntries).ToList()
                : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void Add(string path)
    {
        var list = Load().Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).Prepend(path).Take(MaxEntries).ToList();
        Save(list);
    }

    public static void Remove(string path) =>
        Save(Load().Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList());

    private static void Save(List<string> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ListFile)!);
            File.WriteAllLines(ListFile, list);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
