using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Mri.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter StepForeground =
        new FuncValueConverter<bool, IBrush>(isCurrent =>
            isCurrent ? new SolidColorBrush(Color.Parse("#f2e3b8")) : new SolidColorBrush(Color.Parse("#6d7280")));

    public static readonly IValueConverter StepWeight =
        new FuncValueConverter<bool, FontWeight>(isCurrent =>
            isCurrent ? FontWeight.SemiBold : FontWeight.Normal);
}
