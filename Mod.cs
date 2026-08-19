using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace Bronzeman;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class Mod(
    ISptLogger<Mod> logger,
    BronzemanMod bronzemanMod,
    BronzemanConfig config)
    : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        bronzemanMod.BuildCategories();
        if (config.Debug)
            logger.Success("[bronzeman] Loaded.");
        return Task.CompletedTask;
    }
}
