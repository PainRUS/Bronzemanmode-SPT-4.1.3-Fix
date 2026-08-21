using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

// Run after SPT's ItemEventStaticRouter so the mail item has already been
// transferred into the PMC inventory before Bronzeman unlocks its template.
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class BronzemanMailRouter(
    JsonUtil jsonUtil,
    BronzemanMailRouterCallback callback)
    : StaticRouter(jsonUtil, [
        new RouteAction<ItemEventRouterRequest>(
            "/client/game/profile/items/moving",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.Handle(
                    info,
                    sessionId,
                    output ?? string.Empty,
                    cancellationToken))
    ])
{
}

[Injectable]
public sealed class BronzemanMailRouterCallback(
    ISptLogger<BronzemanMailRouterCallback> logger,
    BronzemanMod bronzemanMod,
    BronzemanConfig config,
    SaveServer saveServer)
{
    public async ValueTask<string> Handle(
        ItemEventRouterRequest request,
        MongoId sessionId,
        string output,
        CancellationToken cancellationToken)
    {
        if (!config.Unlocks.Mail || request.Data is null || request.Data.Count == 0)
            return output;

        var profile = bronzemanMod.GetPlayer(sessionId);
        var inventoryItems = profile.CharacterData?.PmcData?.Inventory?.Items;

        if (inventoryItems is null)
            return output;

        var requestHasWarnings = HasWarnings(output);
        var templatesToUnlock = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedMailTransfer = false;

        foreach (var eventJson in request.Data)
        {
            if (!TryGetMailDestinationItemId(
                    eventJson,
                    requestHasWarnings,
                    out var action,
                    out var destinationItemId))
            {
                continue;
            }

            // Move/Split success is confirmed by the resulting item existing in
            // the PMC inventory. Merge/Transfer are only considered when SPT
            // returned no warnings for the item-event request.
            if (!CollectItemTreeTemplates(
                    inventoryItems,
                    destinationItemId,
                    templatesToUnlock))
            {
                if (config.Debug)
                {
                    logger.Warning(
                        $"[bronzeman] Mail {action} did not produce item {destinationItemId} " +
                        "in PMC inventory; skipping unlock.");
                }

                continue;
            }

            matchedMailTransfer = true;
        }

        if (!matchedMailTransfer || templatesToUnlock.Count == 0)
            return output;

        // Mail rewards are an explicit unlock source, just like quest rewards.
        // Do not apply foundInRaidOnly here: mail items are normally not FiR.
        bronzemanMod.UnlockItemTemplates(profile, templatesToUnlock);

        // UnlockItemTemplates also removes Bronzeman-managed wishlist entries.
        // Persist immediately so trader/flea checks on the next request see the
        // newly unlocked templates even if the client never reloads the profile.
        await saveServer.SaveProfileAsync(sessionId, cancellationToken);

        if (config.Debug)
        {
            logger.Info(
                $"[bronzeman] Mail transfer unlocked {templatesToUnlock.Count} " +
                $"item template(s) for session {sessionId}.");
        }

        return output;
    }

    private static bool TryGetMailDestinationItemId(
        JsonElement eventJson,
        bool requestHasWarnings,
        out string action,
        out MongoId destinationItemId)
    {
        action = string.Empty;
        destinationItemId = default;

        if (!eventJson.TryGetProperty("fromOwner", out var fromOwner)
            || fromOwner.ValueKind != JsonValueKind.Object
            || !fromOwner.TryGetProperty("type", out var ownerType)
            || !string.Equals(ownerType.GetString(), "mail", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!eventJson.TryGetProperty("Action", out var actionNode))
            return false;

        action = actionNode.GetString() ?? string.Empty;

        string? destinationProperty = action switch
        {
            ItemEventActions.MOVE => "item",
            ItemEventActions.SPLIT => "newItem",

            // The target stack already existed before Merge/Transfer, so an
            // overall warning makes success ambiguous. Stay conservative and
            // leave the item locked rather than risk a false unlock.
            ItemEventActions.MERGE when !requestHasWarnings => "with",
            ItemEventActions.TRANSFER when !requestHasWarnings => "with",
            _ => null,
        };

        if (destinationProperty is null
            || !eventJson.TryGetProperty(destinationProperty, out var itemIdNode))
        {
            return false;
        }

        var itemId = itemIdNode.GetString();
        if (string.IsNullOrWhiteSpace(itemId) || !MongoId.IsValidMongoId(itemId))
            return false;

        destinationItemId = new MongoId(itemId);
        return true;
    }

    private static bool CollectItemTreeTemplates(
        List<Item> inventoryItems,
        MongoId rootItemId,
        HashSet<string> templates)
    {
        var rootItem = inventoryItems.FirstOrDefault(item => item.Id == rootItemId);
        if (rootItem is null)
            return false;

        var pendingParents = new Queue<string>();
        var visitedItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pendingParents.Enqueue(rootItemId.ToString());

        while (pendingParents.Count > 0)
        {
            var currentId = pendingParents.Dequeue();
            if (!visitedItemIds.Add(currentId))
                continue;

            var currentItem = inventoryItems.FirstOrDefault(item =>
                string.Equals(item.Id.ToString(), currentId, StringComparison.OrdinalIgnoreCase));

            if (currentItem is not null)
            {
                var templateId = currentItem.Template.ToString();
                if (!string.IsNullOrWhiteSpace(templateId))
                    templates.Add(templateId);
            }

            foreach (var child in inventoryItems.Where(item =>
                         string.Equals(item.ParentId, currentId, StringComparison.OrdinalIgnoreCase)))
            {
                pendingParents.Enqueue(child.Id.ToString());
            }
        }

        return true;
    }

    private static bool HasWarnings(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return true;

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;

            if (root.TryGetProperty("err", out var errorCode)
                && errorCode.ValueKind == JsonValueKind.Number
                && errorCode.TryGetInt32(out var code)
                && code != 0)
            {
                return true;
            }

            if (!root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("warnings", out var warnings)
                || warnings.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return warnings.GetArrayLength() > 0;
        }
        catch
        {
            // Never infer success from an unexpected response shape.
            return true;
        }
    }
}
