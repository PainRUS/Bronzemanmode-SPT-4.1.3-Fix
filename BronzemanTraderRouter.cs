using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class BronzemanTraderRouter(
    JsonUtil jsonUtil,
    BronzemanMod bronzemanMod,
    BronzemanConfig config)
    : DynamicRouter(jsonUtil, [
        new RouteAction<EmptyRequestData>(
            "/client/trading/api/getTraderAssort",
            async (url, info, sessionId, output, cancellationToken) =>
                await Handle(
                    url,
                    config,
                    bronzemanMod,
                    sessionId.ToString(),
                    output ?? string.Empty))
    ])
{
    private static ValueTask<string> Handle(
        string url,
        BronzemanConfig config,
        BronzemanMod bronzemanMod,
        string sessionId,
        string output)
    {
        if (config.Debug) Console.WriteLine(
            $"[bronzeman] Trader post-route handler called. URL={url}, outputLength={output?.Length ?? 0}");

        if (string.IsNullOrWhiteSpace(output))
        {
            if (config.Debug) Console.WriteLine("[bronzeman] Trader post-route output was empty; nothing to filter.");
            return new ValueTask<string>(output);
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(output)?.AsObject();
        }
        catch
        {
            return new ValueTask<string>(output);
        }

        if (root is null)
            return new ValueTask<string>(output);

        JsonObject? data = null;
        JsonArray? items = null;

        if (root["items"] is JsonArray directItems)
        {
            data = root;
            items = directItems;
        }
        else if (root["data"] is JsonObject wrappedData &&
                 wrappedData["items"] is JsonArray wrappedItems)
        {
            data = wrappedData;
            items = wrappedItems;
        }

        if (data is null || items is null)
        {
            if (config.Debug) Console.WriteLine(
                $"[bronzeman] Trader route matched, but no assort items array found. URL: {url}");

            return new ValueTask<string>(output);
        }

        var profile = bronzemanMod.GetPlayer(sessionId);

        if (config.Debug) Console.WriteLine(
            "[bronzeman] Trader filter using CanPurchase() so ignored parent categories are respected.");

        var allItems = items
            .OfType<JsonObject>()
            .ToList();

        var byId = allItems
            .Where(x => !string.IsNullOrEmpty(x["_id"]?.ToString()))
            .ToDictionary(
                x => x["_id"]!.ToString(),
                x => x,
                StringComparer.OrdinalIgnoreCase);

        var rootItems = allItems
            .Where(x => string.Equals(
                x["parentId"]?.ToString(),
                "hideout",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        var originalRootCount = rootItems.Count;

        var rootIdsToRemove = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rootItem in rootItems)
        {
            var rootId = rootItem["_id"]?.ToString() ?? string.Empty;
            var rootTpl = rootItem["_tpl"]?.ToString() ?? string.Empty;

            if (string.IsNullOrEmpty(rootId) ||
                string.IsNullOrEmpty(rootTpl))
            {
                continue;
            }

            var offerAllowed = bronzemanMod.CanPurchase(profile, rootTpl);

            if (offerAllowed && config.RequireUnlockComponents)
            {
                foreach (var child in GetOfferTree(rootId, allItems))
                {
                    var childTpl = child["_tpl"]?.ToString() ?? string.Empty;

                    if (string.IsNullOrEmpty(childTpl) ||
                        !bronzemanMod.CanPurchase(profile, childTpl))
                    {
                        offerAllowed = false;
                        break;
                    }
                }
            }

            if (!offerAllowed)
            {
                rootIdsToRemove.Add(rootId);
            }
        }

        if (config.HideItems)
        {
            var allIdsToRemove = new HashSet<string>(
                rootIdsToRemove,
                StringComparer.OrdinalIgnoreCase);

            foreach (var rootId in rootIdsToRemove)
            {
                foreach (var child in GetOfferTree(rootId, allItems))
                {
                    var childId = child["_id"]?.ToString();

                    if (!string.IsNullOrEmpty(childId))
                        allIdsToRemove.Add(childId);
                }
            }

            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i] is not JsonObject item)
                    continue;

                var id = item["_id"]?.ToString() ?? string.Empty;

                if (allIdsToRemove.Contains(id))
                    items.RemoveAt(i);
            }

            RemoveDictionaryEntries(data, "barter_scheme", rootIdsToRemove);
            RemoveDictionaryEntries(data, "barterScheme", rootIdsToRemove);
            RemoveDictionaryEntries(data, "loyal_level_items", rootIdsToRemove);
            RemoveDictionaryEntries(data, "loyalLevelItems", rootIdsToRemove);
        }
        else
        {
            foreach (var node in items)
            {
                if (node is not JsonObject item)
                    continue;

                var id = item["_id"]?.ToString() ?? string.Empty;

                if (!rootIdsToRemove.Contains(id))
                    continue;

                var upd = item["upd"] as JsonObject ?? new JsonObject();

                upd["UnlimitedCount"] = false;
                upd["StackObjectsCount"] = 0;

                item["upd"] = upd;
            }
        }

        var remainingRootCount = items.Count(node =>
            node is JsonObject obj &&
            string.Equals(
                obj["parentId"]?.ToString(),
                "hideout",
                StringComparison.OrdinalIgnoreCase));

        if (config.Debug) Console.WriteLine(
            $"[bronzeman] Returning {remainingRootCount}/{originalRootCount} trader offers using Bronzeman allow-list.");

        return new ValueTask<string>(root.ToJsonString());
    }

    private static IEnumerable<JsonObject> GetOfferTree(
        string rootId,
        List<JsonObject> allItems)
    {
        var result = new List<JsonObject>();
        var knownParents = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            rootId
        };

        var changed = true;

        while (changed)
        {
            changed = false;

            foreach (var item in allItems)
            {
                var id = item["_id"]?.ToString() ?? string.Empty;
                var parentId = item["parentId"]?.ToString() ?? string.Empty;

                if (string.IsNullOrEmpty(id) ||
                    string.IsNullOrEmpty(parentId) ||
                    !knownParents.Contains(parentId) ||
                    knownParents.Contains(id))
                {
                    continue;
                }

                knownParents.Add(id);
                result.Add(item);
                changed = true;
            }
        }

        return result;
    }

    private static void RemoveDictionaryEntries(
        JsonObject data,
        string propertyName,
        HashSet<string> rootIds)
    {
        if (data[propertyName] is not JsonObject dictionary)
            return;

        foreach (var rootId in rootIds)
            dictionary.Remove(rootId);
    }
}
