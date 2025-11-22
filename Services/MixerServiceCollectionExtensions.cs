using Eggbox.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Eggbox.Services;

public static class MixerServiceCollectionExtensions
{
    public static IServiceCollection AddMixerCore(this IServiceCollection services)
    {
        services.AddSingleton<MixerModel>();
        services.AddSingleton<MixerParser>();
        services.AddSingleton<MixerIO>();              
        services.AddSingleton<MixerTrafficLogService>();
        services.AddSingleton<MixerBroadcastScanner>(); 
        services.AddSingleton<Mixer>();

        return services;
    }
}