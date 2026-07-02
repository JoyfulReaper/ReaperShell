using System.Reflection;
using System.Runtime.Loader;

namespace ReaperShell.Plugins;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly List<AssemblyDependencyResolver> _resolvers;
    private readonly List<string> _probeDirectories;

    public PluginLoadContext(IEnumerable<string> pluginAssemblyPaths)
        : base(isCollectible: true)
    {
        var assemblyPaths = pluginAssemblyPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _resolvers = assemblyPaths
            .Select(path => new AssemblyDependencyResolver(path))
            .ToList();

        _probeDirectories = assemblyPaths
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // ReaperShell.Abstractions must stay in the default load context so
        // the host and plugins share the same IShellCommand identity.
        if (string.Equals(
            assemblyName.Name,
            "ReaperShell.Abstractions",
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var resolver in _resolvers)
        {
            var resolvedAssemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (resolvedAssemblyPath is not null)
            {
                return LoadFromAssemblyPath(resolvedAssemblyPath);
            }
        }

        foreach (var directory in _probeDirectories)
        {
            var candidatePath = Path.Combine(directory, $"{assemblyName.Name}.dll");
            if (File.Exists(candidatePath))
            {
                return LoadFromAssemblyPath(candidatePath);
            }
        }

        return null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var resolver in _resolvers)
        {
            var resolvedPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (resolvedPath is not null)
            {
                return LoadUnmanagedDllFromPath(resolvedPath);
            }
        }

        return 0;
    }
}
