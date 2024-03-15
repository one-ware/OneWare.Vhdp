﻿using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;
using OneWare.UniversalFpgaProjectSystem.Services;
using OneWare.Vhdp.Templates;
using Prism.Ioc;
using Prism.Modularity;

namespace OneWare.Vhdp;

public class OneWareVhdpModule : IModule
{
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
    }

    public void OnInitialized(IContainerProvider containerProvider)
    {
        containerProvider.Resolve<ILanguageManager>().RegisterTextMateLanguage("vhdp", "avares://OneWare.Vhdp/Assets/vhdp.tmLanguage.json", ".vhdp");
        containerProvider.Resolve<IErrorService>().RegisterErrorSource("VHDP");
        
        containerProvider.Resolve<ILanguageManager>().RegisterService(typeof(LanguageServiceVhdp),true, ".vhdp");

        containerProvider.Resolve<FpgaService>().RegisterTemplate<VhdpBlinkTemplate>();
    }
}