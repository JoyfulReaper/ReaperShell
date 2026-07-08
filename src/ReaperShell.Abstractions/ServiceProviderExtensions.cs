namespace ReaperShell.Abstractions;

public static class ServiceProviderExtensions
{
    public static T? GetService<T>(this IServiceProvider? services)
        where T : class
    {
        return services?.GetService(typeof(T)) as T;
    }
}
