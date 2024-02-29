using System.Text;
using Avalonia;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using OneWare.Essentials.EditorExtensions;
using OneWare.Essentials.LanguageService;
using OneWare.Essentials.ViewModels;
using OneWare.Vhdp.AutoConnect;
using OneWare.Vhdp.Folding;
using OneWare.Vhdp.Formatting;
using VHDPlus.Analyzer;
using VHDPlus.Analyzer.Elements;

namespace OneWare.Vhdp;

public class TypeAssistanceVhdp : TypeAssistanceLanguageService
{
    private readonly LanguageServiceVhdp _languageServiceVhdp;

    public TypeAssistanceVhdp(IEditor editor, LanguageServiceVhdp ls) : base(editor, ls)
    {
        _languageServiceVhdp = ls;
        LineCommentSequence = "--";
        FoldingStrategy = new RegexFoldingStrategy(FoldingRegexVhdp.FoldingStart(), FoldingRegexVhdp.FoldingEnd());
        CodeBox.TextArea.IndentationStrategy = IndentationStrategy  = new VhdpIndentationStrategy(CodeBox.Options);
    }

    private int _lastCorrectionOffset = -1;

    protected override async Task TextEnteredAsync(TextInputEventArgs args)
    {
        await base.TextEnteredAsync(args);
        if (CodeBox.CaretOffset == _lastCorrectionOffset)
        {
            _lastCorrectionOffset = -1;
            return;
        }

        //if (!Global.Options.AutoCorrect) return;
        var offset = CodeBox.CaretOffset - 1;
        if (offset < 0 || offset - 1 >= CodeBox.Document.TextLength) return;
        var lastChar = CodeBox.Text[offset - 1];
        var lastLastChar = offset > 1 ? CodeBox.Text[offset - 2] : '\n';

        switch (args.Text)
        {
            case "/" when lastChar is '/':
                CodeBox.Document.Replace(offset - 1, 2, "--");
                _lastCorrectionOffset = CodeBox.CaretOffset;
                break;
            case "=" when lastChar is ' ':
            {
                var text = CodeBox.Text;
                var result = await Task.Run(() => Analyzer.Analyze(CurrentFile.FullPath, text, AnalyzerMode.Resolve));
                var segment = AnalyzerHelper.GetSegmentFromOffset(result, offset - 1);
                if (segment == null) return;
                segment = AnalyzerHelper.SearchConcatParent(segment);
                if (segment.ConcatOperator is "when" or "and" or "or") return;
                if (segment is { SegmentType: SegmentType.DataVariable } && !AnalyzerHelper.InParameter(segment) &&
                    AnalyzerHelper.SearchVariable(segment, segment.NameOrValue) is { } variable)
                {
                    if (offset >= CodeBox.Document.TextLength) return;
                    if (segment.SegmentType is not SegmentType.VariableDeclaration && variable is
                            { VariableType: VariableType.Io or VariableType.Signal })
                    {
                        CodeBox.Document.Replace(offset, 1, "<=");
                        _lastCorrectionOffset = CodeBox.CaretOffset;
                    }
                    else if (variable is { VariableType: VariableType.Variable } ||
                             segment.SegmentType is SegmentType.VariableDeclaration)
                    {
                        CodeBox.Document.Replace(offset, 1, ":=");
                        _lastCorrectionOffset = CodeBox.CaretOffset;
                    }
                }

                if ((segment is { SegmentType: SegmentType.VariableDeclaration } || segment.Parent is
                        { SegmentType: SegmentType.VariableDeclaration }) && segment.Parent is not
                        { SegmentType: SegmentType.EmptyName })
                {
                    if (offset >= CodeBox.Document.TextLength) return;
                    CodeBox.Document.Replace(offset, 1, ":=");
                    _lastCorrectionOffset = CodeBox.CaretOffset;
                }

                break;
            }
            // case ";":
            //     int type = -1;
            //     var sectionId = 3;
            //     
            //     var text2 = CodeBox.Text;
            //     var result2 = await Task.Run(() => global::VHDPlus.Analyzer.Analyzer.Analyze(CurrentFile.FullPath,text2, AnalyzerMode.Indexing));
            //     var segment2 = AnalyzerHelper.GetSegmentFromOffset(result2, offset-1);
            //     if (segment2 == null) return;
            //     if (AnalyzerHelper.SearchTopSegment(segment2, SegmentType.Package) is { } topLevel)
            //     {
            //         //Inside Package
            //         type = 1;
            //         sectionId = 4;
            //     }
            //     else if (AnalyzerHelper.SearchConcatParent(segment2).Parent is { SegmentType: SegmentType.Generic })
            //     {
            //         //Inside Generic
            //         type = 3;
            //     }
            //     else if (AnalyzerHelper.SearchTopSegment(segment2, SegmentType.Main, SegmentType.Component) is { } topLevel2 &&
            //         AnalyzerHelper.SearchConcatParent(segment2) is {} concatParent2 && topLevel2.Parameter.Any() && topLevel2.Parameter.First().Contains(concatParent2))
            //     {
            //         //Inside Main/Comp inside parameter
            //         type = 2;
            //     }
            //     else {
            //         type = 1;
            //     }
            //     VhdpAutocorrection.SignalDeclarationReplacer(type, sectionId, CodeBox, CurrentFile);
            //     break;
            case "=" when lastChar is '!':
                CodeBox.Document.Replace(offset - 1, 2, "/=");
                _lastCorrectionOffset = CodeBox.CaretOffset;
                break;
            case "=" when lastChar is '=':
                CodeBox.Document.Replace(offset - 1, 2, "=");
                _lastCorrectionOffset = CodeBox.CaretOffset;
                break;
            case "&" when lastChar is '&':
                CodeBox.Document.Replace(offset - 1, 2, "AND");
                _lastCorrectionOffset = CodeBox.CaretOffset;
                break;
            case "|" when lastChar is '|':
                CodeBox.Document.Replace(offset - 1, 2, "OR");
                _lastCorrectionOffset = CodeBox.CaretOffset;
                break;
            case "+" when lastChar is '+':
                var operatorCorrection = await GetOperatorCorrectionAsync(offset - 1);
                var varName = LastWord(offset - 2);
                if (operatorCorrection == null) break;
                CodeBox.Document.Replace(offset - 1, 2,
                    $"{(lastLastChar is ' ' ? "" : " ")}{operatorCorrection} {varName} + 1");
                _lastCorrectionOffset = CodeBox.CaretOffset;
                break;
            case "=" when lastChar is '+':
                var operatorCorrection2 = await GetOperatorCorrectionAsync(offset - 1);
                var varName2 = LastWord(offset - 2);
                if (operatorCorrection2 == null) break;
                CodeBox.Document.Replace(offset - 1, 2,
                    $"{(lastLastChar is ' ' ? "" : " ")}{operatorCorrection2} {varName2} + ");
                _lastCorrectionOffset = CodeBox.CaretOffset;
                break;
            case "=" when lastChar is '-':
                var operatorCorrection3 = await GetOperatorCorrectionAsync(offset - 1);
                var varName3 = LastWord(offset - 2);
                if (operatorCorrection3 == null) break;
                CodeBox.Document.Replace(offset - 1, 2,
                    $"{(lastLastChar is ' ' ? "" : " ")}{operatorCorrection3} {varName3} - ");
                _lastCorrectionOffset = CodeBox.CaretOffset;
                break;
        }
    }

    private string LastWord(int index)
    {
        if (index >= CodeBox.Text.Length) return string.Empty;
        var sb = new StringBuilder();
        var firstChar = false;
        for (var i = index; i >= 0; i--)
        {
            var c = CodeBox.Text[i];
            if (c is ' ')
            {
                if (!firstChar) continue;
                break;
            }

            firstChar = true;
            sb.Insert(0, CodeBox.Text[i]);
        }

        return sb.ToString();
    }

    private async Task<string?> GetOperatorCorrectionAsync(int offset)
    {
        var text = CodeBox.Text;
        var result = await Task.Run(() => Analyzer.Analyze(CurrentFile.FullPath, text, AnalyzerMode.Resolve));
        var segment = AnalyzerHelper.GetSegmentFromOffset(result, offset - 1);
        if (segment == null) return null;
        segment = AnalyzerHelper.SearchConcatParent(segment);
        if (segment.ConcatOperator == "when") return null;
        if (segment is { SegmentType: SegmentType.DataVariable } &&
            AnalyzerHelper.SearchVariable(segment, segment.NameOrValue) is { } variable)
        {
            if (offset >= CodeBox.Document.TextLength) return null;
            if (segment.SegmentType is not SegmentType.VariableDeclaration && variable is
                    { VariableType: VariableType.Io or VariableType.Signal })
            {
                return "<=";
            }

            if (variable is { VariableType: VariableType.Variable } ||
                segment.SegmentType is SegmentType.VariableDeclaration)
            {
                return ":=";
            }
        }

        return null;
    }

    public override async Task<List<MenuItemViewModel>?> GetQuickMenuAsync(int offset)
    {
        var quickMenu = await base.GetQuickMenuAsync(offset);

        var context = _languageServiceVhdp.HdpProjectContext.GetContext(CurrentFile.FullPath);

        var segment = AnalyzerHelper.GetSegmentFromOffset(context, offset);
        if (segment is { SegmentType: SegmentType.NewComponent })
        {
            quickMenu ??= [];
            quickMenu.Add(new MenuItemViewModel("CreateSignals")
            {
                Header = "Create Signals",
                Command = new RelayCommand(() => VhdpAutoConnect.AddSignals(CodeBox, segment)),
            });
        }
        return quickMenu;
    }
}