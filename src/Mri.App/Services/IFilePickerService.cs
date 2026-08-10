using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Mri.App.Services;

public interface IFilePickerService
{
    Task<string?> PickFolderAsync(string title);
}

public sealed class StorageFilePickerService(Func<TopLevel?> topLevel) : IFilePickerService
{
    public async Task<string?> PickFolderAsync(string title)
    {
        var storage = topLevel()?.StorageProvider;
        if (storage is null)
            return null;

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}
