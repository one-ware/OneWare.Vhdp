using OneWare.UniversalFpgaProjectSystem.Models;
using OneWare.UniversalFpgaProjectSystem.Services;

namespace OneWare.Vhdp.Compiler;

public class VhdpPreCompileStep : IFpgaPreCompileStep
{
    public string Name => "VHDP Compiler";

    public Task PerformPreCompileStepAsync(UniversalFpgaProjectRoot project, FpgaModel fpga)
    {
        project.AddFile(Path.Combine("build", "test.vhd"), true);
        return Task.CompletedTask;
    }
}