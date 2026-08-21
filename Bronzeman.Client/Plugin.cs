using BepInEx;
using BepInEx.Logging;

namespace Bronzeman.Client;

[BepInPlugin(ModGuid, PluginName, Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string ModGuid = "com.randek.bronzeman.client";
    public const string PluginName = "Bronzeman Client";
    public const string Version = "2.0.1";

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

        try
        {
            new PurchaseBlockedWarningSuppressionPatch().Enable();
            Logger.LogInfo("Bronzeman blocked-purchase warning suppression enabled.");
        }
        catch (System.Exception exception)
        {
            Logger.LogError($"Bronzeman client failed to apply blocked-purchase warning suppression patch: {exception}");
        }
    }

    private void Update()
    {
        // Websocket callbacks are not guaranteed to execute on Unity's main
        // thread. Drain Bronzeman notification requests here so the native EFT UI
        // notification method is always invoked safely from the Unity update loop.
        PurchaseBlockedNotificationPatch.DisplayPendingNotifications();
    }
}
