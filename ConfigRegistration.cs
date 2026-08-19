using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SPTarkov.Server.Core.DI;

namespace Bronzeman;

public sealed class ConfigRegistration : IOnDIConstruct
{
    public static async Task OnDIConstructAsync(IServiceCollection services, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                     ?? throw new InvalidOperationException("Could not determine mod directory.");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var config = JsonSerializer.Deserialize<BronzemanConfig>(
            await File.ReadAllTextAsync(Path.Combine(folder, "config.json"), cancellationToken),
            options)
            ?? throw new InvalidOperationException("Could not deserialize Bronzeman config.");

        var gunsmith = JsonSerializer.Deserialize<GunsmithConfig>(
            await File.ReadAllTextAsync(Path.Combine(folder, "gunsmith.json"), cancellationToken),
            options)
            ?? throw new InvalidOperationException("Could not deserialize gunsmith.json.");

        services.AddSingleton(config);
        services.AddSingleton(gunsmith);
    }
}
