using Avalonia;

namespace Mri.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            // Last-resort crash record: the install log lives in the install
            // dir, but a crash before/outside a run still needs a trace.
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MorrowindRemakeInstaller");
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log"),
                    $"{DateTime.Now:O}\n{e}");
            }
            catch (Exception io) when (io is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
