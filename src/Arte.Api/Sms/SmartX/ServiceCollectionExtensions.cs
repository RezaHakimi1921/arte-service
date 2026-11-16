using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arte.Api.Sms.SmartX;

public static class SmartXSmsServiceCollectionExtensions
{
    /// Always register; RoutingSmsProvider sends only when SMARTX_ALL or SMARTX_KEYS is set.
    public static IServiceCollection AddSmartXSms(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<SmartXSmsOptions>().Bind(config.GetSection(SmartXSmsOptions.Section));

        services.AddHttpClient(SmartXSmsClient.HttpClientName, (sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<SmartXSmsOptions>>().Value;
            http.BaseAddress = new Uri(o.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(Math.Max(5, o.RequestTimeoutSeconds));
            // SmartX nginx returns 403 when User-Agent is empty (HttpClient default).
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ArteService/1.0");
        });

        services.AddSingleton<SmartXTokenProvider>();
        services.TryAddSingleton<ISmsBatchLog, InMemorySmsBatchLog>();
        services.AddSingleton<SmartXSmsClient>();
        return services;
    }
}
