using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReaperShell.Shell;

public sealed class ShellSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Dictionary<string, CommandRepoSettings> Repos { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string DefaultConfiguration { get; set; } = "Debug";

    public static async Task<ShellSettings> LoadOrCreateAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var settingsPath = GetSettingsPath(rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        if (!File.Exists(settingsPath))
        {
            var settings = new ShellSettings();
            await settings.SaveAsync(rootPath, cancellationToken);
            return settings;
        }

        await using var stream = File.OpenRead(settingsPath);
        var settingsFromDisk = await JsonSerializer.DeserializeAsync<ShellSettings>(
            stream,
            JsonOptions,
            cancellationToken);

        return settingsFromDisk ?? new ShellSettings();
    }

    public async Task SaveAsync(string rootPath, CancellationToken cancellationToken)
    {
        var settingsPath = GetSettingsPath(rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        await using var stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, this, JsonOptions, cancellationToken);
    }

    public static string GetSettingsPath(string rootPath)
    {
        return Path.Combine(rootPath, ".rsh", "settings.json");
    }
}

public sealed class CommandRepoSettings
{
    public string Name { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string LocalPath { get; set; } = string.Empty;

    public bool Trusted { get; set; }

    public bool IsGitRepo { get; set; }
}
