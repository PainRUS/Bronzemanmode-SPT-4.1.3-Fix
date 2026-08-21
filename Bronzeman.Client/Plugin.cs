using BepInEx;
using BepInEx.Logging;

namespace Bronzeman.Client;

[BepInPlugin(ModGuid, PluginName, Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string ModGuid = "com.randek.bronzeman.client";
    public const string PluginName = "Bronzeman Client";
    public const string Version = "2.0.3";

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;

        try
        {
            new WishlistSyncPatch().Enable();
            Logger.LogInfo("Bronzeman client wishlist synchronization enabled.");
        }
        catch (System.Exception exception)
        {
            Logger.LogError($"Bronzeman client failed to apply wishlist sync patch: {exception}");
        }

        try
        {
            new PurchaseBlockedNotificationPatch().Enable();
            Logger.LogInfo("Bronzeman blocked-purchase toast notification enabled.");
        }
        catch (System.Exception exception)
        {
            Logger.LogError($"Bronzeman client failed to apply blocked-purchase notification patch: {exception}");
        }
    }
}
