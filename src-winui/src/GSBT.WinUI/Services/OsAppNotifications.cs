using GSBT.Core.Common;
using GSBT.WinUI.Common;
using GSBT.WinUI.ViewModels;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace GSBT.WinUI.Services;

/// <summary>Shows Windows shell toasts via Windows App SDK App Notifications (unpackaged-friendly when registration succeeds).</summary>
public static class OsAppNotifications
{
    private static bool _handlerAttached;
    private static bool _registered;
    private static string? _registeredIconFileName;

    /// <summary>Call once at startup; failures are ignored (OS policy / unpackaged constraints).</summary>
    public static void EnsureRegistered()
    {
        if (!TryGetSessionBranding(out var displayName, out var iconFileName, out var iconSourcePath))
        {
            TryRegisterBare();
            return;
        }

        if (_registered && string.Equals(_registeredIconFileName, iconFileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            AttachNotificationHandler();
            TryClearRegistration();

            var registeredIconPath = CopyIconForNotificationRegistration(iconSourcePath, iconFileName);
            var iconUri = new Uri(registeredIconPath, UriKind.Absolute);
            AppNotificationManager.Default.Register(displayName, iconUri);
            _registered = true;
            _registeredIconFileName = iconFileName;
        }
        catch
        {
            TryRegisterBare();
        }
    }

    /// <summary>Posts a toast when <c>notifications_enabled</c> is true. Calls <see cref="AppNotificationBuilder.MuteAudio"/> when <c>notification_sound_enabled</c> is false.</summary>
    public static void TryShow(SettingsStore settings, string title, string body, BackupToastSeverity severity = BackupToastSeverity.Neutral)
    {
        if (!settings.Get("notifications_enabled", false))
        {
            return;
        }

        EnsureRegistered();
        if (!_registered)
        {
            return;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("app", "gsbt");

            if (IsRedundantToastTitle(title))
            {
                builder.AddText(ApplySeverityToBody(body, severity));
            }
            else
            {
                var (t, b) = ApplySeverityVisuals(title, body, severity);
                builder.AddText(t);
                builder.AddText(b);
            }

            if (!settings.Get("notification_sound_enabled", true))
            {
                builder.MuteAudio();
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch
        {
            // Ignore failures — optional UX.
        }
    }

    private static void AttachNotificationHandler()
    {
        if (_handlerAttached)
        {
            return;
        }

        AppNotificationManager.Default.NotificationInvoked += (_, _) => { };
        _handlerAttached = true;
    }

    private static void TryClearRegistration()
    {
        try
        {
            AppNotificationManager.Default.UnregisterAll();
        }
        catch
        {
            // ignore
        }

        _registered = false;
        _registeredIconFileName = null;
    }

    private static void TryRegisterBare()
    {
        try
        {
            AttachNotificationHandler();
            AppNotificationManager.Default.Register();
            _registered = true;
            _registeredIconFileName = AppBrandingIcons.MainIconFileName;
        }
        catch
        {
            _registered = false;
            _registeredIconFileName = null;
        }
    }

    private static bool TryGetSessionBranding(
        out string displayName,
        out string iconFileName,
        out string iconSourcePath)
    {
        displayName = AppAboutInfo.AppName;
        iconFileName = AppBrandingIcons.MainIconFileName;
        iconSourcePath = string.Empty;

        var sandboxSession = App.LaunchSandboxMonitor && !App.IsSandboxSimulationChild;
        iconFileName = AppBrandingIcons.IconFileNameForSession(sandboxSession);
        displayName = sandboxSession ? "GSBT Sandbox" : AppAboutInfo.AppName;
        return AppBrandingIcons.TryResolveIconPath(iconFileName, out iconSourcePath);
    }

    /// <summary>
    /// WinApp SDK reads the registered icon from a stable local path. Copy beside AppData so updates
    /// apply even when gsbt.exe and gsbt-sandbox.exe share the same hard-linked binary identity.
    /// </summary>
    private static string CopyIconForNotificationRegistration(string sourcePath, string iconFileName)
    {
        var dir = Path.Combine(UserDataDir.GetWinUiUserDataDir(), "notifications");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, iconFileName);
        File.Copy(sourcePath, dest, overwrite: true);
        return dest;
    }

    private static bool IsRedundantToastTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        if (string.Equals(title, AppAboutInfo.AppName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(title, "GSBT Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(title, $"{AppAboutInfo.AppName} (Simulation)", StringComparison.OrdinalIgnoreCase);
    }

    private static string ApplySeverityToBody(string body, BackupToastSeverity severity) =>
        severity switch
        {
            BackupToastSeverity.Warning => $"\u26A0 {body}",
            BackupToastSeverity.Error => $"\u274C {body}",
            _ => body
        };

    private static (string Title, string Body) ApplySeverityVisuals(string title, string body, BackupToastSeverity severity)
    {
        return severity switch
        {
            BackupToastSeverity.Warning => ($"\u26A0 {title}", body),
            BackupToastSeverity.Error => ($"\u274C {title}", body),
            _ => (title, body)
        };
    }
}
