using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

// Run before the native profile-list route so the serialized profile returned
// to the client already contains the Bronzeman wishlist.
[Injectable(TypePriority = OnLoadOrder.Routers - 1)]
public sealed class BronzemanProfileRouter(
    JsonUtil jsonUtil,
    BronzemanProfileRouterCallback callback)
    : StaticRouter(jsonUtil, [
        new RouteAction<EmptyRequestData>(
            "/client/game/profile/list",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.Handle(sessionId, output ?? string.Empty, cancellationToken))
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
        string output,
        CancellationToken cancellationToken)
    {
        var profile = bronzemanMod.GetPlayer(sessionId);

        // Inventory unlock is optional. Wishlist construction is not: locked
        // items still need to be marked when inventory scanning is disabled.
        if (config.Unlocks.Inventory)
            bronzemanMod.InitializePlayer(profile);

        bronzemanMod.ApplyWishlistRules(profile, templateTable);

        // SPT 4.1 keeps profiles in memory; explicitly persist the mutations
        // so bronzemanItems and WishList are written to user/profiles/<id>.json.
        await saveServer.SaveProfileAsync(sessionId, cancellationToken);

        return output;
    }
}
