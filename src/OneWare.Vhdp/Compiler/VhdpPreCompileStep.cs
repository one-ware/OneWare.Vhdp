using OneWare.Essentials.Services;
using OneWare.UniversalFpgaProjectSystem.Models;
using OneWare.UniversalFpgaProjectSystem.Services;

namespace OneWare.Vhdp.Compiler;

public class VhdpPreCompileStep(ILogger logger) : IFpgaPreCompileStep
{
    public string Name => "VHDP Compiler";

    public Task<bool> PerformPreCompileStepAsync(UniversalFpgaProjectRoot project, FpgaModel fpga)
    {
        logger.Warning("VHDP Compiler is not implemented yet!", null, true, true);
        return Task.FromResult(false);
    }
}