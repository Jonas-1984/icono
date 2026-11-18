using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Icono.Mobile.Views;

/// <summary>icono's own file window. Android's file window has no Cancel button (only the Back gesture leaves it), so icono shows the drawings of
/// ONE folder the user picked once (Android remembers the permission) with Open / Save and Cancel; the Android window is only needed for that
/// folder and for "Other location...".</summary>
public partial class MainView
{
    private enum FilesMode { Open, Save }

    /// <summary>Where the bookmark of the chosen folder is kept (the app's own data folder).</summary>
    private static readonly string FolderSetting =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "icono-folder.txt");

    /// <summary>Where the bookmark of the folder with pictures is kept (a different folder than the one of the drawings).</summary>
    private static readonly string PictureFolderSetting =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "icono-picture-folder.txt");

    private IStorageFolder? _folder;

    /// <summary>True while the file window is for pictures: it has its own folder (<see cref="_folder"/> is the one of the kind shown).</summary>
    private bool _filesForPictures;

    private bool _folderIsForPictures;

    private string FolderSettingPath => _filesForPictures ? PictureFolderSetting : FolderSetting;

    private string FilesNoun => _filesForPictures ? "picture" : "drawing";
    private FilesMode _filesMode;
    private string[] _filesExtensions = [];
    private string _filesExtension = "";
    private FilePickerFileType? _filesType;
    private TaskCompletionSource<IStorageFile?>? _filesResult;
    private List<IStorageFile> _filesInFolder = [];

    /// <summary>A name that already exists asks for a second tap: the name waiting for it.</summary>
    private string? _confirmOverwrite;

    private bool IsFilesOpen => _filesResult is not null;

    /// <summary>Asks for a drawing to open; null when cancelled.</summary>
    private Task<IStorageFile?> AskOpenFileAsync() =>
        ShowFiles(FilesMode.Open, "Open a drawing", "", [".icn", ".icono"], IconoFiles);

    /// <summary>Asks for a picture or an SVG to put into the drawing (its own folder); null when cancelled.</summary>
    private Task<IStorageFile?> AskPictureFileAsync() =>
        ShowFiles(FilesMode.Open, "Insert a picture", "", PictureExtensions, PictureFiles, pictures: true);

    /// <summary>Asks where to save (a name in the chosen folder, or another place); null when cancelled.</summary>
    private Task<IStorageFile?> AskSaveFileAsync(string title, string baseName, string extension, FilePickerFileType type) =>
        ShowFiles(FilesMode.Save, title, baseName, [extension], type);

    private async Task<IStorageFile?> ShowFiles(
        FilesMode mode, string title, string baseName, string[] extensions, FilePickerFileType type, bool pictures = false)
    {
        _filesResult?.TrySetResult(null);
        _filesResult = new TaskCompletionSource<IStorageFile?>();
        _filesMode = mode;
        _filesForPictures = pictures;
        _filesExtensions = extensions;
        _filesExtension = extensions[0];
        _filesType = type;
        _confirmOverwrite = null;

        FilesTitle.Text = title;
        FilesNameRow.IsVisible = mode == FilesMode.Save;
        FilesNameBox.Text = baseName;
        FilesExtensionText.Text = _filesExtension;
        FilesActionButton.IsVisible = mode == FilesMode.Save;
        FilesActionButton.Content = "Save";
        FilesLayer.IsVisible = true;

        await LoadFolderAsync();
        return await _filesResult.Task;
    }

    private void CloseFiles(IStorageFile? result)
    {
        var pending = _filesResult;
        _filesResult = null;
        FilesLayer.IsVisible = false;
        pending?.TrySetResult(result);
    }

    // ---------------------------------------------------------------- the folder

    private async Task LoadFolderAsync()
    {
        if (_folderIsForPictures != _filesForPictures)
        {
            _folder = null; // the other kind has its own folder
            _folderIsForPictures = _filesForPictures;
        }

        if (_folder is null && Storage() is { } storage && File.Exists(FolderSettingPath))
        {
            try
            {
                _folder = await storage.OpenFolderBookmarkAsync((await File.ReadAllTextAsync(FolderSettingPath)).Trim());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                _folder = null;
            }
        }

        FilesFolderText.Text = _folder is null ? "No folder chosen yet" : "Folder: " + _folder.Name;
        FilesFolderButton.Content = _folder is null ? "Choose folder..." : "Change folder...";
        FilesNote.Text = _folder is null
            ? $"Choose once the folder for your {FilesNoun}s (for example {(_filesForPictures ? "Pictures" : "Documents/icono - create it in the Android window if needed")}, then tap " +
              "\"Use this folder\"). In the Android window, Back (‹) returns here without choosing."
            : _filesMode == FilesMode.Open ? $"Tap a {FilesNoun} to open it." : "Type a name and tap Save - or tap a file to use its name.";
        await ListFilesAsync();
    }

    private async Task ListFilesAsync()
    {
        FilesList.Children.Clear();
        _filesInFolder = [];
        AddRecentRows();
        if (_folder is null)
        {
            return;
        }

        var files = new List<(IStorageFile File, DateTimeOffset? Changed, ulong? Size)>();
        try
        {
            await foreach (var item in _folder.GetItemsAsync())
            {
                if (item is IStorageFile file)
                {
                    _filesInFolder.Add(file);
                    if (_filesExtensions.Any(e => file.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    {
                        var properties = await file.GetBasicPropertiesAsync();
                        files.Add((file, properties.DateModified, properties.Size));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FilesNote.Text = "The folder cannot be read any more - choose it again. (" + ex.Message + ")";
            return;
        }

        if (files.Count == 0)
        {
            FilesList.Children.Add(new TextBlock
            {
                Text = _filesMode == FilesMode.Open ? $"No {FilesNoun}s in this folder yet." : "No files of this kind in this folder yet.",
                Margin = new Thickness(8), FontSize = 12.5, Foreground = Brush("MutedTextBrush"),
            });
            return;
        }

        foreach (var (file, changed, size) in files.OrderByDescending(f => f.Changed))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock
            {
                Text = file.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("ForegroundTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var details = new TextBlock
            {
                Text = $"{changed?.LocalDateTime:dd.MM.yyyy HH:mm}   {(size ?? 0) / 1024.0:0} KB", FontSize = 11.5, Margin = new Thickness(12, 0, 0, 0),
                Foreground = Brush("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(details, 1);
            row.Children.Add(details);

            var button = new Button
            {
                Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Classes = { "listRow" }, Padding = new Thickness(10, 6),
            };
            button.Click += (_, _) =>
            {
                if (_filesMode == FilesMode.Open)
                {
                    CloseFiles(file);
                }
                else
                {
                    FilesNameBox.Text = Path.GetFileNameWithoutExtension(file.Name);
                    _confirmOverwrite = null;
                    FilesActionButton.Content = "Save";
                }
            };
            FilesList.Children.Add(button);
        }
    }

    private async void OnFilesChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (Storage() is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = _filesForPictures ? "The folder with your pictures" : "The folder for your icono drawings" });
        if (folders.Count == 0)
        {
            return; // Back in the Android window: nothing changes
        }

        _folder = folders[0];
        try
        {
            if (await _folder.SaveBookmarkAsync() is { } bookmark)
            {
                await File.WriteAllTextAsync(FolderSettingPath, bookmark);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // the folder works for now; next time it is asked for again
        }

        await LoadFolderAsync();
    }

    // ---------------------------------------------------------------- the buttons

    private async void OnFilesAction(object? sender, RoutedEventArgs e)
    {
        if (_folder is null)
        {
            OnFilesChooseFolder(sender, e);
            return;
        }

        var baseName = string.Join("_", (FilesNameBox.Text ?? "").Trim().Split(Path.GetInvalidFileNameChars()));
        if (baseName.EndsWith(_filesExtension, StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^_filesExtension.Length];
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            FilesNote.Text = "Type a name first.";
            return;
        }

        var name = baseName + _filesExtension;
        var existing = _filesInFolder.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && _confirmOverwrite != name)
        {
            _confirmOverwrite = name;
            FilesActionButton.Content = "Replace";
            FilesNote.Text = $"\"{name}\" already exists. Tap \"Replace\" to save over it, or change the name.";
            return;
        }

        try
        {
            CloseFiles(existing ?? await _folder.CreateFileAsync(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FilesNote.Text = "The file cannot be made in this folder: " + ex.Message;
        }
    }

    /// <summary>A file elsewhere: Android's own window (left with Back).</summary>
    private async void OnFilesOtherLocation(object? sender, RoutedEventArgs e)
    {
        if (Storage() is not { } storage || _filesType is null)
        {
            return;
        }

        StatusText.Text = "In the Android window, Back (‹) returns to icono without choosing.";
        if (_filesMode == FilesMode.Open)
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = FilesTitle.Text, AllowMultiple = false, FileTypeFilter = [_filesType],
            });
            if (files.Count > 0)
            {
                CloseFiles(files[0]);
            }

            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = FilesTitle.Text,
            // without the extension: Android adds DefaultExtension itself
            SuggestedFileName = string.IsNullOrWhiteSpace(FilesNameBox.Text) ? "Untitled" : FilesNameBox.Text.Trim(),
            DefaultExtension = _filesExtension.TrimStart('.'),
            FileTypeChoices = [_filesType],
            ShowOverwritePrompt = true,
        });
        if (file is not null)
        {
            CloseFiles(file);
        }
    }

    private void OnFilesCancel(object? sender, RoutedEventArgs e) => CloseFiles(null);

    private static IBrush Brush(string key) =>
        Application.Current?.TryGetResource(key, null, out var value) == true && value is IBrush brush ? brush : Brushes.Gray;

    // ---------------------------------------------------------------- Open Recent

    /// <summary>"Open Recent": above the files of the folder, the drawings that were opened or saved last (not for pictures, not when saving).</summary>
    private void AddRecentRows()
    {
        if (_filesMode != FilesMode.Open || _filesForPictures)
        {
            return;
        }

        var recent = RecentDrawings.Load();
        if (recent.Count == 0)
        {
            return;
        }

        FilesList.Children.Add(new TextBlock
        {
            Text = "RECENT", FontSize = 10.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = Brush("MutedTextBrush"), Margin = new Thickness(8, 6, 0, 2),
        });
        foreach (var entry in recent)
        {
            var name = new Button
            {
                Content = new TextBlock
                {
                    Text = entry.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("ForegroundTextBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Classes = { "listRow" }, Padding = new Thickness(10, 6),
            };
            name.Click += async (_, _) =>
            {
                var storage = Storage();
                IStorageFile? file = null;
                try
                {
                    file = storage is null ? null : await storage.OpenFileBookmarkAsync(entry.Bookmark);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    file = null;
                }

                if (file is null)
                {
                    // the file is gone or cannot be reached any more
                    RecentDrawings.Remove(entry);
                    FilesNote.Text = $"\"{entry.Name}\" cannot be found any more - it was taken off the list.";
                    await ListFilesAsync();
                    return;
                }

                CloseFiles(file);
            };
            var forget = new Button
            {
                Content = "✕", FontSize = 12, Width = 36, Classes = { "listRow" }, Foreground = Brush("MutedTextBrush"),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            forget.Click += async (_, _) =>
            {
                RecentDrawings.Remove(entry);
                await ListFilesAsync();
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(name);
            Grid.SetColumn(forget, 1);
            row.Children.Add(forget);
            FilesList.Children.Add(row);
        }

        FilesList.Children.Add(new TextBlock
        {
            Text = "IN THE FOLDER", FontSize = 10.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = Brush("MutedTextBrush"), Margin = new Thickness(8, 10, 0, 2),
        });
    }
}
