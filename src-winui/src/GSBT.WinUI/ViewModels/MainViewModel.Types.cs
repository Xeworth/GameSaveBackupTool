namespace GSBT.WinUI.ViewModels;

/// <summary>Visual severity for in-app / OS toasts (icons and titles).</summary>
public enum BackupToastSeverity
{
    Neutral,
    Warning,
    Error
}

/// <summary>Controls optional buttons on the in-app status toast for auto-backup / integrity messages.</summary>
public enum AutoBackupToastChrome
{
    None,
    /// <summary>Single Dismiss control; auto-dismiss timer is extended.</summary>
    DismissOnly,
    /// <summary>Dismiss plus clear Last backup column highlights.</summary>
    DismissAndClearHighlights
}

public readonly record struct AutoBackupTipPayload(
    string Message,
    AutoBackupToastChrome Chrome = AutoBackupToastChrome.None,
    string? MessageSecondLine = null,
    BackupToastSeverity Severity = BackupToastSeverity.Neutral);

public readonly record struct BackupGamesOutcome(string Message, int Succeeded, int Failed, bool Cancelled);

public sealed record MainSettingsPayload(
    bool MinimizeToTray,
    bool ShowDuplicateSaveTitles,
    bool AutoBackupEnabled,
    int BackupFrequencyMinutes,
    int BackupRetentionCount,
    bool BackupSubfolderPerGame,
    string DefaultBackupPath,
    string LastBackupPath,
    bool NotificationsEnabled,
    bool NotificationSoundEnabled,
    bool InAppEphemeralStatusEnabled,
    bool InAppBackupWarningsEnabled,
    bool BackupSizeEstimateEnabled,
    bool WarnBackupFolderNameCollisions,
    string DateFormat,
    string RunOnStartupMode,
    string UiTheme,
    int StatusMessageDurationSeconds,
    string MainWindowClientPreset,
    int MainWindowCustomWidth,
    int MainWindowCustomHeight,
    bool MainWindowLockResolution,
    bool ReplayTeachingTipsOnNextLaunch,
    bool ShowPlatformColumn,
    bool ShowBackupSizeColumn);
