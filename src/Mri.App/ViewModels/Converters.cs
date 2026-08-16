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

    /// <summary>true when the bound tier equals the converter parameter.</summary>
    public static readonly IValueConverter IsTier =
        new FuncValueConverter<Mri.Core.OpenMw.QualityTier, object?, bool>(
            (tier, param) => param is Mri.Core.OpenMw.QualityTier p && tier == p);

    /// <summary>Ink color that flips to parchment when the tier card is selected.</summary>
    public static readonly IValueConverter TierInk =
        new FuncValueConverter<Mri.Core.OpenMw.QualityTier, object?, IBrush>(
            (tier, param) => param is Mri.Core.OpenMw.QualityTier p && tier == p
                ? new SolidColorBrush(Color.Parse("#F0E4C0"))
                : new SolidColorBrush(Color.Parse("#2E2013")));
}
