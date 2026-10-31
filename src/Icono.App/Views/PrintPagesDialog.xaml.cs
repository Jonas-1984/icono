using System.Windows;
using Icono.Core;

namespace Icono.App.Views;

/// <summary>Asks which pages of a project with several pages are printed: all, the page being worked on, or a typed list like 1,3-5.</summary>
public partial class PrintPagesDialog : ThemedDialogWindow
{
    private readonly int _pageCount;
    private readonly int _currentPage;

    /// <summary>The pages to print (0-based, ascending).</summary>
    public IReadOnlyList<int> Pages { get; private set; } = [];

    public PrintPagesDialog(int pageCount, int currentPage)
    {
        _pageCount = pageCount;
        _currentPage = currentPage;
        InitializeComponent();
        AllRadio.Content = $"All {pageCount} pages";
        CurrentRadio.Content = $"Current page ({currentPage + 1})";
        RangeTextBox.Text = (currentPage + 1).ToString();
    }

    private void Radio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (RangeTextBox is null)
        {
            return;
        }

        RangeTextBox.IsEnabled = RangeRadio.IsChecked == true;
        if (RangeRadio.IsChecked == true)
        {
            RangeTextBox.Focus();
            RangeTextBox.SelectAll();
        }
    }

    private void RangeTextBox_OnGotFocus(object sender, RoutedEventArgs e) => RangeRadio.IsChecked = true;

    private void PrintButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (AllRadio.IsChecked == true)
        {
            Pages = Enumerable.Range(0, _pageCount).ToList();
        }
        else if (CurrentRadio.IsChecked == true)
        {
            Pages = [_currentPage];
        }
        else if (PageRange.TryParse(RangeTextBox.Text, _pageCount, out var picked, out var error))
        {
            Pages = picked;
        }
        else
        {
            InfoText.Text = error;
            RangeTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
