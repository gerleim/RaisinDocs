using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace RaisinDocs.Tests;

public class CheckboxMeasurementTests
{
    [Fact]
    public void CheckboxCharactersHaveSameWidth()
    {
        var typeface = new Typeface("Segoe UI");
        double fontSize = 16;

        var unchecked_ft = new FormattedText("☐", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, fontSize, Brushes.Black, 1.0);

        var checked_ft = new FormattedText("☑", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, fontSize, Brushes.Black, 1.0);

        var prefix_unchecked = new FormattedText("  ☐ ", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, fontSize, Brushes.Black, 1.0);

        var prefix_checked = new FormattedText("  ☑ ", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, fontSize, Brushes.Black, 1.0);

        System.Console.WriteLine($"Unchecked (☐) width: {unchecked_ft.Width}");
        System.Console.WriteLine($"Checked (☑) width: {checked_ft.Width}");
        System.Console.WriteLine($"Character difference: {Math.Abs(unchecked_ft.Width - checked_ft.Width)}");
        System.Console.WriteLine();
        System.Console.WriteLine($"Prefix unchecked ('  ☐ ') width: {prefix_unchecked.Width}");
        System.Console.WriteLine($"Prefix checked ('  ☑ ') width: {prefix_checked.Width}");
        System.Console.WriteLine($"Prefix difference: {Math.Abs(prefix_unchecked.Width - prefix_checked.Width)}");

        Assert.Equal(unchecked_ft.Width, checked_ft.Width, 2);
        Assert.Equal(prefix_unchecked.Width, prefix_checked.Width, 2);
    }
}
