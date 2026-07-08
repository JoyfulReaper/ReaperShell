namespace ReaperShell.Shell;

internal sealed class ShellServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public ShellServiceProvider Add<TService>(TService service)
        where TService : class
    {
        _services[typeof(TService)] = service;
        return this;
    }

    public object? GetService(Type serviceType)
    {
        return _services.TryGetValue(serviceType, out var service) ? service : null;
    }
}
