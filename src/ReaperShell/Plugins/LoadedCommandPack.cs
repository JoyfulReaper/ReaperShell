namespace ReaperShell.Plugins;

public sealed class LoadedCommandPack
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public required PluginLoadContext LoadContext { get; init; }

    public required List<string> RegisteredCommandNames { get; init; }
}
