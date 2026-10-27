using System.Collections.ObjectModel;
using Icono.App.State.Undo;
using Icono.Core;
using Icono.Rendering;

namespace Icono.App.State;

public enum ActiveTool
{
    Select,
    PanZoom,
    Shape,
    PathDraw,
    AddPoint,
    DeletePoint,
    RegionColor,
    Eyedropper,
    WallCut,
    Measure,
    MeasureGap,
    Match,
}

/// <summary>
/// Shared editor state — one instance created by MainWindow and handed to the Tools panel,
/// canvas, and Design panel. Plain object, no DI/MVVM framework: matches this project's
/// existing non-MVVM code-behind style.
/// </summary>
public sealed class EditorContext : ObservableObject
{
    private ActiveTool _activeTool = ActiveTool.Select;
    private ShapeKind _selectedShapeKind = ShapeKind.Rectangle;
    private DimensionKind _currentDimensionKind = DimensionKind.Aligned;
    private Layer? _selectedLayer;
    private ArgbColor _currentFillColor = ArgbColor.FromRgb(0x9E, 0xD9, 0xD1);
    private bool _showGrid;
    private bool _snapToGrid;
    private double _gridSize = PageUnits.ToPx(1, PageUnit.Cm); // 1 cm: the page unit starts as cm
    private PageUnit _pageUnit = PageUnit.Cm;
    private UnitObject? _pendingUnit;

    public Document Document { get; } = new();

    /// <summary>Which snapping helpers the wall tool uses (corners, horizontal / vertical, alignment, equal length ...).</summary>
    public WallSnapOptions WallSnap { get; } = new();

    /// <summary>What shapes snap to while they are moved, placed or resized: the faces and corners of walls and the edges of other shapes.</summary>
    public ObjectSnapOptions ObjectSnap { get; } = new();

    /// <summary>Snapping to points of objects (end points, middles, centres, crossings ...), for every tool.</summary>
    public PointSnapOptions PointSnap { get; } = new();

    public UndoManager UndoManager { get; } = new();

    public ActiveTool ActiveTool { get => _activeTool; set => SetField(ref _activeTool, value); }

    public ShapeKind SelectedShapeKind
    {
        get => _selectedShapeKind;
        set
        {
            if (SetField(ref _selectedShapeKind, value) && value != ShapeKind.CombinedPath)
            {
                PendingUnit = null;
            }
        }
    }

    /// <summary>The cabinet or unit of the catalogue that the shape tool places (with <see cref="ShapeKind.CombinedPath"/> as the kind), or null.</summary>
    public UnitObject? PendingUnit { get => _pendingUnit; set => SetField(ref _pendingUnit, value); }

    /// <summary>The name that identifies the active tool in the palettes and menus ("Select", "Wall", "DimAligned", "Rectangle" ...), or null.
    /// The tool, the kind of shape being placed and the kind of dimension together decide it.</summary>
    public string? ActiveToolTag => ActiveTool switch
    {
        ActiveTool.Select => "Select",
        ActiveTool.PanZoom => "PanZoom",
        ActiveTool.AddPoint => "AddPoint",
        ActiveTool.DeletePoint => "DeletePoint",
        ActiveTool.RegionColor => "RegionColor",
        ActiveTool.Eyedropper => "Eyedropper",
        ActiveTool.WallCut => "WallCut",
        ActiveTool.Measure => "Measure",
        ActiveTool.MeasureGap => "MeasureGap",
        ActiveTool.Match => "Match",
        ActiveTool.Shape when SelectedShapeKind == ShapeKind.CombinedPath && PendingUnit is { } unit => "Unit:" + unit.Id,
        ActiveTool.PathDraw when SelectedShapeKind == ShapeKind.Dimension => "Dim" + CurrentDimensionKind,
        ActiveTool.Shape or ActiveTool.PathDraw => SelectedShapeKind.ToString(),
        _ => null,
    };

    /// <summary>The active tool in words, for the status bar.</summary>
    public string ActiveToolName => ActiveToolTag switch
    {
        null => "-",
        "Select" => "Selection",
        "PanZoom" => "Pan / Zoom",
        "RegionColor" => "Region Color",
        "WallCut" => "Cut Wall",
        "Measure" => "Ruler",
        "MeasureGap" => "Distance between objects",
        "Match" => "Match Properties",
        var tag when tag.StartsWith("Unit:", StringComparison.Ordinal) => PendingUnit?.Name ?? "Unit",
        var tag when tag.StartsWith("Dim", StringComparison.Ordinal) => tag[3..] + " dimension",
        var tag => System.Text.RegularExpressions.Regex.Replace(tag, "(?<=[a-z0-9])(?=[A-Z])", " "),
    };

    /// <summary>One line of how to use the active tool, for the status bar (null for tools that need none).</summary>
    public string? ActiveToolHint => ActiveToolTag switch
    {
        "Wall" => "Wall: click the corners; type a length and press Enter to place a corner; Enter ends, Esc cancels.",
        "WallCut" => "Cut Wall: click the wall where it should be cut.",
        "Measure" => "Ruler: click two points - nothing is added to the drawing. Shift locks the direction.",
        "MeasureGap" => "Distance: click the first object, then the second.",
        "Match" => "Match: click the source, then every object that should get its look. Esc ends.",
        "Eyedropper" => "Eyedropper: click anywhere to pick up a colour.",
        "RegionColor" => "Bucket: click a region to colour it.",
        "Polyline" or "Curve" => "Click each point; double-click or Enter finishes, Esc cancels.",
        var tag when tag is not null && tag.StartsWith("Dim", StringComparison.Ordinal) => "Dimension: click the points, then where the dimension line should run.",
        "Line" or "Arrow" or "DoubleArrow" => "Drag from point to point - it snaps to corners, middles and crossings (F3 = snap on/off, Alt = off).",
        _ => null,
    };

    /// <summary>Source of truth for selection. Index order has no meaning; multi-select is a set.</summary>
    public ObservableCollection<Shape> SelectedShapes { get; } = new();

    /// <summary>The last-touched selected shape, or null if nothing is selected. Setting it
    /// replaces the whole selection with just this one shape (or clears it, if null).</summary>
    public Shape? SelectedShape
    {
        get => SelectedShapes.Count > 0 ? SelectedShapes[^1] : null;
        set
        {
            SelectedShapes.Clear();
            if (value is not null)
            {
                SelectedShapes.Add(value);
            }
        }
    }

    public Layer? SelectedLayer { get => _selectedLayer; set => SetField(ref _selectedLayer, value); }

    public ArgbColor CurrentFillColor { get => _currentFillColor; set => SetField(ref _currentFillColor, value); }

    /// <summary>Holds clones of the last Cut/Copy selection, ready for Paste. Plain in-memory
    /// list (no OS clipboard integration) — scoped to this document/session only.</summary>
    public List<Shape> Clipboard { get; } = new();

    public bool ShowGrid { get => _showGrid; set => SetField(ref _showGrid, value); }

    public bool SnapToGrid { get => _snapToGrid; set => SetField(ref _snapToGrid, value); }

    public double GridSize { get => _gridSize; set => SetField(ref _gridSize, value); }

    /// <summary>The unit the page and the main grid are shown and typed in (the UNIT of the CANVAS SIZE card).</summary>
    public PageUnit PageUnit { get => _pageUnit; set => SetField(ref _pageUnit, value); }

    public event EventHandler? Invalidated;

    /// <summary>A short message for the status bar (what was created, why nothing happened, ...).</summary>
    public event Action<string>? StatusMessage;

    /// <summary>Shows a short message in the status bar.</summary>
    public void Notify(string message) => StatusMessage?.Invoke(message);

    /// <summary>Raised when the drawing scale of the document changed (new value 1:N).</summary>
    public event Action<double>? ScaleChanged;

    /// <summary>The unit new dimensions are written in; it follows what the user last chose for a dimension.</summary>
    public LengthUnit DimensionUnit { get; set; } = LengthUnit.Centimeter;

    private int? _wallSegmentHighlight;

    /// <summary>The wall segment whose dimension is being edited in the DIMENSION card (it is highlighted on the canvas).</summary>
    public int? WallSegmentHighlight
    {
        get => _wallSegmentHighlight;
        set
        {
            if (_wallSegmentHighlight != value)
            {
                _wallSegmentHighlight = value;
                Invalidated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Which kind of dimension the dimension tool places next.</summary>
    public DimensionKind CurrentDimensionKind { get => _currentDimensionKind; set => SetField(ref _currentDimensionKind, value); }

    public EditorContext()
    {
        SelectedShapes.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(SelectedShape));
    }

    private bool _refreshingConnections;

    public void RequestRedraw()
    {
        // Lines glued to a shape's connection points follow it whenever anything changes.
        if (!_refreshingConnections)
        {
            _refreshingConnections = true;
            try
            {
                DrawingScale.Denominator = Document.ScaleDenominator;
                FitDoors();
                ConnectionRouter.Refresh(Document);
                DimensionRouter.Refresh(Document);
            }
            finally
            {
                _refreshingConnections = false;
            }
        }

        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A door is exactly as long as it is wide: whatever changed its width (handles, the SIZE card, undo), its height follows.</summary>
    private void FitDoors()
    {
        foreach (var shape in Document.VisibleShapes())
        {
            ArchitecturalSymbols.Normalize(shape);
        }
    }

    public void Undo()
    {
        UndoManager.Undo();
        RequestRedraw();
    }

    public void Redo()
    {
        UndoManager.Redo();
        RequestRedraw();
    }

    /// <summary>Clones the current selection into <see cref="Clipboard"/>, then deletes the
    /// originals (skipping any that are locked — see <see cref="DeleteSelection"/>).</summary>
    public void CutSelection()
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        Clipboard.Clear();
        Clipboard.AddRange(SelectedShapes.Where(s => !s.Locked).Select(CloneShape));
        DeleteSelection();
    }

    /// <summary>Clones the current selection into the clipboard without touching the originals.</summary>
    public void CopySelection()
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        Clipboard.Clear();
        Clipboard.AddRange(SelectedShapes.Select(CloneShape));
    }

    /// <summary>Inserts fresh clones of the clipboard contents, offset slightly so a paste never
    /// sits exactly on top of the shapes it came from.</summary>
    public void PasteClipboard()
    {
        if (Clipboard.Count == 0)
        {
            return;
        }

        var layer = SelectedLayer ?? Document.Layers.FirstOrDefault();
        if (layer is null)
        {
            return;
        }

        var pasted = Clipboard.Select(CloneShape).ToList();
        foreach (var shape in pasted)
        {
            shape.X += 12;
            shape.Y += 12;
        }

        var undoActions = new List<UndoableAction>();
        foreach (var shape in pasted)
        {
            layer.Shapes.Insert(0, shape);
            undoActions.Add(UndoableAction.ForInsert(layer.Shapes, shape, 0));
        }

        UndoManager.Push(UndoableAction.Combine(undoActions));

        SelectedShapes.Clear();
        foreach (var shape in pasted)
        {
            SelectedShapes.Add(shape);
        }

        SelectedLayer = layer;
        RequestRedraw();
    }

    /// <summary>Selects every shape of every visible, unlocked layer.</summary>
    public void SelectAll()
    {
        var all = Document.Layers.Where(l => l.Visible && !l.Locked).SelectMany(l => l.Shapes).ToList();
        SelectedShapes.Clear();
        foreach (var shape in all)
        {
            SelectedShapes.Add(shape);
        }

        RequestRedraw();
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection()
    {
        SelectedShapes.Clear();
        RequestRedraw();
    }

    /// <summary>Copies the selection and pastes it right away (offset a little), leaving the clipboard as it was.</summary>
    public void DuplicateSelection()
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        var previous = Clipboard.ToList();
        CopySelection();
        PasteClipboard();
        Clipboard.Clear();
        Clipboard.AddRange(previous);
    }

    /// <summary>Back to a pristine editing state after a document was replaced (new / opened): no history, no selection, the
    /// first layer active.</summary>
    public void ResetAfterDocumentReplaced()
    {
        ScaleChanged?.Invoke(Document.ScaleDenominator);
        SheetChanged?.Invoke();
        UndoManager.Clear();
        SelectedShapes.Clear();
        SelectedLayer = Document.Layers.FirstOrDefault();
        ActiveTool = ActiveTool.Select;
        RequestRedraw();
    }

    /// <summary>Removes every selected shape that isn't locked (and isn't in a locked layer) —
    /// locked shapes stay selected so the user notices nothing happened and can unlock first.</summary>
    public void DeleteSelection()
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        var removals = new List<(ObservableCollection<Shape> Collection, Shape Shape, int Index)>();

        foreach (var shape in SelectedShapes.ToList())
        {
            foreach (var layer in Document.Layers)
            {
                if (shape.Locked || layer.Locked || !layer.Shapes.Contains(shape))
                {
                    continue;
                }

                var index = layer.Shapes.IndexOf(shape);
                removals.Add((layer.Shapes, shape, index));
                layer.Shapes.RemoveAt(index);
                break;
            }
        }

        if (removals.Count > 0)
        {
            UndoManager.Push(UndoableAction.ForRemove(removals));
        }

        foreach (var (_, shape, _) in removals)
        {
            SelectedShapes.Remove(shape);
        }

        RequestRedraw();
    }

    /// <summary>Assigns a fresh shared GroupId to every selected shape, so clicking any one of
    /// them afterward re-selects the whole group.</summary>
    public void GroupSelection()
    {
        if (SelectedShapes.Count < 2)
        {
            return;
        }

        var shapes = SelectedShapes.ToList();
        var before = shapes.Select(ShapeSnapshot.Capture).ToList();

        var groupId = Guid.NewGuid();
        foreach (var shape in shapes)
        {
            shape.GroupId = groupId;
        }

        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, after));
        RequestRedraw();
    }

    /// <summary>Clears GroupId on every currently selected shape.</summary>
    public void UngroupSelection()
    {
        var shapes = SelectedShapes.Where(s => s.GroupId is not null).ToList();
        if (shapes.Count == 0)
        {
            return;
        }

        var before = shapes.Select(ShapeSnapshot.Capture).ToList();

        foreach (var shape in shapes)
        {
            shape.GroupId = null;
        }

        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, after));
        RequestRedraw();
    }

    /// <summary>Sets <see cref="Shape.Locked"/> on every currently selected shape.</summary>
    public void SetSelectionLocked(bool locked)
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        var shapes = SelectedShapes.ToList();
        var before = shapes.Select(ShapeSnapshot.Capture).ToList();

        foreach (var shape in shapes)
        {
            shape.Locked = locked;
        }

        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, after));
        RequestRedraw();
    }

    /// <summary>Moves every selected (unlocked) shape to the front (index 0) of its own layer.</summary>
    public void BringSelectionToFront() => ReorderSelection(toFront: true);

    /// <summary>Moves every selected (unlocked) shape to the back (last index) of its own layer.</summary>
    public void SendSelectionToBack() => ReorderSelection(toFront: false);

    private void ReorderSelection(bool toFront)
    {
        var moveActions = new List<UndoableAction>();

        foreach (var shape in SelectedShapes.ToList())
        {
            if (shape.Locked)
            {
                continue;
            }

            var layer = Document.Layers.FirstOrDefault(l => !l.Locked && l.Shapes.Contains(shape));
            if (layer is null)
            {
                continue;
            }

            var fromIndex = layer.Shapes.IndexOf(shape);
            var toIndex = toFront ? 0 : layer.Shapes.Count - 1;
            if (fromIndex == toIndex)
            {
                continue;
            }

            layer.Shapes.Move(fromIndex, toIndex);
            moveActions.Add(UndoableAction.ForMove(layer.Shapes, fromIndex, toIndex));
        }

        if (moveActions.Count > 0)
        {
            UndoManager.Push(UndoableAction.Combine(moveActions));
            RequestRedraw();
        }
    }

    /// <summary>Replaces a shape by other shapes at the same place in its layer (a wall cut into pieces), as ONE undo step,
    /// and selects the new shapes.</summary>
    public void ReplaceShape(Shape original, IReadOnlyList<Shape> parts)
    {
        var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(original));
        if (layer is null || parts.Count == 0)
        {
            return;
        }

        var index = layer.Shapes.IndexOf(original);
        var actions = new List<UndoableAction>();
        layer.Shapes.RemoveAt(index);
        actions.Add(UndoableAction.ForRemove([(layer.Shapes, original, index)]));

        for (var i = 0; i < parts.Count; i++)
        {
            layer.Shapes.Insert(index + i, parts[i]);
            actions.Add(UndoableAction.ForInsert(layer.Shapes, parts[i], index + i));
        }

        UndoManager.Push(UndoableAction.Combine(actions));

        SelectedShapes.Clear();
        foreach (var part in parts)
        {
            SelectedShapes.Add(part);
        }

        RequestRedraw();
    }

    /// <summary>Sets the drawing scale 1:N (for example 50 for 1:50) as an undo step. Shapes keep their size on the page; what changes
    /// is the real-world size they stand for, so every measurement is written at the new scale.</summary>
    public void SetScale(double denominator)
    {
        denominator = Math.Clamp(denominator, 0.01, 100000);
        var before = Document.ScaleDenominator;
        if (Math.Abs(before - denominator) < 1e-9)
        {
            return;
        }

        void Apply(double value)
        {
            Document.ScaleDenominator = value;
            RequestRedraw();
            ScaleChanged?.Invoke(value);
        }

        Apply(denominator);
        UndoManager.Push(UndoableAction.ForValue(() => Apply(before), () => Apply(denominator)));
        StatusMessage?.Invoke($"Scale {DrawingScale.Label(denominator)}");
    }

    // ----- Arrange: align, distribute, flip, rotate ----------------------------------------------------------------

    /// <summary>Applies a change to the selected (unlocked) shapes as ONE undo step; false when nothing changed.</summary>
    private bool EditSelection(Func<IReadOnlyList<Shape>, IReadOnlyCollection<Shape>> edit)
    {
        var shapes = SelectedShapes.Where(s => !s.Locked).ToList();
        if (shapes.Count == 0)
        {
            StatusMessage?.Invoke("Select a shape first.");
            return false;
        }

        var before = shapes.Select(ShapeSnapshot.Capture).ToList();
        var changed = edit(shapes);
        if (changed.Count == 0)
        {
            return false;
        }

        UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, shapes.Select(ShapeSnapshot.Capture).ToList()));
        RequestRedraw();
        return true;
    }

    /// <summary>Aligns the selection to a common edge or centre line; a single shape aligns to the page.</summary>
    public void AlignSelection(AlignKind kind) =>
        EditSelection(shapes => ShapeArranger.Align(shapes, kind, Document.Width, Document.Height));

    /// <summary>Makes the gaps between three or more selected shapes equal.</summary>
    public void DistributeSelection(bool horizontally)
    {
        if (SelectedShapes.Count < 3)
        {
            StatusMessage?.Invoke("Select three or more shapes to distribute them.");
            return;
        }

        EditSelection(shapes => ShapeArranger.Distribute(shapes, horizontally));
    }

    /// <summary>Puts the dimension lines of the selected walls inside the plan, outside it, or automatically.</summary>
    public void SetWallDimensionPlacement(WallDimensionPlacement placement)
    {
        if (!SelectedShapes.Any(s => s.Kind == ShapeKind.Wall))
        {
            StatusMessage?.Invoke("Select a wall first.");
            return;
        }

        EditSelection(shapes =>
        {
            var walls = shapes.Where(s => s.Kind == ShapeKind.Wall && s.WallDimensionPlacement != placement).ToList();
            foreach (var wall in walls)
            {
                wall.WallDimensionPlacement = placement;
            }

            return walls;
        });
    }

    public void FlipSelection(bool horizontally) => EditSelection(shapes =>
    {
        foreach (var shape in shapes)
        {
            if (horizontally)
            {
                shape.FlipHorizontal = !shape.FlipHorizontal;
            }
            else
            {
                shape.FlipVertical = !shape.FlipVertical;
            }
        }

        return shapes.ToList();
    });

    /// <summary>Turns every selected shape by a number of degrees (clockwise) around its own centre.</summary>
    public void RotateSelection(double degrees) => EditSelection(shapes =>
    {
        foreach (var shape in shapes)
        {
            shape.Rotation += degrees;
        }

        return shapes.ToList();
    });

    /// <summary>Raised when the sheet (frame and title block) of the page changed, also by undo / redo.</summary>
    public event Action? SheetChanged;

    /// <summary>Changes the sheet of the page as an undo step.</summary>
    public void SetSheet(SheetInfo sheet)
    {
        var before = Document.Sheet;
        if (before == sheet)
        {
            return;
        }

        void Apply(SheetInfo value)
        {
            Document.Sheet = value;
            RequestRedraw();
            SheetChanged?.Invoke();
        }

        Apply(sheet);
        UndoManager.Push(UndoableAction.ForValue(() => Apply(before), () => Apply(sheet)));
    }

    /// <summary>Switches the sheet on or off. Switching it on fills an empty date with today's.</summary>
    public void ToggleSheet()
    {
        var sheet = Document.Sheet;
        if (sheet.Enabled)
        {
            SetSheet(sheet with { Enabled = false });
            StatusMessage?.Invoke("Sheet removed from the page.");
            return;
        }

        SetSheet(sheet with
        {
            Enabled = true,
            Date = string.IsNullOrWhiteSpace(sheet.Date) ? TodayText() : sheet.Date,
        });
        StatusMessage?.Invoke("Sheet added: fill in the title block in the SHEET card on the right.");
    }

    public static string TodayText() => DateTime.Today.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Creates the dimensions of every selected shape (width, height, diameter, angles, lengths ...) next to it, as ONE
    /// undo step. They stay attached: they follow the shape when it changes.</summary>
    public void AddDimensionsToSelection(DimensionSet set)
    {
        var created = 0;
        var actions = new List<UndoableAction>();

        foreach (var target in SelectedShapes.ToList())
        {
            var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(target));
            if (layer is null)
            {
                continue;
            }

            foreach (var dim in DimensionBuilder.Create(target, set, DimensionUnit))
            {
                layer.Shapes.Insert(0, dim);
                actions.Add(UndoableAction.ForInsert(layer.Shapes, dim, 0));
                created++;
            }
        }

        if (created == 0)
        {
            StatusMessage?.Invoke(SelectedShapes.Count == 0
                ? "Select a shape first to add its dimensions."
                : "This shape has no dimensions of that kind (walls show their own lengths).");
            return;
        }

        UndoManager.Push(UndoableAction.Combine(actions));
        StatusMessage?.Invoke(created == 1 ? "1 dimension added." : $"{created} dimensions added.");
        RequestRedraw();
    }

    // ----- Match: carry the look of one object over to others ---------------------------------------------------

    private Shape? _matchSource;

    /// <summary>The object whose look the Match tool paints onto the shapes that are clicked. Null until one was picked.</summary>
    public Shape? MatchSource
    {
        get => _matchSource;
        set
        {
            if (SetField(ref _matchSource, value))
            {
                Invalidated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Which groups of properties the Match tool carries over.</summary>
    public MatchAspects MatchAspects { get; set; } = MatchAspects.Look;

    /// <summary>Starts the Match tool. A single selected shape becomes the source right away; otherwise the first click picks it.</summary>
    public void BeginMatch()
    {
        MatchSource = SelectedShapes.Count == 1 ? SelectedShapes[0] : null;
        ActiveTool = ActiveTool.Match;
        StatusMessage?.Invoke(MatchSource is null
            ? "Match: click the object whose look you want to copy."
            : "Match: click the objects that should get this look. Ctrl+click picks another source, Esc ends.");
    }

    /// <summary>Paints the look of <see cref="MatchSource"/> onto <paramref name="target"/> as one undo step.</summary>
    public void MatchTo(Shape target)
    {
        if (_matchSource is null || !Document.Layers.Any(l => l.Shapes.Contains(_matchSource)))
        {
            MatchSource = null;
            StatusMessage?.Invoke("Match: the source object is gone - click a new one.");
            return;
        }

        if (ReferenceEquals(target, _matchSource))
        {
            return;
        }

        if (target.Locked || Document.Layers.Any(l => l.Locked && l.Shapes.Contains(target)))
        {
            StatusMessage?.Invoke("Match: that object is locked.");
            return;
        }

        var before = ShapeSnapshot.Capture(target);
        if (!MatchProperties.Copy(_matchSource, target, MatchAspects))
        {
            StatusMessage?.Invoke("Match: nothing to change - the look is already the same, or the two objects have nothing in common.");
            return;
        }

        UndoManager.Push(UndoableAction.ForPropertyEdit([target], [before], [ShapeSnapshot.Capture(target)]));
        StatusMessage?.Invoke("Look matched.");
        RequestRedraw();
    }

    /// <summary>Puts a ready-made room (see <see cref="RoomPreset"/>) in the middle of the page as one closed wall.</summary>
    public void InsertRoom(RoomPreset preset)
    {
        var layer = SelectedLayer is { Locked: false } selected ? selected : Document.Layers.FirstOrDefault(l => !l.Locked);
        if (layer is null)
        {
            StatusMessage?.Invoke("All layers are locked.");
            return;
        }

        var width = Math.Min(Document.Width * 0.6, 480);
        var height = Math.Min(Document.Height * 0.45, width * 0.75);
        var wall = WallRooms.Build(preset, Document.Width / 2.0, Document.Height / 2.0, width, height, Document.ScaleDenominator);

        layer.Shapes.Insert(0, wall);
        UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, wall, 0));
        SelectedShape = wall;
        SelectedLayer = layer;
        ActiveTool = ActiveTool.Select;
        StatusMessage?.Invoke("Room added. Click a number to type the length of a wall.");
        RequestRedraw();
    }

    /// <summary>Gives one segment of a wall a new length (page px) as one undo step. The length is the one the dimension shows (the inside of a room, by
    /// default), so typing 250 makes the room 250 wide inside. Walls at a right angle slide along with the end point, and other walls that
    /// were joined to a moved corner follow it.</summary>
    public bool SetWallSegmentLength(Shape wall, int segment, double lengthPx)
    {
        var segments = WallGeometry.Segments(wall);
        if (segment < 0 || segment >= segments.Count)
        {
            return false;
        }

        // the dimension measures a face, the wall moves by its centre line: the difference between the two stays the same when a side grows
        var centre = Geo.Distance(segments[segment].From, segments[segment].To);
        lengthPx = centre + (lengthPx - WallGeometry.MeasuredLength(wall, segment));

        var others = Document.Layers.Where(l => !l.Locked).SelectMany(l => l.Shapes)
            .Where(s => s.Kind == ShapeKind.Wall && !s.Locked && !ReferenceEquals(s, wall)).ToList();
        var affected = new List<Shape> { wall };
        affected.AddRange(others);
        var before = affected.Select(ShapeSnapshot.Capture).ToList();

        var moves = WallEditor.ResizeSegment(wall, segment, lengthPx);
        if (moves.Count == 0)
        {
            return false;
        }

        foreach (var other in others)
        {
            WallEditor.FollowMoves(other, moves);
        }

        var after = affected.Select(ShapeSnapshot.Capture).ToList();
        var changed = Enumerable.Range(0, affected.Count).Where(i => !after[i].Equals(before[i])).ToList();
        UndoManager.Push(UndoableAction.ForPropertyEdit(
            changed.Select(i => affected[i]).ToList(), changed.Select(i => before[i]).ToList(), changed.Select(i => after[i]).ToList()));
        RequestRedraw();
        return true;
    }

    /// <summary>Moves one corner of a wall onto the straight line between its neighbours, as one undo step.</summary>
    public void StraightenWallCorner(Shape wall, int index)
    {
        var before = ShapeSnapshot.Capture(wall);
        if (!WallStraightener.StraightenCorner(wall, index))
        {
            StatusMessage?.Invoke("That corner cannot be straightened (an end of an open wall has only one neighbour).");
            return;
        }

        UndoManager.Push(UndoableAction.ForPropertyEdit([wall], [before], [ShapeSnapshot.Capture(wall)]));
        StatusMessage?.Invoke("Corner straightened: the two walls run on in one line. Use Merge Straight Parts to make them one wall.");
        RequestRedraw();
    }

    /// <summary>Turns walls that run straight on into one wall (the corner between them is removed) for every selected wall, as one undo step.</summary>
    public void MergeStraightWallParts()
    {
        var walls = SelectedShapes.Where(s => s.Kind == ShapeKind.Wall && !s.Locked).ToList();
        if (walls.Count == 0)
        {
            StatusMessage?.Invoke("Select a wall first.");
            return;
        }

        var before = walls.Select(ShapeSnapshot.Capture).ToList();
        var merged = walls.Sum(w => WallStraightener.MergeStraightParts(w));
        if (merged == 0)
        {
            StatusMessage?.Invoke("No parts to merge: every corner of the wall is a real bend. Drag a corner onto the line between its neighbours first to straighten it.");
            return;
        }

        UndoManager.Push(UndoableAction.ForPropertyEdit(walls, before, walls.Select(ShapeSnapshot.Capture).ToList()));
        StatusMessage?.Invoke(merged == 1 ? "Two walls merged into one." : $"{merged} corners removed: the straight parts are one wall now.");
        RequestRedraw();
    }

    /// <summary>Unites the selected open walls into ONE wall (two walls: the nearest ends are joined; more: one after the other), as one undo
    /// step. Where the ends do not touch a new wall segment bridges the gap; where they run straight on the corner disappears.</summary>
    public void JoinSelectedWalls()
    {
        var walls = SelectedShapes.Where(s => s.Kind == ShapeKind.Wall && s.Points is { Count: >= 2 }).ToList();
        if (walls.Count < 2)
        {
            StatusMessage?.Invoke("Select two walls first (click one, Shift+click the other), then join them.");
            return;
        }

        if (walls.Any(w => w.Locked))
        {
            StatusMessage?.Invoke("A selected wall is locked: unlock it first.");
            return;
        }

        var current = walls[0];
        double gap = 0;
        var straight = 0;
        for (var i = 1; i < walls.Count; i++)
        {
            if (WallEditor.Join(current, walls[i]) is not { } joined)
            {
                return;
            }

            current = joined.Wall;
            gap = Math.Max(gap, joined.Gap);
            straight += joined.StraightCorners;
        }

        var removals = new List<(System.Collections.ObjectModel.ObservableCollection<Shape> Collection, Shape Shape, int Index)>();
        foreach (var wall in walls)
        {
            var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(wall));
            if (layer is not null)
            {
                removals.Add((layer.Shapes, wall, layer.Shapes.IndexOf(wall)));
            }
        }

        var target = removals.OrderBy(r => r.Index).First();
        var insertAt = target.Index;
        foreach (var removal in removals)
        {
            removal.Collection.Remove(removal.Shape);
        }

        target.Collection.Insert(Math.Min(insertAt, target.Collection.Count), current);
        UndoManager.Push(UndoableAction.Combine(
        [
            UndoableAction.ForRemove(removals.OrderBy(r => r.Index).ToList()),
            UndoableAction.ForInsert(target.Collection, current, Math.Min(insertAt, target.Collection.Count - 1)),
        ]));

        SelectedShapes.Clear();
        SelectedShapes.Add(current);
        StatusMessage?.Invoke(
            (gap > 0
                ? $"Walls joined into one: a new wall of {DimensionRenderer.Format(gap, current.WallDimensionUnit, current.DimPrecision)} bridges the gap."
                : "Walls joined into one.")
            + (straight > 0 ? " Walls in one line are one straight wall now." : string.Empty)
            + (walls.Any(w => w.WallClosed) ? " The closed room was opened at the corner nearest to the other wall." : string.Empty));
        RequestRedraw();
    }

    /// <summary>Makes the opening of a door, window or wall opening a given width (page px), as one undo step. The jamb at the start of the symbol
    /// stays, the other one moves.</summary>
    public bool SetSymbolWidth(Shape symbol, double widthPx)
    {
        var before = ShapeSnapshot.Capture(symbol);
        if (!SymbolMeasurements.SetOpeningWidth(symbol, widthPx))
        {
            return false;
        }

        UndoManager.Push(UndoableAction.ForPropertyEdit([symbol], [before], [ShapeSnapshot.Capture(symbol)]));
        RequestRedraw();
        return true;
    }

    /// <summary>Slides a door or window along its wall until the clear distance from its jamb to the wall corner is <paramref name="gapPx"/>, as one undo step.</summary>
    public bool SetSymbolGap(Shape symbol, SymbolDimensionPart part, double gapPx)
    {
        var before = ShapeSnapshot.Capture(symbol);
        if (!SymbolMeasurements.SetGap(Document, symbol, part, gapPx))
        {
            return false;
        }

        UndoManager.Push(UndoableAction.ForPropertyEdit([symbol], [before], [ShapeSnapshot.Capture(symbol)]));
        RequestRedraw();
        return true;
    }

    /// <summary>Shows or hides the width dimension of doors, windows and wall openings: of the selected ones, or of every one on the page.</summary>
    public void SetSymbolDimensions(bool show, bool all)
    {
        var symbols = (all ? Document.VisibleShapes().ToList() : SelectedShapes.ToList()).Where(s => ArchitecturalSymbols.IsSymbol(s.Kind) && !s.Locked && (s.SymbolShowDimension != show || s.SymbolShowGaps != show)).ToList();
        if (symbols.Count == 0)
        {
            StatusMessage?.Invoke(all || SelectedShapes.Any(s => ArchitecturalSymbols.IsSymbol(s.Kind))
                ? "Nothing to change: the dimensions are already " + (show ? "shown." : "hidden.")
                : "Select a door, window or opening first.");
            return;
        }

        var before = symbols.Select(ShapeSnapshot.Capture).ToList();
        foreach (var symbol in symbols)
        {
            symbol.SymbolShowDimension = show;
            symbol.SymbolShowGaps = show;
        }

        UndoManager.Push(UndoableAction.ForPropertyEdit(symbols, before, symbols.Select(ShapeSnapshot.Capture).ToList()));
        StatusMessage?.Invoke($"Dimension {(show ? "shown" : "hidden")} on {symbols.Count} door/window/opening symbol(s).");
        RequestRedraw();
    }

    /// <summary>Closes the selected open walls: the last point is joined to the first by a new wall segment, which makes a closed room.</summary>
    public void CloseSelectedWalls()
    {
        var walls = SelectedShapes.Where(s => s.Kind == ShapeKind.Wall && !s.Locked).ToList();
        if (walls.Count == 0)
        {
            StatusMessage?.Invoke("Select a wall first, then close it to make a closed room.");
            return;
        }

        var open = walls.Where(w => !w.WallClosed).ToList();
        if (open.Count == 0)
        {
            StatusMessage?.Invoke("That wall is already a closed room.");
            return;
        }

        if (open.All(w => w.Points is not { Count: >= 3 }))
        {
            StatusMessage?.Invoke("A room needs at least three corners.");
            return;
        }

        var closeable = open.Where(w => w.Points is { Count: >= 3 }).ToList();
        var before = closeable.Select(ShapeSnapshot.Capture).ToList();
        foreach (var wall in closeable)
        {
            wall.WallClosed = true;
            WallEditor.PruneOverrides(wall);
        }

        UndoManager.Push(UndoableAction.ForPropertyEdit(closeable, before, closeable.Select(ShapeSnapshot.Capture).ToList()));
        var closing = closeable.Count == 1 ? WallGeometry.Segments(closeable[0]).Count - 1 : -1;
        StatusMessage?.Invoke(closing >= 0
            ? $"Room closed: a new wall of {DimensionRenderer.Format(WallGeometry.MeasuredLength(closeable[0], closing), closeable[0].WallDimensionUnit, closeable[0].DimPrecision)} joins the last corner to the first."
            : $"{closeable.Count} rooms closed.");
        RequestRedraw();
    }

    /// <summary>Breaks every selected wall into one wall per segment.</summary>
    public void SplitSelectedWallsAtAllPoints()
    {
        foreach (var wall in SelectedShapes.Where(s => s.Kind == ShapeKind.Wall && !s.Locked && s.Points is { Count: > 1 }).ToList())
        {
            var parts = WallEditor.SplitAtAllPoints(wall);
            if (parts.Count > 1)
            {
                ReplaceShape(wall, parts);
            }
        }
    }

    private static Shape CloneShape(Shape source)
    {
        var clone = new Shape { Kind = source.Kind };
        ShapeSnapshot.Capture(source).ApplyTo(clone);
        return clone;
    }

    /// <summary>Moves every selected (unlocked) shape by a fixed offset — the Arrow-key nudge
    /// (see MainWindow's PreviewKeyDown), one undo entry per key press.</summary>
    public void NudgeSelection(double dx, double dy)
    {
        var shapes = SelectedShapes.Where(s => !s.Locked).ToList();
        if (shapes.Count == 0)
        {
            return;
        }

        var before = shapes.Select(ShapeSnapshot.Capture).ToList();

        foreach (var shape in shapes)
        {
            shape.X += dx;
            shape.Y += dy;
        }

        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, after));
        RequestRedraw();
    }

    /// <summary>Adds a mirrored, fading copy directly below each selected (unlocked) shape — a
    /// classic "water reflection" effect. FadeAmount already fades solid-top to transparent-bottom
    /// in plain document space regardless of FlipVertical, so the reflection reads as attached at
    /// the original's bottom edge and fading away below it with no extra rendering logic needed.</summary>
    public void ReflectSelection()
    {
        var shapes = SelectedShapes.Where(s => !s.Locked).ToList();
        if (shapes.Count == 0)
        {
            return;
        }

        var reflections = new List<Shape>();
        var undoActions = new List<UndoableAction>();

        foreach (var shape in shapes)
        {
            var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
            if (layer is null || layer.Locked)
            {
                continue;
            }

            var reflection = CloneShape(shape);
            reflection.FlipVertical = !shape.FlipVertical;
            reflection.Y = shape.Y + shape.Height;
            reflection.FadeAmount = Math.Max(shape.FadeAmount, 55);
            reflection.GroupId = null;

            layer.Shapes.Insert(0, reflection);
            undoActions.Add(UndoableAction.ForInsert(layer.Shapes, reflection, 0));
            reflections.Add(reflection);
        }

        if (reflections.Count == 0)
        {
            return;
        }

        UndoManager.Push(UndoableAction.Combine(undoActions));

        SelectedShapes.Clear();
        foreach (var reflection in reflections)
        {
            SelectedShapes.Add(reflection);
        }

        RequestRedraw();
    }

    /// <summary>Adds a border-offset copy of each selected (unlocked) shape — see
    /// <see cref="PathOffsetter.Offset"/>. <paramref name="distance"/> is signed: positive grows a
    /// closed shape outward / thickens an open line's outline, negative shrinks a closed shape
    /// inward (clamped so it can't collapse past nothing).</summary>
    public void OffsetSelection(double distance)
    {
        var shapes = SelectedShapes.Where(s => !s.Locked).ToList();
        if (shapes.Count == 0 || distance == 0)
        {
            return;
        }

        var offsets = new List<Shape>();
        var undoActions = new List<UndoableAction>();

        foreach (var shape in shapes)
        {
            var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
            if (layer is null || layer.Locked)
            {
                continue;
            }

            var offsetShape = PathOffsetter.Offset(shape, distance);
            if (offsetShape is null)
            {
                continue;
            }

            layer.Shapes.Insert(0, offsetShape);
            undoActions.Add(UndoableAction.ForInsert(layer.Shapes, offsetShape, 0));
            offsets.Add(offsetShape);
        }

        if (offsets.Count == 0)
        {
            return;
        }

        UndoManager.Push(UndoableAction.Combine(undoActions));

        SelectedShapes.Clear();
        foreach (var offsetShape in offsets)
        {
            SelectedShapes.Add(offsetShape);
        }

        RequestRedraw();
    }

    /// <summary>Merges 2+ selected shapes (all in the same layer) into one combined shape, using
    /// the union of their outlines (overlap included).</summary>
    public void UnionSelection()
    {
        var shapes = SelectedShapes.ToList();
        if (shapes.Count < 2 || shapes.Any(s => s.Locked))
        {
            return;
        }

        var layer = Document.Layers.FirstOrDefault(l => shapes.All(l.Shapes.Contains));
        if (layer is null)
        {
            return;
        }

        // Front-most first (index 0 = front-most) so the combined shape's colors come from
        // whichever shape is visually on top.
        var ordered = shapes.OrderBy(s => layer.Shapes.IndexOf(s)).ToList();
        var combined = ShapeCombiner.Union(ordered);
        if (combined is not null)
        {
            ReplaceWithCombinedShape(layer, ordered, combined);
        }
    }

    /// <summary>Combines every selected shape's geometry into ONE object (see
    /// <see cref="ShapeCombiner.Merge"/>) — unlike <see cref="UnionSelection"/>, works for lines
    /// and stroke-only icons too, and never distorts a piece's own look (no boolean fill-merge).</summary>
    public void MergeSelection()
    {
        var shapes = SelectedShapes.ToList();
        if (shapes.Count < 2 || shapes.Any(s => s.Locked))
        {
            return;
        }

        var layer = Document.Layers.FirstOrDefault(l => shapes.All(l.Shapes.Contains));
        if (layer is null)
        {
            return;
        }

        var ordered = shapes.OrderBy(s => layer.Shapes.IndexOf(s)).ToList();
        var merged = ShapeCombiner.Merge(ordered);
        if (merged is not null)
        {
            ReplaceWithCombinedShape(layer, ordered, merged);
        }
    }

    /// <summary>Cuts away the part of the second selected shape ("target") covered by the first
    /// ("cutter") — selection order picks the roles, not automatic z-order, so the user controls
    /// which shape actually gets trimmed.</summary>
    public void TrimSelection()
    {
        var shapes = SelectedShapes.ToList();
        if (shapes.Count != 2 || shapes.Any(s => s.Locked))
        {
            return;
        }

        var layer = Document.Layers.FirstOrDefault(l => shapes.All(l.Shapes.Contains));
        if (layer is null)
        {
            return;
        }

        var front = shapes[0];
        var back = shapes[1];

        var trimmed = ShapeCombiner.Trim(front, back);
        if (trimmed is not null)
        {
            ReplaceWithCombinedShape(layer, [front, back], trimmed);
        }
    }

    /// <summary>"Explode": every selected combined path (a catalogue object, a merged or imported drawing) falls apart into its single
    /// outlines, every polyline into its segments. All the pieces stay where they were and are selected; one undo step brings the
    /// object back.</summary>
    public void ExplodeSelection()
    {
        var candidates = SelectedShapes.Where(s => !s.Locked && s.Kind is ShapeKind.CombinedPath or ShapeKind.Polyline).ToList();
        if (candidates.Count == 0)
        {
            Notify(SelectedShapes.Count == 0
                ? "Select a combined object (for example a sink or a merged drawing) to explode it."
                : "Only combined objects and polylines can be exploded.");
            return;
        }

        var actions = new List<UndoableAction>();
        var pieces = new List<Shape>();
        foreach (var shape in candidates)
        {
            var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
            if (layer is null || ShapeCombiner.Explode(shape) is not { } parts)
            {
                continue;
            }

            var index = layer.Shapes.IndexOf(shape);
            layer.Shapes.RemoveAt(index);
            actions.Add(UndoableAction.ForRemove([(layer.Shapes, shape, index)]));
            for (var i = 0; i < parts.Count; i++)
            {
                layer.Shapes.Insert(index + i, parts[i]);
                actions.Add(UndoableAction.ForInsert(layer.Shapes, parts[i], index + i));
            }

            pieces.AddRange(parts);
        }

        if (actions.Count == 0)
        {
            Notify("Nothing to explode: this object has only one outline.");
            return;
        }

        UndoManager.Push(UndoableAction.Combine(actions));
        SelectedShapes.Clear();
        foreach (var piece in pieces)
        {
            SelectedShapes.Add(piece);
        }

        RequestRedraw();
        Notify($"Exploded into {pieces.Count} separate pieces (Ctrl+Z puts the object back).");
    }

    private void ReplaceWithCombinedShape(Layer layer, IReadOnlyList<Shape> sources, Shape combined)
    {
        var removals = sources.Select(s => (layer.Shapes, s, layer.Shapes.IndexOf(s))).ToList();
        foreach (var (collection, shape, _) in removals)
        {
            collection.Remove(shape);
        }

        layer.Shapes.Insert(0, combined);

        UndoManager.Push(UndoableAction.Combine(
            [UndoableAction.ForRemove(removals), UndoableAction.ForInsert(layer.Shapes, combined, 0)]));

        SelectedShapes.Clear();
        SelectedShapes.Add(combined);
        SelectedLayer = layer;
        RequestRedraw();
    }

    /// <summary>Converts a fixed-geometry shape (Star, Cross, etc.) into a freeform points-based
    /// shape so every vertex becomes independently draggable via the existing point-edit handles.
    /// No-op if the kind doesn't support it (see <see cref="ShapeGeometryFactory.SupportsPointEditing"/>)
    /// or it's already points-based.</summary>
    /// <summary>Replaces a Text shape with an outlined <see cref="ShapeKind.CombinedPath"/> (see
    /// <see cref="TextOutliner.ConvertToShape"/>) — <see cref="Shape.Kind"/> is <c>init</c>-only,
    /// so this can never be an in-place mutation like <see cref="ConvertToEditablePoints"/>.</summary>
    public void ConvertTextToShape(Shape shape)
    {
        if (shape.Locked || shape.Kind != ShapeKind.Text)
        {
            return;
        }

        var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
        if (layer is null || layer.Locked)
        {
            return;
        }

        var converted = TextOutliner.ConvertToShape(shape);
        if (converted is not null)
        {
            ReplaceWithCombinedShape(layer, [shape], converted);
        }
    }

    public void ConvertToEditablePoints(Shape shape)
    {
        if (shape.Locked || shape.Points is not null || !ShapeGeometryFactory.SupportsPointEditing(shape.Kind))
        {
            return;
        }

        var before = ShapeSnapshot.Capture(shape);

        // Already shape-local (origin = this shape's own X,Y) — ExtractEditablePoints mirrors
        // BuildLocal's un-offset local template, matching Shape.Points' documented convention.
        shape.Points = ShapeGeometryFactory.ExtractEditablePoints(shape);

        var after = ShapeSnapshot.Capture(shape);
        UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [after]));
        RequestRedraw();
    }
}
