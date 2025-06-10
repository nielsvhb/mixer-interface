using Microsoft.Extensions.Logging;
using Blazorise;
using Blazorise.Tailwind;
using Blazorise.Icons.FontAwesome;
using MixerInterface.Interfaces;
using MixerInterface.Services;

namespace MixerInterface;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Console.WriteLine("HELLO WORLD");
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBlazorise();
        builder.Services.AddTailwindProviders();
        builder.Services.AddFontAwesomeIcons();
        builder.Services.AddSingleton<IBandMemberStore, PreferencesBandMemberStore>();
        builder.Services.AddSingleton<OscService>();


#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}