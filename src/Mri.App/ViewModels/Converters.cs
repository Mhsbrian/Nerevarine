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

    /// <summary>Herald strip: ink color per severity.</summary>
    public static readonly IValueConverter AlertInk =
        new FuncValueConverter<Mri.App.Services.AlertSeverity, IBrush>(s => new SolidColorBrush(Color.Parse(s switch
        {
            Mri.App.Services.AlertSeverity.Success => "#3E5A2E",
            Mri.App.Services.AlertSeverity.Warning => "#7A5A1D",
            Mri.App.Services.AlertSeverity.Error => "#7A2E1D",
            _ => "#2E2013",
        })));

    /// <summary>Herald strip: leading glyph per severity.</summary>
    public static readonly IValueConverter AlertGlyph =
        new FuncValueConverter<Mri.App.Services.AlertSeverity, string>(s => s switch
        {
            Mri.App.Services.AlertSeverity.Success => "❧",
            Mri.App.Services.AlertSeverity.Warning => "✦",
            Mri.App.Services.AlertSeverity.Error => "✕",
            _ => "",
        });

    /// <summary>Chrome background: sealed card for warnings/errors, air for the rest.</summary>
    public static readonly IValueConverter AlertChromeBg =
        new FuncValueConverter<Mri.App.Services.AlertSeverity, IBrush>(s => new SolidColorBrush(Color.Parse(s switch
        {
            Mri.App.Services.AlertSeverity.Error => "#F3E0D6",
            Mri.App.Services.AlertSeverity.Warning => "#F0E6C8",
            _ => "#00000000",
        })));

    /// <summary>Chrome border matching the ink, transparent when quiet.</summary>
    public static readonly IValueConverter AlertChromeBorder =
        new FuncValueConverter<Mri.App.Services.AlertSeverity, IBrush>(s => new SolidColorBrush(Color.Parse(s switch
        {
            Mri.App.Services.AlertSeverity.Error => "#7A2E1D",
            Mri.App.Services.AlertSeverity.Warning => "#8A6D3B",
            _ => "#00000000",
        })));

    /// <summary>Errors and warnings get the sealed-card chrome and a dismiss control.</summary>
    public static readonly IValueConverter AlertHasChrome =
        new FuncValueConverter<Mri.App.Services.AlertSeverity, bool>(s =>
            s is Mri.App.Services.AlertSeverity.Error or Mri.App.Services.AlertSeverity.Warning);

    /// <summary>Codex panel title for the active tier's settings ledger.</summary>
    public static readonly IValueConverter TierLedgerTitle =
        new FuncValueConverter<Mri.Core.OpenMw.QualityTier, string>(tier =>
            $"WHAT THE {LauncherViewModel.TierName(tier).ToUpperInvariant()} SEES");

    /// <summary>Ink color that flips to parchment when the tier card is selected.</summary>
    public static readonly IValueConverter TierInk =
        new FuncValueConverter<Mri.Core.OpenMw.QualityTier, object?, IBrush>(
            (tier, param) => param is Mri.Core.OpenMw.QualityTier p && tier == p
                ? new SolidColorBrush(Color.Parse("#F0E4C0"))
                : new SolidColorBrush(Color.Parse("#2E2013")));
}
