using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mri.Core.Nexus;

namespace Mri.App.ViewModels;

public sealed partial class NexusAuthViewModel(WizardState state, NexusApiClient nexus, ApiKeyStore keyStore)
    : PageViewModel
{
    public override string Title => "Nexus Mods account";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidateCommand))]
    private string _apiKey = "";

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext), nameof(UserSummary), nameof(IsPremiumBlocked))]
    private NexusUser? _user;

    [ObservableProperty]
    private bool _isValidating;

    public override bool CanGoNext => User is { IsPremium: true };

    public bool IsPremiumBlocked => User is { IsPremium: false };

    public string? UserSummary => User is { } user
        ? $"Signed in as {user.Name}{(user.IsPremium ? "  ·  Premium ✓" : "  ·  no Premium")}"
        : null;

    public string Explanation =>
        "Automated downloading of the 600+ mods requires a Nexus Mods Premium account " +
        "(the Nexus API only issues download links to Premium users — free accounts must click " +
        "every file by hand on the website).\n\n" +
        "Paste your personal API key below. You can find it at:\n" +
        "https://next.nexusmods.com/settings/api-keys\n\n" +
        "The key stays on this machine, encrypted with your Windows account.";

    public override void OnActivated()
    {
        if (ApiKey.Length == 0 && keyStore.Load() is { } saved)
        {
            ApiKey = saved;
            _ = ValidateAsync();
        }
    }

    private bool CanValidate() => !string.IsNullOrWhiteSpace(ApiKey) && !IsValidating;

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ValidateAsync()
    {
        IsValidating = true;
        StatusMessage = "Checking key with Nexus Mods…";
        try
        {
            var user = await nexus.ValidateKeyAsync(ApiKey);
            User = user;
            if (user is null)
            {
                StatusMessage = "✗ Nexus rejected this key. Copy it exactly from your API-keys page.";
            }
            else if (!user.IsPremium)
            {
                StatusMessage = "✗ This account has no active Premium — automated downloads are not possible.";
            }
            else
            {
                StatusMessage = "✓ Key valid.";
                state.NexusApiKey = ApiKey.Trim();
                state.NexusUser = user;
                keyStore.Save(ApiKey.Trim());
            }
        }
        catch (HttpRequestException e)
        {
            User = null;
            StatusMessage = $"✗ Could not reach Nexus Mods: {e.Message}";
        }
        finally
        {
            IsValidating = false;
            RaiseNavigationChanged();
        }
    }
}
