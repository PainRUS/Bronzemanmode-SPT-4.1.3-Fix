using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

[Injectable(TypePriority = OnLoadOrder.Routers - 1)]
public sealed class BronzemanRaidRouter(
    JsonUtil jsonUtil,
    BronzemanRaidRouterCallback callback)
    : StaticRouter(jsonUtil, [
        new RouteAction<RaidSaveRequest>(
            "/raid/profile/save",
            async (url, info, sessionId, output, cancellationToken) =>
                await callback.Handle(info, sessionId.ToString(), output ?? string.Empty))
    ])
{
}

[Injectable]
public sealed class BronzemanRaidRouterCallback(
    BronzemanMod bronzemanMod,
    BronzemanConfig config)
{
    public ValueTask<string> Handle(
        RaidSaveRequest info,
        string sessionId,
        string output)
    {
        var unlock = info.Exit.Equals("runner", StringComparison.OrdinalIgnoreCase)
            ? config.Unlocks.RaidRunThrough
            : info.Exit.Equals("survived", StringComparison.OrdinalIgnoreCase)
                ? true
                : config.Unlocks.RaidDeath;

        if (!unlock)
            return new ValueTask<string>(output);

        var profile = bronzemanMod.GetPlayer(sessionId);
        var items = ExtractInventoryItems(info.Profile);
        bronzemanMod.UnlockItems(profile, items);

        return new ValueTask<string>(output);
    }

    private static IEnumerable<dynamic> ExtractInventoryItems(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object ||
            !profile.TryGetProperty("Inventory", out var inventory) ||
            inventory.ValueKind != JsonValueKind.Object ||
            !inventory.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.EnumerateArray().Select(x => new JsonRaidItem(x));
    }

    private sealed class JsonRaidItem(JsonElement value)
    {
        public string _tpl =>
            value.TryGetProperty("_tpl", out var tpl)
                ? tpl.GetString() ?? string.Empty
                : string.Empty;

        public JsonRaidUpdate? upd =>
            value.TryGetProperty("upd", out var upd)
                ? new JsonRaidUpdate(upd)
                : null;
    }

    private sealed class JsonRaidUpdate(JsonElement value)
    {
        public bool SpawnedInSession =>
            value.TryGetProperty("SpawnedInSession", out var spawned) &&
            spawned.ValueKind == JsonValueKind.True;
    }
}

public sealed record RaidSaveRequest : IRequestData
{
    public string Exit { get; init; } = string.Empty;
    public JsonElement Profile { get; init; }
}
