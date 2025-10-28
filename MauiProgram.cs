using Microsoft.Extensions.Logging;
using Blazorise;
using Blazorise.Tailwind;
using Blazorise.Icons.FontAwesome;
using Eggbox.Services;

namespace Eggbox;

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
        builder.Services.AddSingleton<MixerConnectorService>();
        builder.Services.AddSingleton<BandStateService>();

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"🔥 Unhandled Exception: {ex}");
        };
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}