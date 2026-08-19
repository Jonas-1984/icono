using System.Windows.Controls;
using Icono.App.State;
using Icono.Core;

namespace Icono.App.Views;

public partial class ToolPanelView : UserControl
{
    public EditorContext? Context { get; set; }

    public ToolPanelView()
    {
        InitializeComponent();
    }

    private void ToolRadioButton_OnChecked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Context is null || sender is not RadioButton { CommandParameter: string tag })
        {
            return;
        }

        switch (tag)
        {
            case "Select":
                Context.ActiveTool = ActiveTool.Select;
                break;
            case "PanZoom":
                Context.ActiveTool = ActiveTool.PanZoom;
                break;
            case "PaintBucket":
                Context.ActiveTool = ActiveTool.PaintBucket;
                break;
            case "Border":
                // The Border tool's exact meaning (frame-around-canvas vs. a shape kind) is
                // ambiguous — treated as a Select alias for now, pending clarification.
                Context.ActiveTool = ActiveTool.Select;
                break;
            default:
                Context.ActiveTool = ActiveTool.Shape;
                Context.SelectedShapeKind = Enum.Parse<ShapeKind>(tag);
                break;
        }
    }
}
