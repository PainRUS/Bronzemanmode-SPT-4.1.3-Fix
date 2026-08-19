using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Bronzeman;

[Injectable]
public sealed class BronzemanQuestPatch : AbstractPatch
{
    private static BronzemanMod _bronzemanMod = default!;
    private static TemplateTable _templateTable = default!;
    private static ISptLogger<BronzemanQuestPatch> _logger = default!;
    private static BronzemanConfig _config = default!;

    public BronzemanQuestPatch(
        ISptLogger<BronzemanQuestPatch> logger,
        BronzemanMod bronzemanMod,
        TemplateTable templateTable,
        BronzemanConfig config)
    {
        _logger = logger;
        _bronzemanMod = bronzemanMod;
        _templateTable = templateTable;
        _config = config;
    }


    private static void LogInfo(string message)
    {
        if (_config.Debug)
            _logger.Info(message);
    }

    private static void LogWarning(string message)
    {
        if (_config.Debug)
            _logger.Warning(message);
    }

    private static void LogError(string message)
    {
        if (_config.Debug)
            _logger.Error(message);
    }

    private static void LogSuccess(string message)
    {
        if (_config.Debug)
            _logger.Success(message);
    }

    protected override MethodBase GetTargetMethod()
    {
        return typeof(QuestController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(x =>
                x.Name == nameof(QuestController.CompleteQuest) &&
                x.GetParameters().Length == 3);
    }

    [PatchPostfix]
    public static void Postfix(object __1, object __2)
    {
        if (!_config.Unlocks.Quests)
            return;

        try
        {
            dynamic request = __1;
            var questId = request.QuestId.ToString();
            var sessionId = __2?.ToString() ?? string.Empty;

            if (string.IsNullOrEmpty(sessionId))
            {
                LogError("[bronzeman] Quest reward unlock failed: session id was empty.");
                return;
            }

            var profile = _bronzemanMod.GetPlayer(sessionId);

            dynamic? profileQuest = null;
            dynamic pmc = profile.CharacterData.PmcData;

            foreach (dynamic q in pmc.Quests)
            {
                if (q.QId.ToString() == questId)
                {
                    profileQuest = q;
                    break;
                }
            }

            // Only unlock rewards after SPT actually marked the quest as Success.
            if (profileQuest is null || Convert.ToInt32(profileQuest.Status) != 4)
                return;

            var quest = TemplateTableLookup(questId);

            if (quest is null)
                return;

            var rewardTemplates = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            var rewards = quest.Rewards as System.Collections.IDictionary;

            if (rewards is null || !rewards.Contains("Success"))
                return;

            var successRewards = rewards["Success"] as System.Collections.IEnumerable;

            if (successRewards is null)
                return;

            foreach (dynamic reward in successRewards)
            {
                var type = GetPropertyString(reward, "Type");

                if (string.Equals(type, "Item", StringComparison.OrdinalIgnoreCase))
                {
                    // A normal item reward can contain multiple actual reward items.
                    // These are the items the player receives (often through mail).
                    var rewardItems = GetPropertyEnumerable(reward, "Items");

                    if (rewardItems is null)
                        continue;

                    foreach (var rewardItem in rewardItems)
                    {
                        var tpl = GetTemplateId(rewardItem);

                        if (!string.IsNullOrEmpty(tpl))
                            rewardTemplates.Add(tpl);
                    }

                    continue;
                }

                if (string.Equals(type, "AssortmentUnlock", StringComparison.OrdinalIgnoreCase))
                {
                    // AssortmentUnlock is NOT a normal item reward.
                    // `Target` is the _id of the specific item inside reward.Items
                    // whose _tpl is the trader template unlocked by this quest.
                    var targetId = GetPropertyString(reward, "Target");
                    var rewardItems = GetPropertyEnumerable(reward, "Items");

                    if (string.IsNullOrEmpty(targetId) || rewardItems is null)
                    {
                        LogWarning(
                            $"[bronzeman] Quest '{questId}' has an AssortmentUnlock reward without a readable Target/Items.");
                        continue;
                    }

                    string? unlockedTpl = null;

                    foreach (var rewardItem in rewardItems)
                    {
                        var rewardItemId = GetPropertyString(rewardItem, "Id")
                                           ?? GetPropertyString(rewardItem, "_id");

                        if (!string.Equals(
                                rewardItemId,
                                targetId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        unlockedTpl = GetTemplateId(rewardItem);
                        break;
                    }

                    if (string.IsNullOrEmpty(unlockedTpl))
                    {
                        LogWarning(
                            $"[bronzeman] Quest '{questId}' AssortmentUnlock target '{targetId}' had no matching template.");
                        continue;
                    }

                    rewardTemplates.Add(unlockedTpl);

                    if (_config.Debug)
                    {
                        LogInfo(
                            $"[bronzeman] Quest assortment unlock: target ({targetId}) -> tpl ({unlockedTpl}).");
                    }
                }
            }

            if (rewardTemplates.Count == 0)
                return;

            LogInfo(
                $"[bronzeman] Unlocking {rewardTemplates.Count} unique item templates from quest '{questId}'.");

            _bronzemanMod.UnlockItemTemplates(profile, rewardTemplates);
        }
        catch (Exception ex)
        {
            LogError($"[bronzeman] Quest reward unlock failed: {ex}");
        }
    }

    private static dynamic? TemplateTableLookup(string questId)
    {
        foreach (var pair in _templateTable.Quests)
        {
            if (pair.Key.ToString() == questId)
                return pair.Value;
        }

        return null;
    }

    private static string? GetPropertyString(object? value, string propertyName)
    {
        if (value is null)
            return null;

        try
        {
            var property = value.GetType().GetProperties()
                .FirstOrDefault(p =>
                    string.Equals(
                        p.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase));

            return property?.GetValue(value)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static System.Collections.IEnumerable? GetPropertyEnumerable(
        object? value,
        string propertyName)
    {
        if (value is null)
            return null;

        try
        {
            var property = value.GetType().GetProperties()
                .FirstOrDefault(p =>
                    string.Equals(
                        p.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase));

            return property?.GetValue(value) as System.Collections.IEnumerable;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetTemplateId(object? item)
    {
        if (item is null)
            return null;

        if (item is string s)
            return s;

        return GetPropertyString(item, "Template")
               ?? GetPropertyString(item, "_tpl")
               ?? GetPropertyString(item, "Tpl");
    }
}
