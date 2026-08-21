using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;

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

    public SptProfile GetPlayer(string sessionId)
    {
        if (!MongoId.IsValidMongoId(sessionId))
            throw new ArgumentException($"Invalid SPT session id: '{sessionId}'.", nameof(sessionId));

        return saveServer.GetProfile(new MongoId(sessionId));
    }

    public SptProfile GetPlayer(MongoId sessionId)
    {
        return saveServer.GetProfile(sessionId);
    }

    public void InitializePlayer(SptProfile profile)
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

    private static bool GetWipeState(SptProfile profile)
    {
        return profile.ProfileInfo?.IsWiped ?? true;
    }

    private static string GetUsername(SptProfile profile)
    {
        return profile.ProfileInfo?.Username ?? "unknown";
    }

    public List<string> GetOrCreateBronzemanItems(SptProfile profile)
    {
        var extensionData = GetExtensionData(GetPmcProfile(profile));

        if (!extensionData.TryGetValue(BronzemanItemsKey, out var value) || value is null)
        {
            var created = new List<string>();
            extensionData[BronzemanItemsKey] = created;
            LogInfo("[bronzeman] Adding characters.pmc.bronzemanItems.");
            return created;
        }

        if (value is List<string> list)
            return list;

        if (value is JsonElement element)
        {
            var parsed = element.Deserialize<List<string>>() ?? [];
            extensionData[BronzemanItemsKey] = parsed;
            return parsed;
        }

        if (value is JsonNode node)
        {
            var parsed = node.Deserialize<List<string>>() ?? [];
            extensionData[BronzemanItemsKey] = parsed;
            return parsed;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var parsed = enumerable.Cast<object>()
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToList();

            extensionData[BronzemanItemsKey] = parsed;
            return parsed;
        }

        throw new InvalidOperationException(
            $"Unexpected type for profile ExtensionData['{BronzemanItemsKey}']: {value.GetType().FullName}");
    }

    private static Dictionary<string, object> GetExtensionData(PmcData pmc)
    {
        // ExtensionData is injected into SPT model classes by the server's
        // Ceciler.JsonExtensionData build patch. Reflection keeps this mod
        // compatible with the public 4.1.2 NuGet reference while using the
        // patched property present at runtime in SPT 4.1.3.
        var property = pmc.GetType().GetProperty("ExtensionData")
                       ?? throw new InvalidOperationException("SPT PMC ExtensionData property is unavailable.");

        var value = property.GetValue(pmc);
        if (value is Dictionary<string, object> dictionary)
            return dictionary;

        if (value is null)
        {
            dictionary = new Dictionary<string, object>();
            property.SetValue(pmc, dictionary);
            return dictionary;
        }

        throw new InvalidOperationException(
            $"SPT PMC ExtensionData has an unexpected type: {value.GetType().FullName}");
    }

    public List<string> ItemCheck(SptProfile profile)
    {
        return BuildAvailableSet(profile).ToList();
    }

    private HashSet<string> BuildAvailableSet(SptProfile profile)
    {
        BuildCategories();

        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        available.UnionWith(GetOrCreateBronzemanItems(profile));
        available.UnionWith(categories);
        available.UnionWith(config.IgnoreItems);
        return available;
    }

    public bool CanPurchase(SptProfile profile, string itemId)
    {
        return CanPurchase(profile, itemId, BuildAvailableSet(profile), logDetails: config.Debug);
    }

    private bool CanPurchase(
        SptProfile profile,
        string itemId,
        HashSet<string> available,
        bool logDetails)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = itemId;

        while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
        {
            if (available.Contains(currentId))
            {
                if (logDetails)
                {
                    var unlockedItems = new HashSet<string>(
                        GetOrCreateBronzemanItems(profile),
                        StringComparer.OrdinalIgnoreCase);

                    var reason =
                        unlockedItems.Contains(currentId) ? "UNLOCKED" :
                        categories.Contains(currentId, StringComparer.OrdinalIgnoreCase) ? "CATEGORY" :
                        config.IgnoreItems.Contains(currentId, StringComparer.OrdinalIgnoreCase) ? "IGNORE" :
                        "ALLOWED";

                    LogInfo(
                        $"[bronzeman] Allowed ({itemId}) {itemHelper.GetItemName(itemId)} " +
                        $"via template/category ({currentId}) [{reason}]");
                }

                return true;
            }

            if (!TryGetTemplate(currentId, out var template) || template is null)
            {
                if (logDetails)
                {
                    LogWarning(
                        $"[bronzeman] TemplateTable does not contain ({currentId}) while checking ({itemId}).");
                }

                break;
            }

            var parentId = template.Parent.ToString();

            if (logDetails)
            {
                LogInfo($"[bronzeman] Template parent walk ({itemId}): ({currentId}) -> ({parentId})");
            }

            if (string.IsNullOrEmpty(parentId))
                break;

            currentId = parentId;
        }

        if (logDetails)
        {
            LogInfo(
                $"[bronzeman] Locked ({itemId}) {itemHelper.GetItemName(itemId)}; " +
                "no bronzemanItems/ignored item/ignored category in template parent chain.");
        }

        return false;
    }

    private bool TryGetTemplate(string itemId, out TemplateItem? template)
    {
        template = null;

        if (!MongoId.IsValidMongoId(itemId))
            return false;

        return templateTable.Items.TryGetValue(new MongoId(itemId), out template);
    }

    public void UnlockItems(SptProfile profile, IEnumerable<Item> items)
    {
        var bronzemanItems = GetOrCreateBronzemanItems(profile);
        var wishlist = GetWishlistDictionary(profile);
        var originalCount = bronzemanItems.Count;
        var materialized = items.ToList();

        if (config.Unlocks.FoundInRaidOnly)
        {
            var originalItemCount = materialized.Count;

            if (config.Debug)
            {
                foreach (var item in materialized.Where(item => item.Upd?.SpawnedInSession != true))
                {
                    var tpl = item.Template.ToString();
                    if (!string.IsNullOrEmpty(tpl))
                    {
                        LogInfo($"[bronzeman] Not Unlocking (not FIR): ({tpl}) {itemHelper.GetItemName(tpl)}");
                    }
                }
            }

            materialized = materialized
                .Where(item => item.Upd?.SpawnedInSession == true)
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
            var tpl = item.Template.ToString();
            if (string.IsNullOrEmpty(tpl))
            {
                LogWarning("[bronzeman] Inventory item had no readable template id, skipping.");
                continue;
            }

            UnlockTemplate(bronzemanItems, wishlist, tpl);

            if (config.Debug)
                LogInfo($"[bronzeman] Unlocking: ({tpl}) {itemHelper.GetItemName(tpl)}");
        }

        LogInfo(
            $"[bronzeman] Unlocked {bronzemanItems.Count - originalCount} items for {GetUsername(profile)}");
    }

    public void UnlockItemTemplates(SptProfile profile, IEnumerable<string> templates)
    {
        var bronzemanItems = GetOrCreateBronzemanItems(profile);
        var wishlist = GetWishlistDictionary(profile);
        var originalCount = bronzemanItems.Count;

        foreach (var tpl in templates.Where(tpl => !string.IsNullOrWhiteSpace(tpl)))
        {
            UnlockTemplate(bronzemanItems, wishlist, tpl);

            if (config.Debug)
                LogInfo($"[bronzeman] Unlocking template: ({tpl}) {itemHelper.GetItemName(tpl)}");
        }

        LogInfo(
            $"[bronzeman] Unlocked {bronzemanItems.Count - originalCount} item templates for {GetUsername(profile)}");
    }

    private void UnlockTemplate(
        List<string> bronzemanItems,
        Dictionary<MongoId, int> wishlist,
        string tpl)
    {
        if (!MongoId.IsValidMongoId(tpl))
        {
            LogWarning($"[bronzeman] Invalid template id '{tpl}', skipping unlock.");
            return;
        }

        if (!bronzemanItems.Contains(tpl, StringComparer.OrdinalIgnoreCase))
            bronzemanItems.Add(tpl);

        RemoveManagedWishlistEntry(wishlist, new MongoId(tpl));
    }

    public void CheckInventory(SptProfile profile)
    {
        try
        {
            var inventoryItems = GetPmcProfile(profile).Inventory?.Items;

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

    private static PmcData GetPmcProfile(SptProfile profile)
    {
        return profile.CharacterData?.PmcData
               ?? throw new InvalidOperationException("SPT profile CharacterData.PmcData is null.");
    }

    public Dictionary<MongoId, int> GetWishlist(SptProfile profile)
    {
        return GetWishlistDictionary(profile);
    }

    private static Dictionary<MongoId, int> GetWishlistDictionary(SptProfile profile)
    {
        var pmc = GetPmcProfile(profile);
        pmc.WishList ??= [];
        return pmc.WishList;
    }

    public bool IsBlockedByWishlist(SptProfile profile, string itemId)
    {
        try
        {
            if (!MongoId.IsValidMongoId(itemId))
                return false;

            var wishlist = GetWishlistDictionary(profile);
            return wishlist.TryGetValue(new MongoId(itemId), out var value)
                   && IsManagedWishlistValue(value);
        }
        catch (Exception ex)
        {
            LogError($"[bronzeman] Failed to check wishlist lock for {itemId}: {ex}");
            return false;
        }
    }

    private bool IsManagedWishlistValue(int value)
    {
        return value == config.WishlistType || value == config.GunsmithWishlistType;
    }

    private void RemoveManagedWishlistEntry(Dictionary<MongoId, int> wishlist, MongoId itemId)
    {
        if (wishlist.TryGetValue(itemId, out var value) && IsManagedWishlistValue(value))
            wishlist.Remove(itemId);
    }

    private bool IsInIgnoredCategory(string itemId)
    {
        BuildCategories();

        var ignoredCategories = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = itemId;

        while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
        {
            if (ignoredCategories.Contains(currentId))
                return true;

            if (!TryGetTemplate(currentId, out var template) || template is null)
                break;

            currentId = template.Parent.ToString();
        }

        return false;
    }

    private void RemoveIgnoredItemsFromManagedWishlist(Dictionary<MongoId, int> wishlist)
    {
        var keysToRemove = wishlist
            .Where(entry =>
            {
                if (!IsManagedWishlistValue(entry.Value))
                    return false;

                var itemId = entry.Key.ToString();
                return IsInIgnoredCategory(itemId)
                       || config.IgnoreItems.Contains(itemId, StringComparer.OrdinalIgnoreCase);
            })
            .Select(entry => entry.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            var itemId = key.ToString();
            wishlist.Remove(key);
            LogInfo(
                $"[bronzeman] Removed managed ignored item from wishlist: ({itemId}) {itemHelper.GetItemName(itemId)}");
        }

        if (keysToRemove.Count > 0)
            LogInfo($"[bronzeman] Removed {keysToRemove.Count} ignored Bronzeman wishlist entries.");
    }

    public void ApplyWishlistRules(SptProfile profile, TemplateTable _)
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

            RemoveIgnoredItemsFromManagedWishlist(wishlist);

            var available = BuildAvailableSet(profile);

            foreach (var entry in templateTable.Items)
            {
                var itemId = entry.Key.ToString();
                var props = entry.Value.Properties;

                if (props is null)
                {
                    RemoveManagedWishlistEntry(wishlist, entry.Key);
                    continue;
                }

                var name = props.Name;
                if (string.IsNullOrEmpty(name)
                    || string.Equals(name, "Dog tag", StringComparison.Ordinal)
                    || props.QuestItem == true)
                {
                    RemoveManagedWishlistEntry(wishlist, entry.Key);
                    continue;
                }

                if (CanPurchase(profile, itemId, available, logDetails: false))
                {
                    RemoveManagedWishlistEntry(wishlist, entry.Key);
                    continue;
                }

                wishlist[entry.Key] = config.WishlistType;
            }

            ApplyGunsmithWishlist(profile, wishlist, available);
            RemoveIgnoredItemsFromManagedWishlist(wishlist);

            LogInfo($"[bronzeman] Wishlist after processing: {wishlist.Count}");
        }
        catch (Exception ex)
        {
            LogError($"[bronzeman] Failed to apply wishlist rules: {ex}");
        }
    }

    private void ApplyGunsmithWishlist(
        SptProfile profile,
        Dictionary<MongoId, int> wishlist,
        HashSet<string> available)
    {
        if (config.GunsmithCount <= 0 || gunsmith.Quests.Count == 0)
            return;

        var pmcQuests = GetPmcProfile(profile).Quests ?? [];
        var completedQuestIds = pmcQuests
            .Where(quest => quest.Status == QuestStatusEnum.Success)
            .Select(quest => quest.QId.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var questCount = Math.Min(config.GunsmithCount, gunsmith.Quests.Count);

        for (var index = 0; index < questCount; index++)
        {
            var quest = gunsmith.Quests[index];
            var questId = ResolveGunsmithQuestId(quest);

            if (!string.IsNullOrEmpty(questId) && completedQuestIds.Contains(questId))
                continue;

            foreach (var itemId in quest.Items)
            {
                if (!MongoId.IsValidMongoId(itemId))
                {
                    LogWarning($"[bronzeman] Invalid Gunsmith item template id '{itemId}', skipping.");
                    continue;
                }

                if (CanPurchase(profile, itemId, available, logDetails: false))
                    continue;

                if (IsInIgnoredCategory(itemId)
                    || config.IgnoreItems.Contains(itemId, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                wishlist[new MongoId(itemId)] = config.GunsmithWishlistType;
            }
        }
    }

    private string ResolveGunsmithQuestId(GunsmithQuest quest)
    {
        var mapped = gunsmith.Gunsmith.FirstOrDefault(entry =>
            string.Equals(entry.Value, quest.Name, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(mapped.Key))
        {
            if (!string.Equals(mapped.Key, quest.Id, StringComparison.OrdinalIgnoreCase))
            {
                LogWarning(
                    $"[bronzeman] Gunsmith data id mismatch for '{quest.Name}': " +
                    $"quests[] has '{quest.Id}', map has '{mapped.Key}'. Using mapped id.");
            }

            return mapped.Key;
        }

        return quest.Id;
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
        if (enabled && !categories.Contains(id, StringComparer.OrdinalIgnoreCase))
            categories.Add(id);
    }
}
