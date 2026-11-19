using System.Text.Json;
using Icono.Core;

namespace Icono.IO;

/// <summary>A copy of the drawing that is being worked on and not saved yet, kept in a file of the app's own folder, so that a drawing is not lost when the system
/// ends the app (memory, a crash) before it was saved. Written shortly after every change and when the app is left, read when the app starts, deleted as soon as the
/// drawing is saved or let go. The title of the drawing is kept in a second small file next to it.</summary>
public static class DocumentRecovery
{
    private static string NamePath(string path) => path + ".name";

    /// <summary>Writes the drawing (first to a temporary name, so that an interrupted write never destroys the copy that is there). False when it could not be written.</summary>
    public static bool Save(Document document, string name, string path)
    {
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            DocumentFile.Save(document, path);
            File.WriteAllText(NamePath(path), name);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when a copy is there.</summary>
    public static bool Exists(string path) => File.Exists(path);

    /// <summary>The copy and the title it had, or null when there is none or it cannot be read (it is then deleted: a broken copy is of no use and must not be offered again).</summary>
    public static (DocumentFile File, string Name)? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var file = DocumentFile.Load(path);
            var name = File.Exists(NamePath(path)) ? File.ReadAllText(NamePath(path)).Trim() : string.Empty;
            return (file, string.IsNullOrWhiteSpace(name) ? "Untitled" : name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Delete(path);
            return null;
        }
    }

    /// <summary>Deletes the copy (when there is none, nothing happens).</summary>
    public static void Delete(string path)
    {
        foreach (var file in new[] { path, NamePath(path), path + ".tmp" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // it stays; the next write or delete takes care of it
            }
        }
    }
}
