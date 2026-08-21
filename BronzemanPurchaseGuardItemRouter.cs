using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers.Ws;
using SPTarkov.Server.Core.Services.Ragfair;

namespace Bronzeman;

// ItemEventCallbacks selects the first ItemEventRouter that can handle an action.
// Register before SPT's native TradeItemEventRouter so Bronzeman can reject a
// locked purchase before TradeController mutates stock, money, or inventory.
// Allowed requests are delegated to the same native TradeCallbacks used by SPT.
[Injectable(TypePriority = OnLoadOrder.Routers - 1)]
public sealed class BronzemanPurchaseGuardItemRouter(
    TradeCallbacks tradeCallbacks,
    TraderAssortHelper traderAssortHelper,
    RagfairOfferService ragfairOfferService,
    SptWebSocketConnectionHandler webSocketConnectionHandler,
    BronzemanMod bronzemanMod,
    BronzemanConfig config,
    BronzemanLocaleState localeState,
    ISptLogger<BronzemanPurchaseGuardItemRouter> logger)
    : ItemEventRouter([
        new ItemRouteAction<ProcessBaseTradeRequestData>(
            ItemEventActions.TRADING_CONFIRM,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await HandleTraderTrade(
                    tradeCallbacks,
                    traderAssortHelper,
                    webSocketConnectionHandler,
                    bronzemanMod,
                    config,
                    localeState,
                    logger,
                    pmcData,
                    body,
                    sessionId,
                    output)),
        new ItemRouteAction<ProcessRagfairTradeRequestData>(
            ItemEventActions.RAGFAIR_BUY_OFFER,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await HandleRagfairTrade(
                    tradeCallbacks,
                    ragfairOfferService,
                    webSocketConnectionHandler,
                    bronzemanMod,
                    config,
                    localeState,
                    logger,
                    pmcData,
                    body,
                    sessionId,
                    output))
    ])
{
    private static readonly MongoId RussianBlockedNotificationId = new("b10c0ed00000000000000001");
    private static readonly MongoId EnglishBlockedNotificationId = new("b10c0ed00000000000000002");

    private static async ValueTask<ItemEventRouterResponse> HandleTraderTrade(
        TradeCallbacks tradeCallbacks,
        TraderAssortHelper traderAssortHelper,
        SptWebSocketConnectionHandler webSocketConnectionHandler,
        BronzemanMod bronzemanMod,
        BronzemanConfig config,
        BronzemanLocaleState localeState,
        ISptLogger<BronzemanPurchaseGuardItemRouter> logger,
        PmcData pmcData,
        ProcessBaseTradeRequestData request,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        // Sales and unknown trade types remain entirely native SPT behavior.
        if (!string.Equals(request.Type, ItemEventActions.BUY_FROM_TRADER, StringComparison.OrdinalIgnoreCase)
            || request is not ProcessBuyTradeRequestData buyRequest
            || !ShouldGuardTrader(config, buyRequest.TransactionId))
        {
            return await tradeCallbacks.ProcessTrade(pmcData, request, sessionId);
        }

        var assort = traderAssortHelper.GetAssort(sessionId, buyRequest.TransactionId);
        var purchaseItems = assort.Items.GetItemWithChildren(buyRequest.ItemId);

        // Preserve native SPT error handling for stale/invalid assort IDs.
        if (purchaseItems.Count == 0)
            return await tradeCallbacks.ProcessTrade(pmcData, request, sessionId);

        var profile = bronzemanMod.GetPlayer(sessionId);
        if (IsPurchaseAllowed(bronzemanMod, config, profile, purchaseItems, buyRequest.ItemId))
            return await tradeCallbacks.ProcessTrade(pmcData, request, sessionId);

        if (config.Debug)
        {
            logger.Warning(
                $"[bronzeman] Blocked locked trader purchase before SPT processing. " +
                $"Trader={buyRequest.TransactionId}, assort={buyRequest.ItemId}.");
        }

        await SendBlockedPurchaseNotification(
            webSocketConnectionHandler,
            localeState,
            sessionId,
            logger,
            config.Debug);

        // Deliberately return the untouched ItemEvent response. A warning/error
        // makes EFT show a critical modal and can eject the player to the main
        // menu. The purchase itself has already been rejected because native SPT
        // trade processing is never entered.
        return output;
    }

    private static async ValueTask<ItemEventRouterResponse> HandleRagfairTrade(
        TradeCallbacks tradeCallbacks,
        RagfairOfferService ragfairOfferService,
        SptWebSocketConnectionHandler webSocketConnectionHandler,
        BronzemanMod bronzemanMod,
        BronzemanConfig config,
        BronzemanLocaleState localeState,
        ISptLogger<BronzemanPurchaseGuardItemRouter> logger,
        PmcData pmcData,
        ProcessRagfairTradeRequestData request,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        if (!config.IncludeRagfair || request.Offers is null || request.Offers.Count == 0)
            return await tradeCallbacks.ProcessRagfairTrade(pmcData, request, sessionId);

        var profile = bronzemanMod.GetPlayer(sessionId);

        // Preflight every offer before delegating any part of the transaction to
        // SPT. One locked offer rejects the whole batch, preventing partial buys.
        foreach (var requestedOffer in request.Offers)
        {
            if (string.IsNullOrWhiteSpace(requestedOffer.Id)
                || !MongoId.IsValidMongoId(requestedOffer.Id))
            {
                // Preserve native handling for malformed/stale requests.
                return await tradeCallbacks.ProcessRagfairTrade(pmcData, request, sessionId);
            }

            var offer = ragfairOfferService.GetOfferByOfferId(new MongoId(requestedOffer.Id));
            if (offer?.Items is null || offer.Items.Count == 0)
            {
                // Native SPT owns OfferNotFound/out-of-stock behavior.
                return await tradeCallbacks.ProcessRagfairTrade(pmcData, request, sessionId);
            }

            if (IsPurchaseAllowed(bronzemanMod, config, profile, offer.Items, offer.Root))
                continue;

            if (config.Debug)
            {
                logger.Warning(
                    $"[bronzeman] Blocked locked flea purchase before SPT processing. " +
                    $"Offer={offer.Id}, root={offer.Root}.");
            }

            await SendBlockedPurchaseNotification(
                webSocketConnectionHandler,
                localeState,
                sessionId,
                logger,
                config.Debug);

            return output;
        }

        return await tradeCallbacks.ProcessRagfairTrade(pmcData, request, sessionId);
    }

    private static bool IsPurchaseAllowed(
        BronzemanMod bronzemanMod,
        BronzemanConfig config,
        SptProfile profile,
        IReadOnlyCollection<Item> purchaseItems,
        MongoId rootItemId)
    {
        var rootItem = purchaseItems.FirstOrDefault(item => item.Id == rootItemId)
                       ?? purchaseItems.FirstOrDefault();

        if (rootItem is null || !bronzemanMod.CanPurchase(profile, rootItem.Template.ToString()))
            return false;

        if (!config.RequireUnlockComponents)
            return true;

        return purchaseItems.All(item =>
        {
            var templateId = item.Template.ToString();
            return !string.IsNullOrWhiteSpace(templateId)
                   && bronzemanMod.CanPurchase(profile, templateId);
        });
    }

    private static bool ShouldGuardTrader(BronzemanConfig config, MongoId traderId)
    {
        if (config.AllTraders)
            return true;

        var traderIdString = traderId.ToString();
        return config.Traders.Contains(traderIdString, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task SendBlockedPurchaseNotification(
        SptWebSocketConnectionHandler webSocketConnectionHandler,
        BronzemanLocaleState localeState,
        MongoId sessionId,
        ISptLogger<BronzemanPurchaseGuardItemRouter> logger,
        bool debug)
    {
        if (!webSocketConnectionHandler.IsWebSocketConnected(sessionId))
        {
            if (debug)
                logger.Warning("[bronzeman] Purchase blocked, but no active EFT websocket was available for the toast notification.");

            return;
        }

        var notification = new WsNotificationPopup
        {
            EventType = NotificationEventType.NotificationPopup,
            EventIdentifier = new MongoId(),
            Image = string.Empty,
            Message = localeState.IsRussian(sessionId)
                ? RussianBlockedNotificationId
                : EnglishBlockedNotificationId,
        };

        await webSocketConnectionHandler.SendMessageAsync(sessionId, notification);
    }
}
