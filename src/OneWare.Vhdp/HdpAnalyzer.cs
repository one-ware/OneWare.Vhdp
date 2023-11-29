using System.Diagnostics;
using System.Text;
using VHDPlus.Analyzer;

namespace OneWare.Vhdp;

public class HdpAnalyzer
{
    private readonly Dictionary<string, AnalyzerContext> _analyzerContexts = new();

    public AnalyzerContext GetContext(string fullPath)
    {
        if (_analyzerContexts.TryGetValue(fullPath, out var context)) return context;
        _analyzerContexts.TryAdd(fullPath, new AnalyzerContext(fullPath, ""));
        return _analyzerContexts[fullPath];
    }
    
    public async Task AnalyzeAsync(string fullPath, AnalyzerMode mode, string? text)
        {
            try
            {
                // if (Global.Options.ShowAnalyzerOutput)
                //     MainDock.Output.WriteLine("Analyzing " + ((ProjectEntry) this).Header + " Mode: " + mode, Brushes.Gray);

                text ??= await File.ReadAllTextAsync(fullPath, Encoding.UTF8);

                text = text.Replace("\t", "    ");

                

                var pC = new ProjectContext();
                //pC.Files.AddRange(Root.AnalyzableFiles.Where(aF => aF != this).Select(x => x.AnalyzerContext));

                var stopWatch = new Stopwatch();
                stopWatch.Start();
                if(mode.HasFlag(AnalyzerMode.Indexing))
                    _analyzerContexts[fullPath] = await Task.Run(() => Analyzer.Analyze(fullPath,text, mode, pC));
                else 
                    _analyzerContexts[fullPath] = await Task.Run(() => Analyzer.Analyze(_analyzerContexts[fullPath], mode, pC));
                stopWatch.Stop();
                /*if (Global.Options.ShowAnalyzerOutput)
                    MainDock.Output.WriteLine(
                        "Analyzing " + ((ProjectEntry) this).Header + " finished after " + stopWatch.ElapsedMilliseconds + "ms",
                        Brushes.Gray);*/
                
                if (mode is AnalyzerMode.Indexing or AnalyzerMode.Resolve) return;

                // var errorList = AnalyzerContext.Diagnostics
                //     .Select(x =>
                //         new ErrorListItemModel(x.Message, (ErrorType) x.Level, ErrorSource.Vhdpls, this, x.StartLine+1,
                //             x.StartCol+1, x.EndLine+1, x.EndCol+1));
                //
                // //Update Errors
                // MainDock.ErrorList.RefreshErrors(errorList.ToList(), ErrorSource.Vhdpls, this);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }
}