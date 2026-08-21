using System.Text.Json.Serialization;

namespace Bronzeman;

public sealed class BronzemanConfig
{
    [JsonPropertyName("unlocks")]
    public UnlockConfig Unlocks { get; set; } = new();

    [JsonPropertyName("hideItems")]
    public bool HideItems { get; set; } = true;

    [JsonPropertyName("allTraders")]
    public bool AllTraders { get; set; }

    [JsonPropertyName("traders")]
    public List<string> Traders { get; set; } = [];

    [JsonPropertyName("includeRagfair")]
    public bool IncludeRagfair { get; set; } = true;

    [JsonPropertyName("ignoreCategories")]
    public IgnoreCategoriesConfig IgnoreCategories { get; set; } = new();

    [JsonPropertyName("ignoreItems")]
    public List<string> IgnoreItems { get; set; } = [];

    [JsonPropertyName("requireUnlockComponents")]
    public bool RequireUnlockComponents { get; set; } = true;

    [JsonPropertyName("debug")]
    public bool Debug { get; set; }

    [JsonPropertyName("wishlisttype")]
    public int WishlistType { get; set; } = 4;

    [JsonPropertyName("gunsmith")]
    public int GunsmithWishlistType { get; set; } = 3;

    [JsonPropertyName("gunsmithcount")]
    public int GunsmithCount { get; set; } = 25;
}

public sealed class UnlockConfig
{
    [JsonPropertyName("raidRunThrough")]
    public bool RaidRunThrough { get; set; } = true;

    [JsonPropertyName("raidDeath")]
    public bool RaidDeath { get; set; }

    [JsonPropertyName("inventory")]
    public bool Inventory { get; set; } = true;

    [JsonPropertyName("mail")]
    public bool Mail { get; set; } = true;

    [JsonPropertyName("quests")]
    public bool Quests { get; set; } = true;

    [JsonPropertyName("foundInRaidOnly")]
    public bool FoundInRaidOnly { get; set; }
}

public sealed class IgnoreCategoriesConfig
{
    public bool Keys { get; set; } = true;
    public bool SpecialEquipment { get; set; } = true;
    public bool SecureContainers { get; set; } = true;
    public bool Maps { get; set; } = true;
    public bool Money { get; set; } = true;
    public bool FoodAndDrink { get; set; }
    public bool BarterItems { get; set; }
    public bool MeleeWeapons { get; set; }
    public bool Throwables { get; set; }
    public bool InfoItems { get; set; }
    public bool Meds { get; set; }
    public bool Backpacks { get; set; }
    public bool Rigs { get; set; }
    public bool WeaponParts { get; set; }
    public bool Guns { get; set; }
    public bool ArmourHelmet { get; set; }
    public bool Headphones { get; set; }
    public bool ArmBands { get; set; }
    public bool Ammo { get; set; }
    public bool AmmoBoxes { get; set; }
    public bool Containers { get; set; } = true;
}

public sealed class GunsmithConfig
{
    [JsonPropertyName("gunsmith")]
    public Dictionary<string, string> Gunsmith { get; set; } = [];

    [JsonPropertyName("quests")]
    public List<GunsmithQuest> Quests { get; set; } = [];
}

public sealed class GunsmithQuest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = [];
}
