using System.Diagnostics;

namespace Mri.App.Services;

public enum AlertSeverity
{
    /// <summary>Flavor and quiet confirmations — plain ink.</summary>
    Quiet,

    /// <summary>Something good completed.</summary>
    Success,

    /// <summary>Heads-up that changed what will happen.</summary>
    Warning,

    /// <summary>Something failed; stays until dismissed.</summary>
    Error,
}

/// <summary>Best-effort native desktop notifications (roundel included);
/// used when a long operation ends while the window may be unfocused.</summary>
public static class Notifier
{
    public static void Notify(string title, string body)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notify-send",
                    ArgumentList = { "-i", "nerevarine", "-a", "Nerevarine", title, body },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            else if (OperatingSystem.IsWindows())
            {
                var script =
                    "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null;" +
                    "$x = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02);" +
                    $"$t = $x.GetElementsByTagName('text'); $t.Item(0).AppendChild($x.CreateTextNode('{Esc(title)}')) | Out-Null;" +
                    $"$t.Item(1).AppendChild($x.CreateTextNode('{Esc(body)}')) | Out-Null;" +
                    "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Nerevarine').Show([Windows.UI.Notifications.ToastNotification]::new($x))";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
        }
        catch
        {
            // Notifications are a courtesy, never a failure.
        }
    }

    private static string Esc(string s) => s.Replace("'", "''");
}
