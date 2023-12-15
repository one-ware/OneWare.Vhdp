using Avalonia.Threading;
using OneWare.SDK.Models;
using OneWare.SDK.Services;
using Prism.Ioc;

namespace OneWare.Vhdp;

public class ProjectWatcher : IDisposable
{
    private readonly HdpProjectContext _context;
    private readonly FileSystemWatcher _fileSystemWatcher;
    private readonly object _lock = new();
    private DispatcherTimer? _timer;
    private readonly Dictionary<string, List<FileSystemEventArgs>> _changes = new();

    public ProjectWatcher(HdpProjectContext context)
    {
        _context = context;

        _fileSystemWatcher = new FileSystemWatcher(context.Workspace)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true
        };

        _fileSystemWatcher.Changed += File_Changed;
        _fileSystemWatcher.Deleted += File_Changed;
        _fileSystemWatcher.Renamed += File_Changed;
        _fileSystemWatcher.Created += File_Changed;

        try
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) =>
            {
                lock (_lock)
                {
                    ProcessChanges();
                }
            });
            _timer.Start();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private void File_Changed(object source, FileSystemEventArgs e)
    {
        if (e.Name == null) return;
        lock (_lock)
        {
            _changes.TryAdd(e.FullPath, new List<FileSystemEventArgs>());
            _changes[e.FullPath].Add(e);
        }
    }

    private void ProcessChanges()
    {
        foreach (var change in _changes)
        {
            Process(change.Key, change.Value);
        }

        //Task.WhenAll(_changes.Select(x => ProcessAsync(x.Key, x.Value)));
        _changes.Clear();
    }

    private void Process(string path, IReadOnlyCollection<FileSystemEventArgs> changes)
    {
        try
        {
            var lastArg = changes.Last();

            switch (lastArg.ChangeType)
            {
                case WatcherChangeTypes.Created:
                    _context.AddPath(path);
                    return;
                case WatcherChangeTypes.Renamed:
                    var changedArgs = lastArg as RenamedEventArgs;
                    _context.RenamePath(changedArgs!.OldFullPath, changedArgs.FullPath);
                    return;
                case WatcherChangeTypes.Changed:
                    _context.RefreshPath(path);
                    return;
                case WatcherChangeTypes.Deleted:
                    _context.RemovePath(path);
                    return;
            }
        }
        catch (Exception e)
        {
            ContainerLocator.Container.Resolve<ILogger>().Error(e.Message, e, false);
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _fileSystemWatcher.Dispose();
    }
}