using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

// Run after the native MatchStaticRouter. SPT applies the post-raid profile and
// saves it in its own /client/match/local/end handler; Bronzeman must mutate
// that final in-memory profile, not a pre-merge copy.
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class BronzemanRaidRouter(
    JsonUtil jsonUtil,
    BronzemanRaidRouterCallback callback)
    : StaticRouter(jsonUtil, [
        new RouteAction<EndLocalRaidRequestData>(
            "/client/match/local/end",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.Handle(info, sessionId, output ?? string.Empty, cancellationToken))
    ])
{
}

[Injectable]
public sealed class BronzemanRaidRouterCallback(
    BronzemanMod bronzemanMod,
    BronzemanConfig config,
    SaveServer saveServer)
{
    public async ValueTask<string> Handle(
        EndLocalRaidRequestData info,
        MongoId sessionId,
        string output,
        CancellationToken cancellationToken)
    {
        var result = info.Results;
        var raidProfile = result?.Profile;
        var inventoryItems = raidProfile?.Inventory?.Items;

        if (result?.Result is null || inventoryItems is null)
            return output;

        if (!ShouldUnlock(result.Result.Value))
            return output;

        var profile = bronzemanMod.GetPlayer(sessionId);
        bronzemanMod.UnlockItems(profile, inventoryItems);

        // Native SPT already saved the profile before this post-route runs.
        // Persist Bronzeman's extension-data and wishlist mutations explicitly.
        await saveServer.SaveProfileAsync(sessionId, cancellationToken);

        return output;
    }

    private bool ShouldUnlock(ExitStatus status)
    {
        return status switch
        {
            ExitStatus.SURVIVED => true,
            ExitStatus.TRANSIT => true,
            ExitStatus.RUNNER => config.Unlocks.RaidRunThrough,
            ExitStatus.KILLED => config.Unlocks.RaidDeath,
            ExitStatus.LEFT => config.Unlocks.RaidDeath,
            ExitStatus.MISSINGINACTION => config.Unlocks.RaidDeath,
            _ => false,
        };
    }
}
