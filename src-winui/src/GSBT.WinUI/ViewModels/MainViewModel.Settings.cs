namespace GSBT.WinUI.ViewModels;

public sealed partial class MainViewModel
{
    public async Task SaveSettingsAsync(MainSettingsPayload payload)
    {
        _settings.Set("minimize_to_tray", payload.MinimizeToTray);
        _settings.Set("show_duplicate_save_titles", payload.ShowDuplicateSaveTitles);
        _settings.Set("auto_backup_enabled", payload.AutoBackupEnabled);
        _settings.Set("backup_frequency_minutes", payload.BackupFrequencyMinutes);
        _settings.Set("backup_retention_count", payload.BackupRetentionCount);
        _settings.Set("backup_subfolder_per_game", payload.BackupSubfolderPerGame);
        if (!string.IsNullOrWhiteSpace(payload.DefaultBackupPath))
        {
            _settings.Set("default_backup_path", payload.DefaultBackupPath);
        }

        if (!string.IsNullOrWhiteSpace(payload.LastBackupPath))
        {
            _settings.Set("last_backup_path", payload.LastBackupPath);
        }

        _settings.Set("ui_theme", ThemeBridge.NormalizeUiThemeKey(string.IsNullOrWhiteSpace(payload.UiTheme) ? "dark" : payload.UiTheme));
        _settings.Set("notifications_enabled", payload.NotificationsEnabled);
        _settings.Set("notification_sound_enabled", payload.NotificationSoundEnabled);
        _settings.Set("in_app_ephemeral_status_enabled", payload.InAppEphemeralStatusEnabled);
        _settings.Set("in_app_backup_warnings_enabled", payload.InAppBackupWarningsEnabled);
        _settings.Set("backup_size_estimate_enabled", payload.BackupSizeEstimateEnabled);
        _settings.Set("warn_backup_folder_name_collisions", payload.WarnBackupFolderNameCollisions);

        var fmt = string.IsNullOrWhiteSpace(payload.DateFormat) ? "iso" : payload.DateFormat.Trim().ToLowerInvariant();
        _settings.Set("date_format", fmt);

        var startupMode = string.IsNullOrWhiteSpace(payload.RunOnStartupMode) ? "disabled" : payload.RunOnStartupMode.Trim().ToLowerInvariant();
        _settings.Set("run_on_startup_mode", startupMode);
        WindowsStartupRegistration.Apply(startupMode);

        var durationSec = Math.Clamp(payload.StatusMessageDurationSeconds, 1, 5);
        _settings.Set("status_message_duration_seconds", durationSec);

        _settings.Set("main_window_lock_resolution", payload.MainWindowLockResolution);
        _settings.Set(ReplayTeachingTipsOnNextLaunchSettingKey, payload.ReplayTeachingTipsOnNextLaunch);

        if (payload.MainWindowLockResolution
            && GSBT.WinUI.App.MainWindowRef is Window win
            && WindowSizeHelper.TryGetClientSize(win, out var lockW, out var lockH))
        {
            lockW = Math.Max(WindowSizeHelper.MinClientWidth, lockW);
            lockH = Math.Max(WindowSizeHelper.MinClientHeight, lockH);
            var classified = WindowSizeHelper.ClassifyClientPixels(lockW, lockH);
            _settings.Set("main_window_client_preset", classified);
            _settings.Set("main_window_custom_width", lockW);
            _settings.Set("main_window_custom_height", lockH);
        }
        else
        {
            var winPreset = WindowSizeHelper.NormalizeMainWindowPreset(payload.MainWindowClientPreset);
            _settings.Set("main_window_client_preset", winPreset);
            if (winPreset == WindowSizeHelper.MainWindowPresetCustom)
            {
                _settings.Set("main_window_custom_width", Math.Max(WindowSizeHelper.MinClientWidth, payload.MainWindowCustomWidth));
                _settings.Set("main_window_custom_height", Math.Max(WindowSizeHelper.MinClientHeight, payload.MainWindowCustomHeight));
            }
        }

        RefreshLastBackupDisplays();
        ReconcileLastBackupDiskIntegrity();
        _settings.Set(GameTableColumnVisibility.ShowPlatformColumnKey, payload.ShowPlatformColumn);
        _settings.Set(GameTableColumnVisibility.ShowBackupSizeColumnKey, payload.ShowBackupSizeColumn);

        await RefreshBackupSizeDisplaysAsync();
        StatusText = "Settings saved.";
        _autoBackup.RestartMonitoringIfNeeded();
    }
    public MainSettingsPayload LoadSettingsUi()
    {
        var (cw, ch) = GetMainWindowCustomDimensionsForUi();
        return new MainSettingsPayload(
            MinimizeToTray,
            _settings.Get("show_duplicate_save_titles", false),
            _settings.Get("auto_backup_enabled", false),
            _settings.Get("backup_frequency_minutes", 5),
            _settings.Get("backup_retention_count", 3),
            _settings.Get("backup_subfolder_per_game", true),
            _settings.Get("default_backup_path", string.Empty),
            _settings.Get("last_backup_path", string.Empty),
            _settings.Get("notifications_enabled", false),
            _settings.Get("notification_sound_enabled", true),
            _settings.Get("in_app_ephemeral_status_enabled", true),
            _settings.Get("in_app_backup_warnings_enabled", true),
            _settings.Get("backup_size_estimate_enabled", true),
            _settings.Get("warn_backup_folder_name_collisions", true),
            _settings.Get("date_format", "iso"),
            _settings.Get("run_on_startup_mode", "disabled"),
            ThemeBridge.NormalizeUiThemeKey(_settings.Get("ui_theme", "dark")),
            Math.Clamp(_settings.Get("status_message_duration_seconds", 3), 1, 5),
            WindowSizeHelper.NormalizeMainWindowPreset(_settings.Get("main_window_client_preset", WindowSizeHelper.MainWindowPreset800)),
            cw,
            ch,
            _settings.Get("main_window_lock_resolution", false),
            _settings.Get(ReplayTeachingTipsOnNextLaunchSettingKey, false),
            _settings.Get(GameTableColumnVisibility.ShowPlatformColumnKey, true),
            _settings.Get(GameTableColumnVisibility.ShowBackupSizeColumnKey, true));
    }

    private (int W, int H) GetMainWindowCustomDimensionsForUi()
    {
        var cw = _settings.Get("main_window_custom_width", 0);
        var ch = _settings.Get("main_window_custom_height", 0);
        if (cw >= WindowSizeHelper.MinClientWidth && ch >= WindowSizeHelper.MinClientHeight)
        {
            return (cw, ch);
        }

        return WindowSizeHelper.CalibratedClientSizeForNominal(800, 600);
    }

    /// <summary>User resize: persist as <c>custom</c> preset with last client size.</summary>
    public void PersistMainWindowClientResize(int width, int height)
    {
        width = Math.Max(WindowSizeHelper.MinClientWidth, width);
        height = Math.Max(WindowSizeHelper.MinClientHeight, height);
        _settings.Set("main_window_client_preset", WindowSizeHelper.MainWindowPresetCustom);
        _settings.Set("main_window_custom_width", width);
        _settings.Set("main_window_custom_height", height);
    }

    public bool MinimizeToTray => _settings.Get("minimize_to_tray", false);
}
