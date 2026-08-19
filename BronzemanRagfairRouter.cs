using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class BronzemanRagfairRouter(
    JsonUtil jsonUtil,
    BronzemanRagfairRouterCallback callback)
    : StaticRouter(jsonUtil, [
        new RouteAction<EmptyRequestData>(
            "/client/ragfair/find",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.Handle(
                    url,
                    sessionId.ToString(),
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
        string sessionId,
        string output)
    {
        if (config.Debug) Console.WriteLine(
            $"[bronzeman] Ragfair post-route handler called. URL={url}, outputLength={output?.Length ?? 0}");
        if (!config.IncludeRagfair)
        {
            return new ValueTask<string>(output);
        }

        // Never try to parse an empty response.
        if (string.IsNullOrWhiteSpace(output))
        {
            return new ValueTask<string>(output);
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(output)?.AsObject();
        }
        catch
        {
            // Do not break flea requests if SPT/another mod returns
            // something that is not JSON.
            return new ValueTask<string>(output);
        }

        var data = root?["data"]?.AsObject();

        if (data is null || data["offers"] is not JsonArray offers)
        {
            return new ValueTask<string>(output);
        }

        var profile = bronzemanMod.GetPlayer(sessionId);

        var originalCount = offers.Count;

        for (var i = offers.Count - 1; i >= 0; i--)
        {
            if (offers[i] is not JsonObject offer)
                continue;

            var offerItems = offer["items"] as JsonArray;

            if (offerItems is null || offerItems.Count == 0)
            {
                offers.RemoveAt(i);
                continue;
            }

            // Do not rely on offer["root"]. Find the actual root item from
            // the offer item tree. Flea root items normally have parentId=hideout.
            var rootItem = offerItems
                .OfType<JsonObject>()
                .FirstOrDefault(item =>
                    string.Equals(
                        item["parentId"]?.ToString(),
                        "hideout",
                        StringComparison.OrdinalIgnoreCase))
                ?? offerItems.OfType<JsonObject>().FirstOrDefault();

            var rootTpl = rootItem?["_tpl"]?.ToString() ?? string.Empty;

            bool allowed;

            if (config.RequireUnlockComponents)
            {
                allowed = offerItems
                    .OfType<JsonObject>()
                    .All(item =>
                    {
                        var tpl = item["_tpl"]?.ToString() ?? string.Empty;

                        return !string.IsNullOrEmpty(tpl) &&
                               bronzemanMod.CanPurchase(profile, tpl);
                    });
            }
            else
            {
                allowed =
                    !string.IsNullOrEmpty(rootTpl) &&
                    bronzemanMod.CanPurchase(profile, rootTpl);
            }

            if (allowed)
            {
                if (config.Debug)
                {
                    if (config.Debug) Console.WriteLine(
                        $"[bronzeman] Keeping flea offer {rootTpl}");
                }

                continue;
            }

            

            offers.RemoveAt(i);
        }

        var availableCount = offers.Count;

        if (config.Debug) Console.WriteLine(
            $"[bronzeman] Returning {availableCount}/{originalCount} flea offers using Bronzeman filter");

        return new ValueTask<string>(
            root?.ToJsonString() ?? output);
    }

    private static bool GetBool(JsonNode? node)
    {
        return node is not null &&
               bool.TryParse(node.ToString(), out var value) &&
               value;
    }
}
