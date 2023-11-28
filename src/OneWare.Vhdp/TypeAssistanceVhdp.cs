using OneWare.SDK.EditorExtensions;
using OneWare.SDK.LanguageService;
using OneWare.SDK.ViewModels;
using OneWare.Vhdp.Folding;

namespace OneWare.Vhdp;

public class TypeAssistanceVhdp : TypeAssistanceLsp
{
    public TypeAssistanceVhdp(IEditor editor, LanguageServiceBase ls) : base(editor, ls)
    {
        LineCommentSequence = "--";
        FoldingStrategy = new RegexFoldingStrategy(FoldingRegexVhdp.FoldingStart, FoldingRegexVhdp.FoldingEnd);
    }
}