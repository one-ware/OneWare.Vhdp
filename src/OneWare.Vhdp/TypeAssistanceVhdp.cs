using OneWare.SDK.EditorExtensions;
using OneWare.SDK.LanguageService;
using OneWare.SDK.ViewModels;
using OneWare.Vhdp.Folding;

namespace OneWare.Vhdp;

public class TypeAssistanceVhdp : TypeAssistanceLsp
{
    private readonly LanguageServiceVhdp _languageServiceVhdp;
    
    public TypeAssistanceVhdp(IEditor editor, LanguageServiceVhdp ls) : base(editor, ls)
    {
        _languageServiceVhdp = ls;
        LineCommentSequence = "--";
        FoldingStrategy = new RegexFoldingStrategy(FoldingRegexVhdp.FoldingStart(), FoldingRegexVhdp.FoldingEnd());
    }

    public override void CodeUpdated()
    {
        base.CodeUpdated();
        _ = _languageServiceVhdp.AnalyzeFullAsync(Editor);
    }
}