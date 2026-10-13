using EggSharp.Api;
using EggSharp.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace EggLedger.Domain.Api;

public static class EggLedgerApi {
    public static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(100);

    public static readonly ApiPreset Preset = ApiPresets.EggLedger with {
        Profiles = [.. ApiPresets.EggLedger.Profiles.Select(p => p with { Timeout = HostTimeout })],
    };

    public static IServiceCollection AddEggLedgerApi(this IServiceCollection services, string origin, Uri? baseAddress = null) {
        services.RemoveAll<IEggIncApi>();
        services.RemoveAll<ApiClient>();
        services.AddLogging();
        services.AddEggSharpApi(
            Preset,
            _ => new ProtobufNetEiCodec(),
            _ => new AuxbrainSigner(null),
            _ => new VersionTripleSource(ApiClient.DefaultVersions),
            _ => new AuxbrainHosts(origin, null, baseAddress));
        services.AddScoped(sp => new ApiClient(sp.GetRequiredService<IEggIncApi>(), origin));
        return services;
    }

    public static ApiClient Create(HttpMessageHandler handler, string origin = ApiClient.DefaultApiPrefix, Uri? baseAddress = null) {
        var services = new ServiceCollection();
        services.AddEggLedgerApi(origin, baseAddress);
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));
        return new ApiClient(services.BuildServiceProvider().GetRequiredService<IEggIncApi>(), origin);
    }
}
