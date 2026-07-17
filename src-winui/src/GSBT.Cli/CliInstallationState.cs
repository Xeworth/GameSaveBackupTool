namespace GSBT.Cli;

public static class CliInstallationState
{
    public const string GuiExecutableName = "gsbt-main.exe";

    public static bool IsGuiInstalled => IsGuiInstalledAt(AppContext.BaseDirectory);

    public static string GuiExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, GuiExecutableName);

    public static bool IsGuiInstalledAt(string installDirectory) =>
        File.Exists(Path.Combine(installDirectory, GuiExecutableName));
}
