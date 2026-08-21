using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

// Ragfair filtering must run after the route that produced the search response.
// SPT 4.1.x exposes both /client/ragfair/find and /client/ragfair/search.
// UI Fixes 6.x also exposes /uifixes/ragfair/find for slot-specific linked searches.
// UI Fixes registers its router at OnLoadOrder.Routers + 1, so Bronzeman uses +2
// to guarantee that its post-filter receives the completed UI Fixes flea response.
[Injectable(TypePriority = OnLoadOrder.Routers + 2)]
public sealed class BronzemanRagfairRouter(
    JsonUtil jsonUtil,
    BronzemanRagfairRouterCallback callback)
    : StaticRouter(jsonUtil, [
        new RouteAction<EmptyRequestData>(
            "/client/ragfair/find",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.Handle(
                    url,
                    sessionId,
                    output ?? string.Empty)),
        new RouteAction<EmptyRequestData>(
            "/client/ragfair/search",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.Handle(
                    url,
                    sessionId,
                    output ?? string.Empty)),
        new RouteAction<EmptyRequestData>(
            "/uifixes/ragfair/find",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.Handle(
                    url,
                    sessionId,
                    output ?? string.Empty))
    ])
{
}

[Injectable]
public sealed class BronzemanRagfairRouterCallback(
    BronzemanMod bronzemanMod,
    BronzemanConfig config)
{
    public ValueTask<string> Handle(
        string url,
        MongoId sessionId,
        string output)
    {
        if (!config.IncludeRagfair)
            return new ValueTask<string>(output);

        if (config.Debug)
        {
            Console.WriteLine(
                $"[bronzeman] Ragfair post-route handler called. URL={url}, outputLength={output.Length}");
        }

        if (string.IsNullOrWhiteSpace(output))
            return new ValueTask<string>(output);

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(output)?.AsObject();
        }
        catch
        {
            // Never break flea requests if SPT or another post-route returns
            // something unexpected.
            return new ValueTask<string>(output);
        }

        var data = root?["data"] as JsonObject;
        if (data is null)
            return new ValueTask<string>(output);

        var profile = bronzemanMod.GetPlayer(sessionId);

        // SPT returns the flea browser's left-side item tree as a dictionary
        // of template id -> offer count in data.categories. Remove locked root
        // templates here so unopened items do not appear as selectable entries
        // that only lead to an empty offer page.
        if (data["categories"] is JsonObject categories)
        {
            var originalCategoryCount = categories.Count;
            var categoryKeys = categories
                .Select(category => category.Key)
                .ToList();

            foreach (var templateId in categoryKeys)
            {
                if (!string.IsNullOrEmpty(templateId)
                    && bronzemanMod.CanPurchase(profile, templateId))
                {
                    continue;
                }

                categories.Remove(templateId);
            }

            if (config.Debug)
            {
                Console.WriteLine(
                    $"[bronzeman] Returning {categories.Count}/{originalCategoryCount} flea category entries using Bronzeman filter");
            }
        }

        if (data["offers"] is not JsonArray offers)
            return new ValueTask<string>(root?.ToJsonString() ?? output);

        var originalCount = offers.Count;

        for (var index = offers.Count - 1; index >= 0; index--)
        {
            if (offers[index] is not JsonObject offer)
                continue;

            if (offer["items"] is not JsonArray offerItems || offerItems.Count == 0)
            {
                offers.RemoveAt(index);
                continue;
            }

            // Find the actual root item in the offer item tree. Fall back to
            // the first item for compatibility with offers whose root has no
            // explicit hideout parent marker.
            var rootItem = offerItems
                .OfType<JsonObject>()
                .FirstOrDefault(item =>
                    string.Equals(
                        item["parentId"]?.ToString(),
                        "hideout",
                        StringComparison.OrdinalIgnoreCase))
                ?? offerItems.OfType<JsonObject>().FirstOrDefault();

            var rootTpl = rootItem?["_tpl"]?.ToString() ?? string.Empty;

            var allowed = config.RequireUnlockComponents
                ? offerItems
                    .OfType<JsonObject>()
                    .All(item =>
                    {
                        var tpl = item["_tpl"]?.ToString() ?? string.Empty;
                        return !string.IsNullOrEmpty(tpl)
                               && bronzemanMod.CanPurchase(profile, tpl);
                    })
                : !string.IsNullOrEmpty(rootTpl)
                  && bronzemanMod.CanPurchase(profile, rootTpl);

            if (allowed)
            {
                if (config.Debug)
                    Console.WriteLine($"[bronzeman] Keeping flea offer {rootTpl}");

                continue;
            }

            offers.RemoveAt(index);
        }

        if (config.Debug)
        {
            Console.WriteLine(
                $"[bronzeman] Returning {offers.Count}/{originalCount} flea offers using Bronzeman filter");
        }

        // SPT paginates before this post-route executes. We intentionally do
        // not rewrite offersCount because that field describes the server-side
        // search result set rather than only the current filtered page.
        return new ValueTask<string>(root?.ToJsonString() ?? output);
    }
}
