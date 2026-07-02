using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootPath = Environment.CurrentDirectory;
        var settings = await LoadSettingsAsync(rootPath);

        var parser = new CommandParser();
        var registry = new CommandRegistry();
        var processRunner = new ProcessRunner();
        var commandPackManager = new CommandPackManager(registry, processRunner);
        var lifetime = new ShellLifetime();

        RegisterBuiltIns(registry, settings, processRunner, commandPackManager, lifetime, rootPath);

        var context = new ShellContext(
            Console.Out,
            Console.Error,
            new DirectoryInfo(rootPath),
            services: null,
            cancellationToken: CancellationToken.None);

        var host = new ShellHost(parser, registry, lifetime);
        await host.RunAsync(context, CancellationToken.None);
        return 0;
    }

    private static void RegisterBuiltIns(
        CommandRegistry registry,
        ShellSettings settings,
        ProcessRunner processRunner,
        CommandPackManager commandPackManager,
        ShellLifetime lifetime,
        string rootPath)
    {
        registry.RegisterBuiltIn(new HelpCommand(registry));
        registry.RegisterBuiltIn(new ClearCommand());
        registry.RegisterBuiltIn(new ExitCommand("exit", lifetime));
        registry.RegisterBuiltIn(new ExitCommand("quit", lifetime));
        registry.RegisterBuiltIn(new PwdCommand());
        registry.RegisterBuiltIn(new LsCommand());
        registry.RegisterBuiltIn(new CdCommand());
        registry.RegisterBuiltIn(new CatCommand());
        registry.RegisterBuiltIn(new RepoCommand(settings, processRunner, commandPackManager, rootPath));
        registry.RegisterBuiltIn(new PluginsCommand(commandPackManager));
    }

    private static async Task<ShellSettings> LoadSettingsAsync(string rootPath)
    {
        try
        {
            return await ShellSettings.LoadOrCreateAsync(rootPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load settings: {ex.Message}");
            return new ShellSettings();
        }
    }
}
