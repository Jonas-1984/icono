using System.Text.Json;
using System.Text.Json.Serialization;
using Icono.Core;

namespace Icono.IO;

/// <summary>One layer as stored in an .icn file.</summary>
public sealed class LayerFile
{
    public string Name { get; set; } = "Layer";

    public ArgbColor AccentColor { get; set; } = ArgbColor.FromRgb(0x2E, 0x86, 0xFF);

    public bool Visible { get; set; } = true;

    public bool Locked { get; set; }

    public List<Shape> Shapes { get; set; } = [];
}

/// <summary>The on-disk form of a whole <see cref="Document"/> (page size, background and every layer with its shapes).
/// Stored as indented JSON with enum names, so files stay readable and survive new enum members being added.</summary>
public sealed class DocumentFile
{
    /// <summary>The extension new documents are saved with.</summary>
    public const string Extension = ".icn";

    /// <summary>The extension of the first versions; such files still open and are saved as <see cref="Extension"/> when saved under a new name.</summary>
    public const string LegacyExtension = ".icono";

    /// <summary>2: the loop arrows take their head from the line ends of the shape (older files drew it as part of the shape).</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public int Width { get; set; } = Document.PreviewSize;

    public int Height { get; set; } = Document.PreviewSize;

    /// <summary>Number of pages below each other (1 in files written before there were several).</summary>
    public int PageCount { get; set; } = 1;

    /// <summary>Drawing scale 1:N (see <see cref="Document.ScaleDenominator"/>).</summary>
    public double ScaleDenominator { get; set; } = 1;

    /// <summary>Frame and title block of the page; null in files written before sheets existed.</summary>
    public SheetInfo? Sheet { get; set; }

    public BackgroundMode BackgroundMode { get; set; }

    public ArgbColor BackgroundColor { get; set; } = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);

    public byte[]? BackgroundImageBytes { get; set; }

    public string? BackgroundImageName { get; set; }

    public BackgroundImageFit BackgroundImageFit { get; set; }

    public ArgbColor BackgroundBlurColor { get; set; } = ArgbColor.FromRgb(0x6F, 0xB1, 0xFF);

    public bool BackgroundBlurGradientEnabled { get; set; }

    public ArgbColor BackgroundBlurGradientColor { get; set; } = ArgbColor.FromRgb(0xB3, 0x88, 0xFF);

    public double BackgroundBlurAmount { get; set; } = 60;

    public double BackgroundFadeAmount { get; set; } = 40;

    public double BackgroundOpacity { get; set; } = 70;

    public List<LayerFile> Layers { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static DocumentFile From(Document document) => new()
    {
        Width = document.Width,
        Height = document.Height,
        PageCount = document.PageCount,
        ScaleDenominator = document.ScaleDenominator,
        Sheet = document.Sheet,
        BackgroundMode = document.BackgroundMode,
        BackgroundColor = document.BackgroundColor,
        BackgroundImageBytes = document.BackgroundImageBytes,
        BackgroundImageName = document.BackgroundImageName,
        BackgroundImageFit = document.BackgroundImageFit,
        BackgroundBlurColor = document.BackgroundBlurColor,
        BackgroundBlurGradientEnabled = document.BackgroundBlurGradientEnabled,
        BackgroundBlurGradientColor = document.BackgroundBlurGradientColor,
        BackgroundBlurAmount = document.BackgroundBlurAmount,
        BackgroundFadeAmount = document.BackgroundFadeAmount,
        BackgroundOpacity = document.BackgroundOpacity,
        Layers = document.Layers.Select(l => new LayerFile
        {
            Name = l.Name,
            AccentColor = l.AccentColor,
            Visible = l.Visible,
            Locked = l.Locked,
            Shapes = l.Shapes.ToList(),
        }).ToList(),
    };

    /// <summary>Replaces the contents of <paramref name="document"/> with this file's contents (the Document instance
    /// itself is kept, because the whole UI is wired to it).</summary>
    public void ApplyTo(Document document)
    {
        document.Layers.Clear();

        // loop arrows of older files had their head built in: they keep it as an open head at the end
        var givesLoopsAHead = Version < 2;

        document.Width = Math.Clamp(Width, 1, 20000);
        document.Height = Math.Clamp(Height, 1, 20000);
        document.PageCount = PageCount;
        document.BackgroundMode = BackgroundMode;
        document.BackgroundColor = BackgroundColor;
        document.BackgroundImageBytes = BackgroundImageBytes;
        document.BackgroundImageName = BackgroundImageName;
        document.BackgroundImageFit = BackgroundImageFit;
        document.BackgroundBlurColor = BackgroundBlurColor;
        document.BackgroundBlurGradientEnabled = BackgroundBlurGradientEnabled;
        document.BackgroundBlurGradientColor = BackgroundBlurGradientColor;
        document.BackgroundBlurAmount = BackgroundBlurAmount;
        document.BackgroundFadeAmount = BackgroundFadeAmount;
        document.BackgroundOpacity = BackgroundOpacity;
        document.ScaleDenominator = ScaleDenominator > 0 ? ScaleDenominator : 1;
        document.Sheet = Sheet ?? new SheetInfo();

        foreach (var file in Layers)
        {
            var layer = new Layer { Name = file.Name, AccentColor = file.AccentColor, Visible = file.Visible, Locked = file.Locked };
            foreach (var shape in file.Shapes)
            {
                if (givesLoopsAHead && shape.Kind is ShapeKind.LoopArrowClockwise or ShapeKind.LoopArrowCounterClockwise)
                {
                    shape.LineEndHead = ArrowHeadKind.Open;
                }

                layer.Shapes.Add(shape);
            }

            document.Layers.Add(layer);
        }

        if (document.Layers.Count == 0)
        {
            document.Layers.Add(new Layer { Name = "Layer 1" });
        }
    }

    /// <summary>Writes <paramref name="document"/> to <paramref name="path"/>; the file is written to a temporary name first,
    /// so a failed save never destroys the previous version.</summary>
    /// <summary>The document as JSON text — also used as a "fingerprint" to tell whether anything changed since the last save.</summary>
    public static string Serialize(Document document) => JsonSerializer.Serialize(From(document), Options);

    public static void Save(Document document, string path)
    {
        var json = Serialize(document);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Reads an .icn (or legacy .icono) file. Throws <see cref="InvalidDataException"/> when it is not a valid document.</summary>
    /// <summary>The largest document file icono opens (a document with many pictures is a few megabytes; a file this big is not a document).</summary>
    public const long MaxFileBytes = 512L * 1024 * 1024;

    public static DocumentFile Load(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new InvalidDataException("The file is too big to be an icono document.");
        }

        try
        {
            var file = JsonSerializer.Deserialize<DocumentFile>(File.ReadAllText(path), Options);
            return file is null || file.Version > CurrentVersion
                ? throw new InvalidDataException("Unsupported or empty icono file.")
                : file;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The file is not a valid icono document.", ex);
        }
    }
}
