using System.Text.Json;
using System.Text.Json.Serialization;

namespace GSBT.WinUI.Services;

internal static class SimulationFlagsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Write(string path, SimulationLaunchFlags flags)
    {
        var json = JsonSerializer.Serialize(flags, Options);
        File.WriteAllText(path, json);
    }

    public static SimulationLaunchFlags Read(string path)
    {
        var text = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SimulationLaunchFlags>(text, Options)
            ?? new SimulationLaunchFlags(false, false, SandboxSevenZipUiMode.Auto, false, false, string.Empty);
    }
}
