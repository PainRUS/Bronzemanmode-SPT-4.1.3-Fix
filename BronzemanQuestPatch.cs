using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
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

    protected override MethodBase GetTargetMethod()
    {
        return typeof(QuestController).GetMethod(
                   nameof(QuestController.CompleteQuest),
                   BindingFlags.Instance | BindingFlags.Public,
                   binder: null,
                   types:
                   [
                       typeof(PmcData),
                       typeof(CompleteQuestRequestData),
                       typeof(MongoId)
                   ],
                   modifiers: null)
               ?? throw new MissingMethodException(
                   typeof(QuestController).FullName,
                   nameof(QuestController.CompleteQuest));
    }

    [PatchPostfix]
    public static void Postfix(CompleteQuestRequestData __1, MongoId __2)
    {
        if (!_config.Unlocks.Quests)
            return;

        try
        {
            var profile = _bronzemanMod.GetPlayer(__2);
            var pmc = profile.CharacterData?.PmcData;

            if (pmc is null)
            {
                LogError("[bronzeman] Quest reward unlock failed: PMC profile is null.");
                return;
            }

            var profileQuest = pmc.Quests?.FirstOrDefault(quest => quest.QId == __1.QuestId);

            // Only unlock rewards after SPT actually marked the quest as Success.
            if (profileQuest?.Status != QuestStatusEnum.Success)
                return;

            if (!_templateTable.Quests.TryGetValue(__1.QuestId, out var quest)
                || quest.Rewards is null
                || !quest.Rewards.TryGetValue(QuestStatusEnum.Success.ToString(), out var rewards))
            {
                LogWarning(
                    $"[bronzeman] Quest '{__1.QuestId}' completed but no static Success rewards were found.");
                return;
            }

            var gameVersion = pmc.Info?.GameVersion;
            var rewardTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var reward in rewards)
            {
                if (!RewardAppliesToGameEdition(reward, gameVersion))
                    continue;

                switch (reward.Type)
                {
                    case RewardType.Item:
                        AddItemRewardTemplates(reward, rewardTemplates);
                        break;

                    case RewardType.AssortmentUnlock:
                        AddAssortmentUnlockTemplate(__1.QuestId, reward, rewardTemplates);
                        break;
                }
            }

            if (rewardTemplates.Count == 0)
                return;

            LogInfo(
                $"[bronzeman] Unlocking {rewardTemplates.Count} unique item templates from quest '{__1.QuestId}'.");

            // Template unlocks intentionally bypass foundInRaidOnly. They are
            // granted by quest completion, not obtained as physical raid loot.
            _bronzemanMod.UnlockItemTemplates(profile, rewardTemplates);
        }
        catch (Exception ex)
        {
            LogError($"[bronzeman] Quest reward unlock failed: {ex}");
        }
    }

    private static bool RewardAppliesToGameEdition(Reward reward, string? gameVersion)
    {
        if (string.IsNullOrEmpty(gameVersion))
            return true;

        if (reward.AvailableInGameEditions?.Count > 0
            && !reward.AvailableInGameEditions.Contains(gameVersion))
        {
            return false;
        }

        if (reward.NotAvailableInGameEditions?.Count > 0
            && reward.NotAvailableInGameEditions.Contains(gameVersion))
        {
            return false;
        }

        return true;
    }

    private static void AddItemRewardTemplates(
        Reward reward,
        HashSet<string> rewardTemplates)
    {
        if (reward.Items is null)
            return;

        foreach (var rewardItem in reward.Items)
        {
            var templateId = rewardItem.Template.ToString();
            if (!string.IsNullOrEmpty(templateId))
                rewardTemplates.Add(templateId);
        }
    }

    private static void AddAssortmentUnlockTemplate(
        MongoId questId,
        Reward reward,
        HashSet<string> rewardTemplates)
    {
        if (reward.Items is null || string.IsNullOrEmpty(reward.Target))
        {
            LogWarning(
                $"[bronzeman] Quest '{questId}' has an AssortmentUnlock reward without readable Target/Items.");
            return;
        }

        var targetItem = reward.Items.FirstOrDefault(item =>
            string.Equals(
                item.Id.ToString(),
                reward.Target,
                StringComparison.OrdinalIgnoreCase));

        if (targetItem is null)
        {
            LogWarning(
                $"[bronzeman] Quest '{questId}' AssortmentUnlock target '{reward.Target}' had no matching reward item.");
            return;
        }

        var templateId = targetItem.Template.ToString();
        if (string.IsNullOrEmpty(templateId))
            return;

        rewardTemplates.Add(templateId);

        LogInfo(
            $"[bronzeman] Quest assortment unlock: target ({reward.Target}) -> tpl ({templateId}).");
    }
}
