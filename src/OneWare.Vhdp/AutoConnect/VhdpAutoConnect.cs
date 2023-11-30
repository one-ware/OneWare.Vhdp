using System.Text.RegularExpressions;
using AvaloniaEdit;
using OneWare.SDK.Services;
using Prism.Ioc;
using VHDPlus.Analyzer;
using VHDPlus.Analyzer.Elements;
using VHDPlus.Analyzer.Info;

namespace OneWare.Vhdp.AutoConnect;

public class VhdpAutoConnect
{
    public static void AddSignals(TextEditor codeBox, Segment comp)
    {
        codeBox.Document.BeginUpdate();

        try
        {
            var str = "";
            var iostr = "";
            if (!comp.Context.AvailableComponents.ContainsKey(comp.LastName.ToLower()) || !comp.Parameter.Any()) return;

            var cO = comp.Context.AvailableComponents[comp.LastName.ToLower()];
            var insertLine = comp.Context.GetLine(comp.Offset) + 1;
            var iOs = cO.Variables.Where(x => x.Value.VariableType is VariableType.Io).ToList();
            var generics = cO.Variables.Where(x => x.Value.VariableType is VariableType.Generic).ToList();
            var usedGenerics = new List<DefinedVariable>();
            var generateNames = new List<string>();

            var countIoLines = 0;
            var countSignalLines = 0;

            var mm = comp.Parameter[0].Any() ? comp.Parameter[0][0] : null;
            while (mm != null)
            {
                if (mm.Children.Any() && mm.Children.First() is
                        { ConcatOperator: "=>", SegmentType: SegmentType.EmptyName })
                    generateNames.Add(mm.NameOrValue.ToLower());
                var lastCm = AnalyzerHelper.SearchNextOperatorChild(mm, ",");
                if (lastCm == null) break;
                mm = lastCm;
            }

            foreach (var io in iOs)
            {
                if (!generateNames.Contains(io.Key)) continue;
                var compPar = comp.Parameter[0].Any() ? comp.Parameter[0][0] : null;
                while (compPar != null)
                {
                    if (compPar.NameOrValue.Equals(io.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        var generateIo = cO.Context.Connections.ContainsKey(io.Value.Name.ToLower());

                        var oC = AnalyzerHelper.SearchOperatorChild(io.Value.Owner, ":");
                        if (oC != null && oC.Children.Any() &&
                            oC.Children.First().ConcatOperator is "out" or "in" or "inout" or "buffer")
                        {
                            if (!generateIo) //Remove IO concat
                            {
                                var nS = new Segment(oC.Context, oC, oC.Children.First().NameOrValue, oC.SegmentType,
                                    DataType.Unknown, oC.Children.First().Offset, ":")
                                {
                                    SymSegment = oC.Children.First().SymSegment
                                };
                                nS.Children.AddRange(oC.Children.First().Children);
                                nS.Parameter.AddRange(oC.Children.First().Parameter);
                                oC = nS;
                            }
                        }

                        var printS = oC != null ? PrintSegment.Convert(oC) : "";

                        foreach (var generic in generics)
                        {
                            var pattern = @"\b" + generic.Value.Name + @"\b";
                            if (Regex.IsMatch(printS, pattern))
                            {
                                if (!usedGenerics.Contains(generic.Value)) usedGenerics.Add(generic.Value);
                                printS = Regex.Replace(printS, pattern, $"{comp.LastName}_{generic.Value.Name}");
                            }
                        }

                        if (generateIo)
                        {
                            iostr += $"\n{io.Value.Name}{printS}";
                            countIoLines++;
                        }
                        else
                        {
                            str += $"SIGNAL {comp.LastName}_{io.Value.Name}{printS}\n";
                            countSignalLines++;
                        }

                        break;
                    }

                    compPar = AnalyzerHelper.SearchNextOperatorChild(compPar, ",");
                }
            }

            foreach (var io in usedGenerics)
            {
                if (!generateNames.Contains(io.Name.ToLower())) continue;
                var compPar = comp.Parameter[0].Any() ? comp.Parameter[0][0] : null;
                while (compPar != null)
                {
                    if (compPar.NameOrValue.Equals(io.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        var oC = AnalyzerHelper.SearchOperatorChild(io.Owner, ":");
                        if (oC != null && oC.Children.Any() &&
                            oC.Children.First().ConcatOperator is "out" or "in" or "inout" or "buffer")
                        {
                            var nS = new Segment(oC.Context, oC, oC.Children.First().NameOrValue, oC.SegmentType,
                                DataType.Unknown, oC.Children.First().Offset, ":")
                            {
                                SymSegment = oC.Children.First().SymSegment
                            };
                            nS.Children.AddRange(oC.Children.First().Children);
                            nS.Parameter.AddRange(oC.Children.First().Parameter);
                            oC = nS;
                        }

                        var printS = oC != null ? PrintSegment.Convert(oC) : "";
                        str = str.Insert(0, $"CONSTANT {comp.LastName}_{io.Name}{printS}\n");
                        countSignalLines++;
                    }

                    compPar = AnalyzerHelper.SearchNextOperatorChild(compPar, ",");
                }
            }

            var cM = comp.Parameter[0].Any() ? comp.Parameter[0][0] : null;
            while (cM != null)
            {
                var lastCm = AnalyzerHelper.SearchNextOperatorChild(cM, ",");
                if (lastCm == null) break;
                cM = lastCm;
            }

            while (cM != null && cM.SegmentType != SegmentType.NewComponent)
            {
                var generateIo = cO.Context.Connections.ContainsKey(cM.NameOrValue.ToLower());
                if (cM.Children.Any() && cM.Children.First() is
                        { SegmentType: SegmentType.EmptyName, ConcatOperator: "=>" } cMChild)
                {
                    if (usedGenerics.Select(x => x.Name).Contains(cM.NameOrValue)
                        || iOs.Select(x => x.Value.Name).Contains(cM.NameOrValue))
                        codeBox.Document.Replace(cMChild.ConcatOperatorIndex,
                            cMChild.Offset - cMChild.ConcatOperatorIndex + 1,
                            generateIo ? $"=> {cM.NameOrValue}" : $"=> {comp.LastName}_{cM.NameOrValue}");
                    else
                    {
                        var owner = generics.Select(x => x.Value).FirstOrDefault(x => x.Name == cM.NameOrValue);
                        if (owner != null)
                        {
                            var oC = AnalyzerHelper.SearchOperatorChild(owner.Owner, ":=");
                            if (oC != null)
                            {
                                var ns = new Segment(oC.Context, oC.Parent, oC.NameOrValue, oC.SegmentType,
                                    oC.DataType, oC.Offset, oC.ConcatOperator, oC.ConcatOperatorIndex)
                                {
                                    SymSegment = false,
                                };
                                var printS = PrintSegment.Convert(ns);
                                if (printS.Length > 3) printS = printS[4..^0]; //Remove :=
                                codeBox.Document.Replace(cMChild.ConcatOperatorIndex,
                                    cMChild.Offset - cMChild.ConcatOperatorIndex + 1, $"=> {printS}");
                            }
                        }
                    }
                }

                cM = cM.Parent;
            }

            codeBox.Document.Replace(comp.Offset, 0, str);
            codeBox.TextArea.IndentationStrategy.IndentLines(codeBox.Document, insertLine,
                insertLine + countSignalLines + 1);
            var mainComp = AnalyzerHelper.SearchTopSegment(comp, SegmentType.Component, SegmentType.Main);
            if (mainComp != null)
            {
                var insertOffset = mainComp.Offset;
                if (mainComp.Parameter.Any() && mainComp.Parameter.First().Any())
                {
                    var lastParameter = mainComp.Parameter.First().Last();
                    insertOffset = lastParameter.EndOffset + 1;
                }
                else
                {
                    for (int i = insertOffset; i < codeBox.Document.TextLength - 1; i++)
                    {
                        if (codeBox.Text[i] is '(')
                        {
                            insertOffset = i + 1;
                            break;
                        }
                    }
                }

                codeBox.Document.Replace(insertOffset, 0, iostr);
                var startLine = comp.Context.GetLine(insertOffset);
                codeBox.TextArea.IndentationStrategy.IndentLines(codeBox.Document, startLine,
                    startLine + countIoLines + 1);
            }
        }
        catch (Exception e)
        {
            ContainerLocator.Container.Resolve<ILogger>().Error(e.Message, e);
        }

        codeBox.Document.EndUpdate();
    }
}