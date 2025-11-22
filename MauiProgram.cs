using Microsoft.Extensions.Logging;
using Blazorise;
using Blazorise.Tailwind;
using Blazorise.Icons.FontAwesome;
using Eggbox.Models;
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
        
        builder.Services.AddMixerCore();

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Unhandled Exception: {ex?.Message}");
            Console.ResetColor();
        };
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}