using System.Diagnostics;
using System.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OneWare.ProjectSystem.Models;
using OneWare.SDK.Helpers;
using OneWare.SDK.Services;
using OneWare.UniversalFpgaProjectSystem.Parser;
using Prism.Ioc;
using VHDPlus.Analyzer;
using VHDPlus.Analyzer.Diagnostics;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace OneWare.Vhdp;

public class HdpAnalyzer
{
    private readonly string _workspace;

    private readonly Dictionary<string, string> _documents = new();
    private readonly Dictionary<string, AnalyzerContext> _analyzerContexts = new();

    private UniversalProjectRoot? _projectRoot;

    public event EventHandler<string>? DiagnosticsChanged;
    
    public HdpAnalyzer(string workspace)
    {
        _workspace = workspace;
        _ = LoadWorkspaceAsync();
    }

    public void ProcessChanges(string fullPath, Container<TextDocumentContentChangeEvent> changes)
    {
        _documents[fullPath] = ApplyChanges(_documents[fullPath], changes);
    }

    private static string ApplyChanges(string document, IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        var lines = document.Split('\n');
        var sb = new StringBuilder(document);
        
        var sortedChanges = changes.Select(change =>
            {
                var startCharIndex = lines.Take(change.Range!.Start.Line).Sum(line => line.Length + 1) + change.Range.Start.Character;
                var endCharIndex = lines.Take(change.Range.End.Line).Sum(line => line.Length + 1) + change.Range.End.Character;
                return (startCharIndex, endCharIndex, change.Text);
            })
            .OrderByDescending(c => c.startCharIndex)
            .ToList();
        
        foreach (var (startCharIndex, endCharIndex, text) in sortedChanges)
        {
            sb.Remove(startCharIndex, endCharIndex - startCharIndex);
            sb.Insert(startCharIndex, text);
        }

        return sb.ToString();
    }
    
    public void ProcessChanges(string fullPath, string newText)
    {
        _documents[fullPath] = newText;
    }
    
    private async Task LoadWorkspaceAsync()
    {
        try
        {
            var files = Directory.GetFiles(_workspace);
            var projectFile = files.FirstOrDefault(x =>
                Path.GetExtension(x).Equals(".fpgaproj", StringComparison.OrdinalIgnoreCase));

            if (projectFile == null) throw new Exception(".fpgaproj not found");
            
            _projectRoot = await UniversalFpgaProjectParser.DeserializeAsync(projectFile);

            if (_projectRoot == null) throw new Exception(".fpgaproj failed loading");
            ProjectHelper.ImportEntries(_projectRoot.FullPath, _projectRoot);
            
            //Read and Index
            await Task.WhenAll(_projectRoot.Files.Where(x => x.Extension == ".vhdp")
                .Select(x => ReadAndIndexAsync(x.FullPath)));
            
            //Resolve and Check
            await Task.WhenAll(_analyzerContexts.Select(x =>
                AnalyzeAsync(x.Key, AnalyzerMode.Resolve | AnalyzerMode.Check)));
        }
        catch (Exception e)
        {
            ContainerLocator.Container.Resolve<ILogger>().Error(e.Message, e);
        }
    }
    
    private async Task ReadAndIndexAsync(string fullPath)
    {
        _documents[fullPath] = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
        await AnalyzeAsync(fullPath, AnalyzerMode.Indexing);
    }

    public AnalyzerContext GetContext(string fullPath)
    {
        if (_analyzerContexts.TryGetValue(fullPath, out var context)) return context;
        _analyzerContexts.TryAdd(fullPath, new AnalyzerContext(fullPath, ""));
        return _analyzerContexts[fullPath];
    }

    private string GetDocument(string fullPath)
    {
        _documents.TryAdd(fullPath, string.Empty);
        return _documents[fullPath];
    }

    public async Task AnalyzeAsync(string fullPath, AnalyzerMode mode)
    {
        try
        {
            var text = GetDocument(fullPath);

            var pC = new ProjectContext();
            pC.Files.AddRange(_analyzerContexts.Values);

            var stopWatch = new Stopwatch();
            stopWatch.Start();
            if (mode.HasFlag(AnalyzerMode.Indexing))
                _analyzerContexts[fullPath] = await Task.Run(() => Analyzer.Analyze(fullPath, text, mode, pC));
            else
                _analyzerContexts[fullPath] =
                    await Task.Run(() => Analyzer.Analyze(_analyzerContexts[fullPath], mode, pC));
            stopWatch.Stop();

            ContainerLocator.Container.Resolve<ILogger>()
                .Log($"Analyzing {fullPath} finished after {stopWatch.ElapsedMilliseconds}ms", ConsoleColor.Gray);

            if (mode is AnalyzerMode.Indexing or AnalyzerMode.Resolve) return;
            
            DiagnosticsChanged?.Invoke(this, fullPath);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }
    }
}