using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TubaWinUi3.Pages;

public sealed class FavGlyphConverter : IValueConverter
{
    private const string StarGlyph = "\uE734";
    private const string StarOutlineGlyph = "\uE735";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? StarGlyph : StarOutlineGlyph;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
