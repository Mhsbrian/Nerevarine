using CommunityToolkit.Mvvm.ComponentModel;

namespace Mri.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    private bool _isCurrent;

    /// <summary>Set by the wizard; drives the sidebar highlight.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    /// <summary>Whether the wizard's Next button is enabled on this page.</summary>
    public virtual bool CanGoNext => true;

    public virtual bool CanGoBack => true;

    /// <summary>Called when the page becomes the active one.</summary>
    public virtual void OnActivated()
    {
    }

    protected void RaiseNavigationChanged()
    {
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoBack));
    }
}
