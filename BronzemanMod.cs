using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.DI.Annotations;

namespace Bronzeman;

[Injectable]
public sealed class BronzemanMod(
    ISptLogger<BronzemanMod> logger,
    SaveServer saveServer,
    ItemHelper itemHelper,
    TemplateTable templateTable,
    BronzemanConfig config,
    GunsmithConfig gunsmith)
{
    private const string BronzemanItemsKey = "bronzemanItems";

    private readonly List<string> categories = [];
    private bool categoriesBuilt;

    private void LogInfo(string message)
    {
        if (config.Debug)
            logger.Info(message);
    }

    private void LogWarning(string message)
    {
        if (config.Debug)
            logger.Warning(message);
    }

    private void LogError(string message)
    {
        if (config.Debug)
            logger.Error(message);
    }

    private void LogSuccess(string message)
    {
        if (config.Debug)
            logger.Success(message);
    }

    public dynamic GetPlayer(string sessionId)
    {
        return saveServer.GetProfile(sessionId);
    }

    public void InitializePlayer(dynamic profile)
    {
        if (GetWipeState(profile))
        {
            LogInfo("[bronzeman] InitializePlayer: wipe=true, skipping.");
            return;
        }

        LogInfo("[bronzeman] InitializePlayer: wipe=false, processing profile.");

        var items = GetOrCreateBronzemanItems(profile);

        LogInfo($"[bronzeman] bronzemanItems currently has {items.Count} entries.");

        CheckInventory(profile);

        LogInfo($"[bronzeman] bronzemanItems after inventory scan: {items.Count} entries.");
    }

    private static bool GetWipeState(dynamic profile)
    {
        try
        {
            return Convert.ToBoolean(profile.ProfileInfo.IsWiped);
        }
        catch
        {
            // During very early profile creation, fail safe and skip processing.
            return true;
        }
    }


    private static string GetUsername(dynamic profile)
    {
        try
        {
            return profile.ProfileInfo.Username?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static bool HasSpawnedInSession(dynamic item)
    {
        try
        {
            JsonElement json = JsonSerializer.SerializeToElement((object)item);

            // SPT C# models may serialize this as "Upd",
            // while raid/profile JSON normally uses "upd".
            if (!json.TryGetProperty("upd", out var upd) &&
                !json.TryGetProperty("Upd", out upd))
            {
                return false;
            }

            if (!upd.TryGetProperty("SpawnedInSession", out var spawned))
            {
                return false;
            }

            return spawned.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    public List<string> GetOrCreateBronzemanItems(dynamic profile)
    {
        var extensionData = GetExtensionData(profile);

        if (!extensionData.ContainsKey(BronzemanItemsKey))
        {
            var created = new List<string>();

            SetExtensionValue(extensionData, BronzemanItemsKey, created);

            LogInfo("[bronzeman] Adding characters.pmc.bronzemanItems.");

            return created;
        }

        var value = extensionData[BronzemanItemsKey];

        if (value is List<string> list)
            return list;

        if (value is JsonElement element)
        {
            var parsed = element.Deserialize<List<string>>() ?? [];
            SetExtensionValue(extensionData, BronzemanItemsKey, parsed);
            return parsed;
        }

        if (value is JsonNode node)
        {
            var parsed = node.Deserialize<List<string>>() ?? [];
            SetExtensionValue(extensionData, BronzemanItemsKey, parsed);
            return parsed;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var parsed = enumerable.Cast<object>()
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrEmpty(x))
                .Cast<string>()
                .ToList();

            SetExtensionValue(extensionData, BronzemanItemsKey, parsed);
            return parsed;
        }

        throw new InvalidOperationException(
            $"Unexpected type for profile.ExtensionData['{BronzemanItemsKey}']: {value?.GetType().FullName}");
    }

    private static Dictionary<string, object> GetExtensionData(dynamic profile)
    {
        dynamic pmc = GetPmcProfile(profile);
        object? value = pmc.ExtensionData;

        return value as Dictionary<string, object>
            ?? throw new InvalidOperationException(
                $"SPT PMC ExtensionData is unavailable or has an unexpected type: {value?.GetType().FullName}");
    }

    private static void SetExtensionValue(
        Dictionary<string, object> extensionData,
        string key,
        List<string> value)
    {
        extensionData[key] = value;
    }

    public List<string> ItemCheck(dynamic profile)
    {
        // BronzemanMod can be resolved as separate DI instances.
        // Build ignored categories on the exact instance performing this check.
        BuildCategories();

        List<string> unlocked = GetOrCreateBronzemanItems(profile);

        var available = new List<string>(unlocked.Count + categories.Count + config.IgnoreItems.Count);
        available.AddRange(unlocked);
        available.AddRange(categories);
        available.AddRange(config.IgnoreItems);

        return available
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetTemplateParentId(object item)
    {
        try
        {
            JsonElement json = JsonSerializer.SerializeToElement(item);

            if (json.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in json.EnumerateObject())
                {
                    if (!string.Equals(property.Name, "_parent", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(property.Name, "parent", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(property.Name, "parentId", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.String)
                        return property.Value.GetString();
                }
            }
        }
        catch
        {
        }

        try
        {
            var type = item.GetType();

            foreach (var propertyName in new[] { "Parent", "_parent", "ParentId" })
            {
                var property = type.GetProperty(propertyName);
                var value = property?.GetValue(item)?.ToString();

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch
        {
        }

        return null;
    }

    private dynamic? GetTemplateById(string id)
    {
        foreach (var entry in templateTable.Items)
        {
            if (string.Equals(
                    entry.Key.ToString(),
                    id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    public bool CanPurchase(dynamic profile, string itemId)
    {
        var available = new HashSet<string>(
            ItemCheck(profile),
            StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = itemId;

        while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
        {
            if (available.Contains(currentId))
            {
                if (config.Debug)
                {
                    var unlockedItems = new HashSet<string>(
                        GetOrCreateBronzemanItems(profile),
                        StringComparer.OrdinalIgnoreCase);

                    var ignoredCategories = new HashSet<string>(
                        categories,
                        StringComparer.OrdinalIgnoreCase);

                    var ignoredItems = new HashSet<string>(
                        config.IgnoreItems,
                        StringComparer.OrdinalIgnoreCase);

                    var reason =
                        unlockedItems.Contains(currentId) ? "UNLOCKED" :
                        ignoredCategories.Contains(currentId) ? "CATEGORY" :
                        ignoredItems.Contains(currentId) ? "IGNORE" :
                        "ALLOWED";

                    LogInfo(
                        $"[bronzeman] Allowed ({itemId}) {itemHelper.GetItemName(itemId)} " +
                        $"via template/category ({currentId}) [{reason}]");
                }

                return true;
            }

            dynamic? template = GetTemplateById(currentId);

            if (template is null)
            {
                if (config.Debug)
                {
                    LogWarning(
                        $"[bronzeman] TemplateTable does not contain ({currentId}) while checking ({itemId}).");
                }

                break;
            }

            string parentId = GetTemplateParentId((object)template) ?? string.Empty;

            if (config.Debug)
            {
                LogInfo(
                    $"[bronzeman] Template parent walk ({itemId}): ({currentId}) -> ({parentId})");
            }

            if (string.IsNullOrEmpty(parentId))
                break;

            currentId = parentId;
        }

        if (config.Debug)
        {
            LogInfo(
                $"[bronzeman] Locked ({itemId}) {itemHelper.GetItemName(itemId)}; " +
                "no bronzemanItems/ignored item/ignored category in template parent chain.");
        }

        return false;
    }

    private static string? GetItemTemplateId(dynamic item)
    {
        if (item is null)
            return null;

        try
        {
            JsonElement json = JsonSerializer.SerializeToElement((object)item);

            if (json.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in json.EnumerateObject())
                {
                    if (!string.Equals(property.Name, "_tpl", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(property.Name, "tpl", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(property.Name, "templateId", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.String)
                        return property.Value.GetString();
                }
            }
        }
        catch
        {
        }

        try
        {
            var type = ((object)item).GetType();

            foreach (var propertyName in new[] { "_tpl", "Tpl", "TemplateId", "Template" })
            {
                var property = type.GetProperty(propertyName);
                var value = property?.GetValue(item)?.ToString();

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch
        {
        }

        return null;
    }

    public void UnlockItems(dynamic profile, IEnumerable<dynamic> items)
    {
        var bronzemanItems = GetOrCreateBronzemanItems(profile);
        var wishlist = GetWishlist(profile);
        var originalCount = bronzemanItems.Count;

        var materialized = items.ToList();

        if (config.Unlocks.FoundInRaidOnly)
        {
            var originalItemCount = materialized.Count;

            if (config.Debug)
            {
                foreach (var item in materialized.Where(x =>
                    x is not null &&
                    !HasSpawnedInSession(x)))
                {
                    var tpl = GetItemTemplateId(item);
                    if (string.IsNullOrEmpty(tpl))
                        continue;

                    LogInfo(
                        $"[bronzeman] Not Unlocking (not FIR): ({tpl}) {itemHelper.GetItemName(tpl)}");
                }
            }

            materialized = materialized
                .Where(HasSpawnedInSession)
                .ToList();

            var ignored = originalItemCount - materialized.Count;
            if (ignored > 0)
            {
                LogInfo(
                    $"[bronzeman] Not unlocking {ignored} items as `config.unlocks.foundInRaidOnly` is true");
            }
        }

        foreach (var item in materialized)
        {
            if (item is null)
                continue;

            var tpl = GetItemTemplateId(item);

            if (string.IsNullOrEmpty(tpl))
            {
                LogWarning("[bronzeman] Inventory item had no readable template id, skipping.");
                continue;
            }

            if (!bronzemanItems.Contains(tpl))
            {
                bronzemanItems.Add(tpl);

                if (wishlist.ContainsKey(tpl))
                {
                    var wishlistValue = Convert.ToInt32(wishlist[tpl]);

                    if (wishlistValue == config.WishlistType ||
                        wishlistValue == config.GunsmithWishlistType)
                    {
                        wishlist.Remove(tpl);
                    }
                }
            }

            if (config.Debug)
            {
                LogInfo(
                    $"[bronzeman] Unlocking: ({tpl}) {itemHelper.GetItemName(tpl)}");
            }
        }

        LogInfo(
            $"[bronzeman] Unlocked {bronzemanItems.Count - originalCount} items for {GetUsername(profile)}");
    }

    public void UnlockItemTemplates(dynamic profile, IEnumerable<string> templates)
    {
        var items = templates.Select(tpl => new UnlockItem { _tpl = tpl });
        UnlockItems(profile, items);
    }

    public void CheckInventory(dynamic profile)
    {
        try
        {
            var inventoryItems = ((dynamic)GetPmcProfile(profile)).Inventory.Items;

            if (inventoryItems is null)
            {
                LogWarning("[bronzeman] PMC inventory items are null.");
                return;
            }

            UnlockItems(profile, inventoryItems);
        }
        catch (Exception ex)
        {
            LogError($"[bronzeman] Failed to scan PMC inventory: {ex}");
        }
    }

    private static object GetPmcProfile(dynamic profile)
    {
        object? pmc = profile.CharacterData?.PmcData;

        return pmc
            ?? throw new InvalidOperationException(
                "SPT profile CharacterData.PmcData is null.");
    }

    public dynamic GetWishlist(dynamic profile)
    {
        return ((dynamic)GetPmcProfile(profile)).WishList;
    }

    public bool IsBlockedByWishlist(dynamic profile, string itemId)
    {
        try
        {
            var wishlist = GetWishlistDictionary(profile);

            if (!wishlist.Contains(itemId))
                return false;

            var raw = wishlist[itemId];
            if (raw is null)
                return false;

            var value = Convert.ToInt32(raw);

            return value == config.WishlistType ||
                   value == config.GunsmithWishlistType;
        }
        catch (Exception ex)
        {
            LogError($"[bronzeman] Failed to check wishlist lock for {itemId}: {ex}");
            return false;
        }
    }

    private bool IsInIgnoredCategory(string itemId)
    {
        BuildCategories();

        var ignoredCategories = new HashSet<string>(
            categories,
            StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = itemId;

        while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
        {
            if (ignoredCategories.Contains(currentId))
                return true;

            dynamic? template = GetTemplateById(currentId);

            if (template is null)
                break;

            currentId = GetTemplateParentId((object)template) ?? string.Empty;
        }

        return false;
    }

    private void RemoveIgnoredCategoryItemsFromWishlist(IDictionary wishlist)
    {
        BuildCategories();

        var keysToRemove = new List<object>();

        foreach (DictionaryEntry entry in wishlist)
        {
            var itemId = entry.Key?.ToString();

            if (string.IsNullOrEmpty(itemId))
                continue;

            if (IsInIgnoredCategory(itemId))
                keysToRemove.Add(entry.Key);
        }

        foreach (var key in keysToRemove)
        {
            var itemId = key?.ToString() ?? string.Empty;
            wishlist.Remove(key);

            if (config.Debug)
            {
                LogInfo(
                    $"[bronzeman] Removed ignored-category item from wishlist: ({itemId}) {itemHelper.GetItemName(itemId)}");
            }
        }

        if (keysToRemove.Count > 0)
        {
            LogInfo(
                $"[bronzeman] Removed {keysToRemove.Count} ignored-category items from wishlist.");
        }
    }

    public void ApplyWishlistRules(dynamic profile, dynamic templateTable)
    {
        try
        {
            if (GetWipeState(profile))
            {
                LogInfo("[bronzeman] ApplyWishlistRules: wipe=true, skipping.");
                return;
            }
            LogInfo("[bronzeman] ApplyWishlistRules: wipe=false, processing.");
            var unlockedBeforeWishlist = GetOrCreateBronzemanItems(profile);
            LogInfo($"[bronzeman] Building wishlist from {unlockedBeforeWishlist.Count} bronzemanItems.");
            var wishlist = GetWishlistDictionary(profile);
            LogInfo($"[bronzeman] Wishlist before processing: {wishlist.Count}");

            // Remove stale entries that belong to ignoreCategories:true.
            RemoveIgnoredCategoryItemsFromWishlist(wishlist);
            var quests = gunsmith.Quests;
            var pmcQuests = ((dynamic)GetPmcProfile(profile)).Quests;

            foreach (var entry in templateTable.Items)
            {
                var itemId = entry.Key.ToString();
                var handbookItem = entry.Value;
                var props = handbookItem.Properties;

                var name = props.Name?.ToString();
                var questItem = GetBoolProperty(props, "QuestItem");

                if (string.IsNullOrEmpty(name) ||
                    string.Equals(name, "Dog tag", StringComparison.Ordinal) ||
                    questItem)
                    continue;

                // Items in ignoreCategories:true should never remain on the Bronzeman wishlist.
                if (IsInIgnoredCategory(itemId))
                {
                    if (wishlist.Contains(itemId))
                    {
                        wishlist.Remove(itemId);

                        if (config.Debug)
                        {
                            LogInfo(
                                $"[bronzeman] Removing ignored-category item from wishlist: ({itemId}) {itemHelper.GetItemName(itemId)}");
                        }
                    }

                    continue;
                }

                if (!CanPurchase(profile, itemId))
                {
                    wishlist[itemId] = config.WishlistType;
                }

                LogInfo($"[bronzeman] Wishlist after processing: {wishlist.Count}");
            }

            if (!QuestListContainsId(pmcQuests, quests[1].Id))
            {
                for (var z = 1; z < config.GunsmithCount && z < quests.Count; z++)
                {
                    foreach (var item in quests[z].Items)
                    {
                        if (!CanPurchase(profile, item))
                            wishlist[item] = config.GunsmithWishlistType;
                    }
                }

                // Gunsmith entries may have re-added ignored-category items.
                RemoveIgnoredCategoryItemsFromWishlist(wishlist);
                return;
            }

            for (var i = 0; i < quests.Count; i++)
            {
                foreach (dynamic pmcQuest in pmcQuests)
                {
                    if (!string.Equals(
                            GetStringProperty(pmcQuest, "Qid"),
                            quests[i].Id,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (GetIntProperty(pmcQuest, "Status") != 4)
                    {
                        for (var k = i; k < config.GunsmithCount && k < quests.Count; k++)
                        {
                            foreach (var item in quests[k].Items)
                            {
                                if (!CanPurchase(profile, item))
                                    wishlist[item] = config.GunsmithWishlistType;
                            }
                        }
                    }

                    break;
                }
            }

            // Final cleanup so ignoreCategories:true items never remain wishlisted.
            RemoveIgnoredCategoryItemsFromWishlist(wishlist);
        }
        catch (Exception ex)
        {
            LogError($"[bronzeman] Failed to apply wishlist rules: {ex}");
        }
    }

    private static IDictionary GetWishlistDictionary(dynamic profile)
    {
        object? wishlist = ((dynamic)GetPmcProfile(profile)).WishList;
        return wishlist as IDictionary
            ?? throw new InvalidOperationException("PMC WishList is not a dictionary.");
    }

    private static bool QuestListContainsId(dynamic quests, string id)
    {
        foreach (dynamic quest in quests)
        {
            if (string.Equals(GetStringProperty(quest, "Qid"), id, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? GetStringProperty(dynamic value, string propertyName)
    {
        try
        {
            return value.GetType().GetProperty(propertyName)?.GetValue(value)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static int GetIntProperty(dynamic value, string propertyName)
    {
        try
        {
            var property = value.GetType().GetProperty(propertyName);
            var raw = property?.GetValue(value);
            return raw is null ? 0 : Convert.ToInt32(raw);
        }
        catch
        {
            return 0;
        }
    }

    private static bool GetBoolProperty(dynamic value, string propertyName)
    {
        try
        {
            var property = value.GetType().GetProperty(propertyName);
            var raw = property?.GetValue(value);
            return raw is not null && Convert.ToBoolean(raw);
        }
        catch
        {
            return false;
        }
    }

    public void BuildCategories()
    {
        if (categoriesBuilt)
            return;

        categoriesBuilt = true;

        Add("543be5e94bdc2df1348b4568", config.IgnoreCategories.Keys);
        Add("5447e0e74bdc2d3c308b4567", config.IgnoreCategories.SpecialEquipment);
        Add("5448bf274bdc2dfc2f8b456a", config.IgnoreCategories.SecureContainers);
        Add("567849dd4bdc2d150f8b456e", config.IgnoreCategories.Maps);
        Add("543be5dd4bdc2deb348b4569", config.IgnoreCategories.Money);
        Add("543be6674bdc2df1348b4569", config.IgnoreCategories.FoodAndDrink);
        Add("5448eb774bdc2d0a728b4567", config.IgnoreCategories.BarterItems);
        Add("5447e1d04bdc2dff2f8b4567", config.IgnoreCategories.MeleeWeapons);
        Add("543be6564bdc2df4348b4568", config.IgnoreCategories.Throwables);
        Add("5448ecbe4bdc2d60728b4568", config.IgnoreCategories.InfoItems);
        Add("543be5664bdc2dd4348b4569", config.IgnoreCategories.Meds);
        Add("5448e53e4bdc2d60728b4567", config.IgnoreCategories.Backpacks);
        Add("5448e5284bdc2dcb718b4567", config.IgnoreCategories.Rigs);
        Add("5448fe124bdc2da5018b4567", config.IgnoreCategories.WeaponParts);
        Add("5422acb9af1c889c16000029", config.IgnoreCategories.Guns);
        Add("57bef4c42459772e8d35a53b", config.IgnoreCategories.ArmourHelmet);
        Add("5645bcb74bdc2ded0b8b4578", config.IgnoreCategories.Headphones);
        Add("5b3f15d486f77432d0509248", config.IgnoreCategories.ArmBands);
        Add("5485a8684bdc2da71d8b4567", config.IgnoreCategories.Ammo);
        Add("543be5cb4bdc2deb348b4568", config.IgnoreCategories.AmmoBoxes);
        if (config.IgnoreCategories.Containers)
        {
            Add("5795f317245977243854e041", true);
            Add("5671435f4bdc2d96058b4569", true);
        }
    }

    private void Add(string id, bool enabled)
    {
        if (enabled && !categories.Contains(id))
            categories.Add(id);
    }

    private sealed class UnlockItem
    {
        public string _tpl { get; init; } = string.Empty;
    }
}
