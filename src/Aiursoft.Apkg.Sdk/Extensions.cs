using Aiursoft.AiurProtocol;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Apkg.Sdk;

public static class Extensions
{
    public static IServiceCollection AddApkgServer(this IServiceCollection services, string endPointUrl)
    {
        services.AddAiurProtocolClient();
        services.Configure<ServerConfig>(options => options.Instance = endPointUrl);
        services.AddScoped<ServerAccess>();
        return services;
    }

    public static Version GetSdkVersion()
    {
        return typeof(Extensions).Assembly.GetName().Version!;
    }
}
