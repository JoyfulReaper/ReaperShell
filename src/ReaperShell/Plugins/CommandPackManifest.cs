using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReaperShell.Plugins;

public sealed class CommandPackManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string CommandsPath { get; set; } = "commands";

    public static async Task<CommandPackManifest> LoadAsync(string manifestPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<CommandPackManifest>(
            stream,
            JsonOptions,
            cancellationToken);

        if (manifest is null)
        {
            throw new InvalidOperationException($"Manifest at '{manifestPath}' is empty or invalid.");
        }

        return manifest;
    }

    public async Task SaveAsync(string manifestPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }, cancellationToken);
    }
}
