using System.Reflection;
using System.Runtime.CompilerServices;
using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.Plugins;

public sealed class CommandPackManager
{
    private readonly CommandRegistry _commandRegistry;
    private readonly Dictionary<string, LoadedCommandPack> _loadedPacks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ProcessRunner _processRunner;

    public CommandPackManager(CommandRegistry commandRegistry, ProcessRunner processRunner)
    {
        _commandRegistry = commandRegistry;
        _processRunner = processRunner;
    }

    public IReadOnlyCollection<LoadedCommandPack> LoadedPacks => _loadedPacks.Values.ToArray();

    public bool IsLoaded(string repoName)
    {
        return _loadedPacks.ContainsKey(repoName);
    }

    public async Task<CommandPackBuildResult> BuildAsync(
        CommandRepoSettings repo,
        string configuration,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await LoadManifestAsync(repo, cancellationToken);
            var commandProjects = DiscoverCommandProjects(repo, manifest);
            var nuGetConfigPath = await EnsureNuGetConfigAsync(cancellationToken);
            var dotnetEnvironment = CreateDotnetEnvironment();

            if (commandProjects.Count == 0)
            {
                context.WriteErrorLine("No command projects were found in the command pack.");
                return new CommandPackBuildResult(1, Array.Empty<string>());
            }

            var builtAssemblies = new List<string>();

            foreach (var projectPath in commandProjects)
            {
                context.WriteLine($"Building {projectPath}...");

                var restoreResult = await _processRunner.RunAsync(
                    "dotnet",
                    ["restore", projectPath, "--configfile", nuGetConfigPath, "--ignore-failed-sources"],
                    repo.LocalPath,
                    context.WriteLine,
                    context.WriteErrorLine,
                    dotnetEnvironment,
                    cancellationToken);

                if (restoreResult.ExitCode != 0)
                {
                    context.WriteErrorLine($"Restore failed: {projectPath}");
                    return new CommandPackBuildResult(restoreResult.ExitCode, Array.Empty<string>());
                }

                var result = await _processRunner.RunAsync(
                    "dotnet",
                    ["build", projectPath, "-c", configuration, "--no-restore"],
                    repo.LocalPath,
                    context.WriteLine,
                    context.WriteErrorLine,
                    dotnetEnvironment,
                    cancellationToken);

                if (result.ExitCode != 0)
                {
                    context.WriteErrorLine($"Build failed: {projectPath}");
                    return new CommandPackBuildResult(result.ExitCode, Array.Empty<string>());
                }

                var assemblyPath = FindBuiltAssembly(projectPath, configuration);
                if (assemblyPath is null)
                {
                    context.WriteErrorLine($"Build succeeded but no DLL was found for {projectPath}.");
                    return new CommandPackBuildResult(1, Array.Empty<string>());
                }

                builtAssemblies.Add(assemblyPath);
                context.WriteLine($"Build succeeded: {assemblyPath}");
            }

            return new CommandPackBuildResult(0, builtAssemblies);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Build failed: {ex.Message}");
            return new CommandPackBuildResult(1, Array.Empty<string>());
        }
    }

    private static async Task<string> EnsureNuGetConfigAsync(CancellationToken cancellationToken)
    {
        var configDirectory = Path.Combine(Path.GetTempPath(), "ReaperShell");
        Directory.CreateDirectory(configDirectory);

        var configPath = Path.Combine(configDirectory, "NuGet.Config");
        if (!File.Exists(configPath))
        {
            await File.WriteAllTextAsync(
                configPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """,
                cancellationToken);
        }

        return configPath;
    }

    private static IReadOnlyDictionary<string, string?> CreateDotnetEnvironment()
    {
        var dotnetHome = Path.Combine(Path.GetTempPath(), "ReaperShell", "dotnet-home");
        var appData = Path.Combine(dotnetHome, "AppData", "Roaming");
        var packagesPath = Path.Combine(dotnetHome, "packages");

        Directory.CreateDirectory(dotnetHome);
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(packagesPath);

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["APPDATA"] = appData,
            ["DOTNET_CLI_HOME"] = dotnetHome,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["NUGET_PACKAGES"] = packagesPath
        };
    }

    public async Task<CommandPackLoadResult> LoadAsync(
        CommandRepoSettings repo,
        string configuration,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        if (_loadedPacks.ContainsKey(repo.Name))
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is already loaded.");
            return new CommandPackLoadResult(1, Array.Empty<string>());
        }

        try
        {
            var manifest = await LoadManifestAsync(repo, cancellationToken);
            var commandProjects = DiscoverCommandProjects(repo, manifest);
            var assemblyPaths = commandProjects
                .Select(projectPath => FindBuiltAssembly(projectPath, configuration))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToArray();

            if (assemblyPaths.Length == 0)
            {
                context.WriteErrorLine("No built command DLLs were found. Run 'repo build <name>' first.");
                return new CommandPackLoadResult(1, Array.Empty<string>());
            }

            var loadContext = new PluginLoadContext(assemblyPaths);
            var registeredNames = new List<string>();

            try
            {
                foreach (var assemblyPath in assemblyPaths)
                {
                    var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

                    foreach (var command in InstantiateCommands(assembly, context))
                    {
                        if (!_commandRegistry.RegisterPlugin(command, repo.Name, repo.LocalPath))
                        {
                            context.WriteErrorLine(
                                $"Warning: skipped command '{command.Name}' because that name is already registered.");
                            continue;
                        }

                        registeredNames.Add(command.Name);
                    }
                }

                if (registeredNames.Count == 0)
                {
                    RollBackRegisteredCommands(registeredNames);
                    loadContext.Unload();
                    context.WriteErrorLine("No commands were loaded from the command pack.");
                    return new CommandPackLoadResult(1, Array.Empty<string>());
                }

                var loadedPack = new LoadedCommandPack
                {
                    Name = repo.Name,
                    Path = repo.LocalPath,
                    LoadContext = loadContext,
                    RegisteredCommandNames = registeredNames
                };

                _loadedPacks.Add(repo.Name, loadedPack);
                context.WriteLine($"Loaded commands: {string.Join(", ", registeredNames)}");
                return new CommandPackLoadResult(0, registeredNames);
            }
            catch
            {
                RollBackRegisteredCommands(registeredNames);
                loadContext.Unload();
                throw;
            }
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Load failed: {ex.Message}");
            return new CommandPackLoadResult(1, Array.Empty<string>());
        }
    }

    public Task<CommandPackUnloadResult> UnloadAsync(string repoName, ShellContext context)
    {
        if (!_loadedPacks.Remove(repoName, out var loadedPack))
        {
            context.WriteErrorLine($"Repo '{repoName}' is not loaded.");
            return Task.FromResult(new CommandPackUnloadResult(1, false, false));
        }

        var registeredCommandNames = loadedPack.RegisteredCommandNames.ToArray();
        PluginLoadContext? loadContext = loadedPack.LoadContext;
        loadedPack = null;

        foreach (var commandName in registeredCommandNames)
        {
            _commandRegistry.Unregister(commandName);
        }

        var weakReference = RequestUnload(loadContext);
        loadContext = null;
        var fullyUnloaded = WaitForUnload(weakReference);

        context.WriteLine($"Unload requested for '{repoName}'.");
        if (!fullyUnloaded)
        {
            context.WriteLine("The plugin context still has live references, so unload is not yet guaranteed.");
        }
        else
        {
            context.WriteLine("The plugin context was collected.");
        }

        return Task.FromResult(new CommandPackUnloadResult(0, true, fullyUnloaded));
    }

    private void RollBackRegisteredCommands(IEnumerable<string> registeredNames)
    {
        foreach (var commandName in registeredNames)
        {
            _commandRegistry.Unregister(commandName);
        }
    }

    private static async Task<CommandPackManifest> LoadManifestAsync(
        CommandRepoSettings repo,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(repo.LocalPath, "shellpack.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("shellpack.json was not found.", manifestPath);
        }

        return await CommandPackManifest.LoadAsync(manifestPath, cancellationToken);
    }

    private static List<string> DiscoverCommandProjects(CommandRepoSettings repo, CommandPackManifest manifest)
    {
        var commandsRoot = Path.Combine(repo.LocalPath, manifest.CommandsPath);
        if (!Directory.Exists(commandsRoot))
        {
            return [];
        }

        return Directory.GetFiles(commandsRoot, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? FindBuiltAssembly(string projectPath, string configuration)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        var buildDirectory = Path.Combine(projectDirectory, "bin", configuration);
        if (!Directory.Exists(buildDirectory))
        {
            return null;
        }

        return Directory.GetFiles(buildDirectory, $"{assemblyName}.dll", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RequestUnload(PluginLoadContext? loadContext)
    {
        var weakReference = new WeakReference(loadContext);
        loadContext?.Unload();
        return weakReference;
    }

    private static bool WaitForUnload(WeakReference weakReference)
    {
        // Collectible unload is only requested here. If plugin code still has
        // static state, background work, or other rooted references, collection
        // can legitimately stay pending after these GC passes.
        for (var attempt = 0; attempt < 3 && weakReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return !weakReference.IsAlive;
    }

    private static IEnumerable<IShellCommand> InstantiateCommands(Assembly assembly, ShellContext context)
    {
        foreach (var type in SafeGetTypes(assembly, context))
        {
            if (!type.IsClass || type.IsAbstract || !type.IsPublic)
            {
                continue;
            }

            if (ImplementsMismatchedShellCommandContract(type))
            {
                context.WriteErrorLine(
                    $"Warning: skipped '{type.FullName}' because it implements a different ReaperShell.Abstractions contract instance.");
                continue;
            }

            if (!typeof(IShellCommand).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                context.WriteErrorLine($"Skipped '{type.FullName}' because it does not have a parameterless constructor.");
                continue;
            }

            IShellCommand? command;
            try
            {
                command = Activator.CreateInstance(type) as IShellCommand;
            }
            catch (Exception ex)
            {
                context.WriteErrorLine(
                    $"Skipped '{type.FullName}' because its constructor failed: {GetExceptionMessage(ex)}");
                continue;
            }

            if (command is null)
            {
                context.WriteErrorLine(
                    $"Skipped '{type.FullName}' because it could not be instantiated as IShellCommand.");
                continue;
            }

            yield return command;
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly, ShellContext context)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            foreach (var loaderException in ex.LoaderExceptions.Where(loaderException => loaderException is not null))
            {
                context.WriteErrorLine(
                    $"Warning: failed to inspect some types in '{assembly.FullName}': {loaderException!.Message}");
            }

            return ex.Types.Where(type => type is not null).Cast<Type>();
        }
    }

    private static bool ImplementsMismatchedShellCommandContract(Type type)
    {
        return type.GetInterfaces().Any(
            interfaceType =>
                string.Equals(interfaceType.FullName, typeof(IShellCommand).FullName, StringComparison.Ordinal) &&
                interfaceType != typeof(IShellCommand));
    }

    private static string GetExceptionMessage(Exception exception)
    {
        if (exception is TargetInvocationException { InnerException: { } innerException })
        {
            return innerException.Message;
        }

        return exception.Message;
    }
}

public sealed record CommandPackBuildResult(int ExitCode, IReadOnlyList<string> AssemblyPaths);

public sealed record CommandPackLoadResult(int ExitCode, IReadOnlyList<string> LoadedCommands);

public sealed record CommandPackUnloadResult(int ExitCode, bool UnloadRequested, bool FullyUnloaded);
