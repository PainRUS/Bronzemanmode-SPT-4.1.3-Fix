using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Bronzeman;

[Injectable(InjectionType.Singleton)]
public sealed class BronzemanLocaleState
{
    private readonly ConcurrentDictionary<string, string> locales = new(StringComparer.OrdinalIgnoreCase);

    public void SetLocale(MongoId sessionId, string localeId)
    {
        if (string.IsNullOrWhiteSpace(localeId))
            return;

        locales[sessionId.ToString()] = localeId.Trim();
    }

    public string GetPurchaseBlockedMessage(MongoId sessionId)
    {
        return locales.TryGetValue(sessionId.ToString(), out var localeId)
               && localeId.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? "Bronzemanmode:Товар не доступен к покупке"
            : "Bronzemanmode:Item is not available for purchase";
    }
}

// EFT requests /client/menu/locale/{language} for the currently selected UI locale.
// Record it after SPT has served the native locale response and leave the response untouched.
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class BronzemanLocaleRouter(
    JsonUtil jsonUtil,
    BronzemanLocaleState localeState)
    : DynamicRouter(jsonUtil, [
        new RouteAction<EmptyRequestData>(
            "/client/menu/locale/",
            async (url, info, sessionId, output, cancellationToken) =>
            {
                var localeId = url.Replace("/client/menu/locale/", string.Empty, StringComparison.OrdinalIgnoreCase);
                var queryIndex = localeId.IndexOfAny(['?', '#']);
                if (queryIndex >= 0)
                    localeId = localeId[..queryIndex];

                localeState.SetLocale(sessionId, localeId.Trim('/'));
                return output ?? string.Empty;
            })
    ])
{
}
