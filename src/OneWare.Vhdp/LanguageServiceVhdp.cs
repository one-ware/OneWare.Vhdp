using OneWare.SDK.LanguageService;
using OneWare.SDK.ViewModels;

namespace OneWare.Vhdp;

public class LanguageServiceVhdp : LanguageServiceBase, ILanguageService
{
    public LanguageServiceVhdp(string name, string? workspace = null) : base(name, workspace)
    {
    }

    public ITypeAssistance GetTypeAssistance(IEditor editor)
    {
        return new TypeAssistanceVhdp(editor, this);
    }

    public override Task ActivateAsync()
    {
        return Task.CompletedTask;
    }
}