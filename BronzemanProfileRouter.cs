using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

[Injectable(TypePriority = OnLoadOrder.Routers - 1)]
public sealed class BronzemanProfileRouter(
    JsonUtil jsonUtil,
    BronzemanProfileRouterCallback callback)
    : StaticRouter(jsonUtil, [
        new RouteAction<EmptyRequestData>(
            "/client/game/profile/list",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.Handle(
                    sessionId,
                    output ?? string.Empty)
        )
    ])
{
}

[Injectable]
public sealed class BronzemanProfileRouterCallback(
    BronzemanMod bronzemanMod,
    BronzemanConfig config,
    TemplateTable templateTable,
    SaveServer saveServer)
{
    public async ValueTask<string> Handle(
        MongoId sessionId,
        string output)
    {
        if (!config.Unlocks.Inventory)
        {
            return output;
        }

        var profile = bronzemanMod.GetPlayer(sessionId.ToString());

        bronzemanMod.InitializePlayer(profile);
        bronzemanMod.ApplyWishlistRules(profile, templateTable);

        // SPT 4.1 keeps profiles in memory; explicitly persist the mutations
        // so bronzemanItems and WishList are written to user/profiles/<id>.json.
        await saveServer.SaveProfileAsync(sessionId);

        return output;
    }
}
