using Aiursoft.AiurProtocol;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Services;
using Microsoft.Extensions.Options;

namespace Aiursoft.Apkg.Sdk;

public class ServerAccess(
    AiurProtocolClient http,
    IOptions<ServerConfig> serverLocator)
{
    private readonly ServerConfig _serverLocator = serverLocator.Value;

    public Task<AiurResponse> InfoAsync()
    {
        var url = new AiurApiEndpoint(host: _serverLocator.Instance, route: "/api/info", param: new { });
        return http.Get<AiurResponse>(url);
    }
}
