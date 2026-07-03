using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

public sealed class ShellWatchService
{
    private static readonly TimeSpan ReloadDebounceDelay = TimeSpan.FromMilliseconds(750);

    private readonly ShellHost _shellHost;
    private readonly Dictionary<string, WatchedRepo> _watchedRepos =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ShellWatchService(ShellHost shellHost)
    {
        _shellHost = shellHost;
    }

    public int WatchedRepoCount
    {
        get
        {
            lock (_gate)
            {
                return _watchedRepos.Count;
            }
        }
    }

    public bool TryStartWatching(
        ShellContext context,
        string repoName,
        string repoPath,
        out string message)
    {
        try
        {
            lock (_gate)
            {
                if (_watchedRepos.ContainsKey(repoName))
                {
                    message = $"Repo '{repoName}' is already being watched.";
                    return false;
                }

                var watcher = new FileSystemWatcher(repoPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
                };

                var watchedRepo = new WatchedRepo(repoName, repoPath, watcher, context);
                watcher.Changed += (_, eventArgs) => OnFileChanged(watchedRepo, eventArgs.FullPath);
                watcher.Created += (_, eventArgs) => OnFileChanged(watchedRepo, eventArgs.FullPath);
                watcher.Deleted += (_, eventArgs) => OnFileChanged(watchedRepo, eventArgs.FullPath);
                watcher.Renamed += (_, eventArgs) => OnFileChanged(watchedRepo, eventArgs.FullPath);
                watcher.Error += (_, eventArgs) => OnWatcherError(watchedRepo, eventArgs.GetException());
                watcher.EnableRaisingEvents = true;

                _watchedRepos.Add(repoName, watchedRepo);
                message = $"Watching repo '{repoName}' for command-pack changes.";
                return true;
            }
        }
        catch (Exception ex)
        {
            message = $"Failed to watch repo '{repoName}': {ex.Message}";
            return false;
        }
    }

    public bool StopWatching(string repoName, out string message)
    {
        WatchedRepo? watchedRepo;
        lock (_gate)
        {
            if (!_watchedRepos.Remove(repoName, out watchedRepo))
            {
                message = $"Repo '{repoName}' is not being watched.";
                return false;
            }
        }

        watchedRepo.Dispose();
        message = $"Stopped watching repo '{repoName}'.";
        return true;
    }

    public IReadOnlyList<string> GetWatchedRepoNames()
    {
        lock (_gate)
        {
            return _watchedRepos.Keys
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private void OnFileChanged(WatchedRepo watchedRepo, string fullPath)
    {
        if (!IsRelevantPath(fullPath))
        {
            return;
        }

        try
        {
            watchedRepo.ScheduleReload(async reloadCancellationToken =>
            {
                // FileSystemWatcher often emits several events per edit/save. We debounce
                // bursts into one queued `repo reload` and cancel pending work if the
                // watcher is stopped before the reload starts.
                watchedRepo.Context.WriteLine($"File change detected in {watchedRepo.Name}.");
                watchedRepo.Context.WriteLine("Reloading command pack...");

                try
                {
                    await _shellHost.QueueInteractiveCommandAsync(
                        watchedRepo.Context,
                        $"repo reload {watchedRepo.Name}",
                        echoCommand: false,
                        reloadCancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    watchedRepo.Context.WriteErrorLine($"Watcher reload failed for '{watchedRepo.Name}': {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            watchedRepo.Context.WriteErrorLine($"Watcher error for '{watchedRepo.Name}': {ex.Message}");
        }
    }

    private static void OnWatcherError(WatchedRepo watchedRepo, Exception? exception)
    {
        watchedRepo.Context.WriteErrorLine(
            $"Watcher error for '{watchedRepo.Name}': {exception?.Message ?? "Unknown file watcher error."}");
    }

    private static bool IsRelevantPath(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.Equals(fileName, "shellpack.json", StringComparison.OrdinalIgnoreCase))
        {
            return !ContainsIgnoredSegment(fullPath);
        }

        var extension = Path.GetExtension(fullPath);
        return !ContainsIgnoredSegment(fullPath) &&
               (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsIgnoredSegment(string fullPath)
    {
        return fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class WatchedRepo : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private readonly SemaphoreSlim _reloadLock = new(1, 1);
        private readonly FileSystemWatcher _watcher;
        private CancellationTokenSource? _debounceCancellation;
        private bool _disposed;

        public WatchedRepo(string name, string path, FileSystemWatcher watcher, ShellContext context)
        {
            Name = name;
            Path = path;
            _watcher = watcher;
            Context = context;
        }

        public ShellContext Context { get; }

        public string Name { get; }

        public string Path { get; }

        public void ScheduleReload(Func<CancellationToken, Task> reloadAction)
        {
            CancellationTokenSource debounceCancellation;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _debounceCancellation?.Cancel();
                _debounceCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
                debounceCancellation = _debounceCancellation;
            }

            _ = DebounceAndReloadAsync(debounceCancellation, reloadAction);
        }

        public void Dispose()
        {
            CancellationTokenSource? debounceCancellation;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                debounceCancellation = _debounceCancellation;
                _debounceCancellation = null;
            }

            debounceCancellation?.Cancel();
            _lifetimeCancellation.Cancel();
            _watcher.Dispose();
        }

        private async Task DebounceAndReloadAsync(
            CancellationTokenSource debounceCancellation,
            Func<CancellationToken, Task> reloadAction)
        {
            try
            {
                await Task.Delay(ReloadDebounceDelay, debounceCancellation.Token);

                await _reloadLock.WaitAsync(debounceCancellation.Token);
                try
                {
                    lock (_gate)
                    {
                        if (_disposed || !ReferenceEquals(_debounceCancellation, debounceCancellation))
                        {
                            return;
                        }

                        _debounceCancellation = null;
                    }

                    await reloadAction(debounceCancellation.Token);
                }
                finally
                {
                    _reloadLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Context.WriteErrorLine($"Watcher error for '{Name}': {ex.Message}");
            }
            finally
            {
                debounceCancellation.Dispose();
            }
        }
    }
}
