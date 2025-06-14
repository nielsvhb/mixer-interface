using Microsoft.Extensions.Logging;
using Blazorise;
using Blazorise.Tailwind;
using Blazorise.Icons.FontAwesome;
using MixerInterface.Services;

namespace MixerInterface;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBlazorise();
        builder.Services.AddTailwindProviders();
        builder.Services.AddFontAwesomeIcons();
        builder.Services.AddSingleton<OscService>();
        builder.Services.AddSingleton<MixerScannerService>();


#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}