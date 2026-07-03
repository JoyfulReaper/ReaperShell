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

    public Dictionary<string, string> Aliases { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> Hooks { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string DefaultConfiguration { get; set; } = "Debug";

    public string? EditorCommand { get; set; }

    public static async Task<ShellSettings> LoadOrCreateAsync(
        string stateDirectory,
        CancellationToken cancellationToken)
    {
        var settingsPath = GetSettingsPath(stateDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        if (!File.Exists(settingsPath))
        {
            var settings = new ShellSettings();
            await settings.SaveAsync(stateDirectory, cancellationToken);
            return settings;
        }

        await using var stream = File.OpenRead(settingsPath);
        var settingsFromDisk = await JsonSerializer.DeserializeAsync<ShellSettings>(
            stream,
            JsonOptions,
            cancellationToken);

        return settingsFromDisk?.Normalize() ?? new ShellSettings();
    }

    public async Task SaveAsync(string stateDirectory, CancellationToken cancellationToken)
    {
        var settingsPath = GetSettingsPath(stateDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        await using var stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, this, JsonOptions, cancellationToken);
    }

    public static string GetSettingsPath(string stateDirectory)
    {
        return Path.Combine(stateDirectory, "settings.json");
    }

    public ShellSettings Normalize()
    {
        Repos = Repos is null
            ? new Dictionary<string, CommandRepoSettings>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CommandRepoSettings>(
                Repos
                    .Where(pair => pair.Key is not null && pair.Value is not null)
                    .Select(pair =>
                    {
                        pair.Value.Name = string.IsNullOrWhiteSpace(pair.Value.Name) ? pair.Key : pair.Value.Name;
                        pair.Value.Source ??= string.Empty;
                        pair.Value.LocalPath ??= string.Empty;
                        return pair;
                    }),
                StringComparer.OrdinalIgnoreCase);

        Aliases = Aliases is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(
                Aliases
                    .Where(pair => pair.Key is not null)
                    .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value ?? string.Empty)),
                StringComparer.OrdinalIgnoreCase);

        Hooks = Hooks is null
            ? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, List<string>>(
                Hooks
                    .Where(pair => pair.Key is not null)
                    .Select(pair => new KeyValuePair<string, List<string>>(
                        pair.Key,
                        pair.Value?.Where(ritual => !string.IsNullOrWhiteSpace(ritual)).ToList() ?? [])),
                StringComparer.OrdinalIgnoreCase);

        return this;
    }
}

public sealed class CommandRepoSettings
{
    public string Name { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string LocalPath { get; set; } = string.Empty;

    public bool Trusted { get; set; }

    public bool IsGitRepo { get; set; }

    public bool AutoSyncOnSuccessfulReload { get; set; }
}
