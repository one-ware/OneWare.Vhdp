using System.Reflection;
using System.Text.Json.Nodes;
using OneWare.Essentials.Services;
using OneWare.UniversalFpgaProjectSystem.Helpers;
using OneWare.UniversalFpgaProjectSystem.Models;
using OneWare.UniversalFpgaProjectSystem.Services;

namespace OneWare.Vhdp.Templates;

public class VhdpBlinkTemplate(ILogger logger, IDockService dockService) : IFpgaProjectTemplate
{
    public string Name => "VHDP Blink";

    public void FillTemplate(UniversalFpgaProjectRoot root)
    {
        var codeBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new NullReferenceException(Assembly.GetExecutingAssembly().Location);
        
        var path = Path.Combine(codeBase, "Assets", "Templates", "VhdpBlink");

        try
        {
            var name = root.Header.Replace(" ", "");
            TemplateHelper.CopyDirectoryAndReplaceString(path, root.FullPath, ("%PROJECTNAME%", name));
            var file = root.AddFile(name + ".vhdp");
            root.TopEntity = file;
            
            root.IncludePath("*.vhdp");

            _ = dockService.OpenFileAsync(file);
        }
        catch (Exception e)
        {
            logger.Error(e.Message, e);
        }
    }
}