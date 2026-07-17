using System.Reflection;

namespace GSBT.Core.Common;

public static class AppVersionInfo
{
    public static string RawVersion { get; } =
        typeof(AppVersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+', 2)[0]
        ?? "0.0.0.0";

    public static string DisplayVersion => "v" + RawVersion;
}
