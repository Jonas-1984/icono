using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Icono.Mobile.Views;

/// <summary>The menu a long press opens - the tablet's right-click menu.</summary>
public sealed partial class DocumentCanvas
{
    private void ShowMenu(bool onObject)
    {
        if (_context is not { } context)
        {
            return;
        }

        var selected = context.SelectedShapes;
        var any = onObject && selected.Count > 0;
        var flyout = new MenuFlyout();

        void Add(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, MinHeight = 36, FontSize = 14 };
            item.Click += (_, _) =>
            {
                action();
                context.RequestRedraw();
            };
            flyout.Items.Add(item);
        }

        void Line() => flyout.Items.Add(new Separator());

        if (any)
        {
            var locked = selected.All(s => s.Locked);
            if (selected.Count == 1 && CanEditText(selected[0]))
            {
                var shape = selected[0];
                Add("Edit text", () => RequestText(shape, LayerOf(shape), shape.X, shape.Y), !locked);
                Line();
            }

            Add("Cut", context.CutSelection, !locked);
            Add("Copy", context.CopySelection);
            Add("Paste", context.PasteClipboard, context.Clipboard.Count > 0);
            Add("Duplicate", context.DuplicateSelection);
            Add("Delete", context.DeleteSelection, !locked);
            Line();
            var grouped = selected.Count > 1 && selected.All(s => s.GroupId is { } id && id == selected[0].GroupId);
            if (selected.Any(s => s.GroupId is not null))
            {
                Add("Ungroup", context.UngroupSelection);
            }

            if (selected.Count > 1 && !grouped)
            {
                Add("Group", context.GroupSelection);
            }

            Add(locked ? "Unlock" : "Lock", () => context.SetSelectionLocked(!locked));
            Line();
            Add("Bring to front", context.BringSelectionToFront, !locked);
            Add("Send to back", context.SendSelectionToBack, !locked);
        }
        else
        {
            Add("Paste", context.PasteClipboard, context.Clipboard.Count > 0);
            Add("Select all", context.SelectAll);
            Line();
            Add("Fit page", FitToView);
        }

        _menu = flyout;
        flyout.ShowAt(this, showAtPointer: true);
    }

    private MenuFlyout? _menu;

    private void CloseMenu()
    {
        _menu?.Hide();
        _menu = null;
    }
}
