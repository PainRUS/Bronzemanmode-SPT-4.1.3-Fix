using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

/// <summary>
/// Exposes the authoritative PMC wishlist to the optional Bronzeman client
/// companion. Item-event profileChanges do not contain wishlist deltas, so
/// server-side Bronzeman unlocks otherwise leave EFT's in-memory
/// WishlistManager stale until the profile is reloaded.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class BronzemanWishlistSyncRouter(
    JsonUtil jsonUtil,
    HttpResponseUtil httpResponseUtil,
    BronzemanMod bronzemanMod)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<EmptyRequestData>(
            "/bronzeman/client/wishlist",
            async (_, _, sessionId, _, _) =>
            {
                var profile = bronzemanMod.GetPlayer(sessionId);
                var wishlist = bronzemanMod.GetWishlist(profile)
                    .ToDictionary(entry => entry.Key.ToString(), entry => entry.Value);

                return httpResponseUtil.GetBody(wishlist);
            })
    ])
{
}
