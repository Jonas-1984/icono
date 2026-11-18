using Avalonia.Platform.Storage;

namespace Icono.Mobile.Views;

/// <summary>The "Open Recent" list of the tablet app: the last drawings that were opened or saved (newest first), each with the name and the bookmark
/// Android gives for the file (the bookmark opens it again without asking). Kept as a text file in the app's own data folder, one line per file:
/// the name, a tab, the bookmark.</summary>
public static class RecentDrawings
{
    private const int MaxEntries = 10;

    private static readonly string ListFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "icono-recent.txt");

    public sealed record Entry(string Name, string Bookmark);

    public static IReadOnlyList<Entry> Load()
    {
        try
        {
            return File.Exists(ListFile)
                ? File.ReadAllLines(ListFile)
                    .Select(line => line.Split('\t', 2))
                    .Where(parts => parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                    .Select(parts => new Entry(parts[0], parts[1]))
                    .Take(MaxEntries)
                    .ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Puts a file at the top of the list (a file that was already in it moves up).</summary>
    public static async Task AddAsync(IStorageFile file)
    {
        string? bookmark;
        try
        {
            bookmark = await file.SaveBookmarkAsync();
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(bookmark))
        {
            return;
        }

        Save(Load().Where(e => e.Bookmark != bookmark).Prepend(new Entry(file.Name, bookmark.Replace('\t', ' '))).Take(MaxEntries).ToList());
    }

    /// <summary>Takes a file off the list (it is gone, or the user does not want it there).</summary>
    public static void Remove(Entry entry) => Save(Load().Where(e => e.Bookmark != entry.Bookmark).ToList());

    private static void Save(List<Entry> list)
    {
        try
        {
            File.WriteAllLines(ListFile, list.Select(e => e.Name.Replace('\t', ' ').Replace('\n', ' ') + "\t" + e.Bookmark));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the list is a convenience: without it the drawings are still found in the folder
        }
    }
}
