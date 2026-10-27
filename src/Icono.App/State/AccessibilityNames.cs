using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Icono.App.State;

/// <summary>Icon-only buttons and menu items would have no name for a screen reader: they get their tool tip as name.</summary>
public static class AccessibilityNames
{
    /// <summary>Names every button, toggle, radio button and menu item below <paramref name="root"/> that has a text tool tip and no name yet.</summary>
    public static void FromToolTips(DependencyObject root)
    {
        if (root is FrameworkElement { ToolTip: string tip } element
            && (root is ButtonBase || root is MenuItem)
            && tip.Length > 0
            && string.IsNullOrEmpty(AutomationProperties.GetName(element)))
        {
            AutomationProperties.SetName(element, tip);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject)
            {
                FromToolTips(dependencyObject);
            }
        }
    }
}
