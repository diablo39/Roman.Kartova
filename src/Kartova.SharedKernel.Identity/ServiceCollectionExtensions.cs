using Duende.IdentityModel.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kartova.SharedKernel.Identity;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKeycloakAdminClient(
        this IServiceCollection services,
        IConfiguration config,
        string sectionName = "KartovaIdentity:Keycloak")
    {
        services.AddOptions<KeycloakAdminOptions>().Bind(config.GetSection(sectionName)).ValidateOnStart();

        services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl);
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
            var http = new HttpClient { BaseAddress = new Uri(opts.BaseUrl) };
            return new TokenClient(http, new TokenClientOptions
            {
                Address = $"{opts.BaseUrl}/realms/{opts.Realm}/protocol/openid-connect/token",
                ClientId = opts.AdminClientId,
                ClientSecret = opts.AdminClientSecret,
            });
        });

        return services;
    }
}
