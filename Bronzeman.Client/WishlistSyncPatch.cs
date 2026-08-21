using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Bronzeman.Client;

/// <summary>
/// Bronzeman mutates the authoritative wishlist on the SPT server when a mail
/// attachment is claimed. EFT's item-event response does not carry wishlist
/// deltas, so the local Profile.WishlistManager remains stale until a profile
/// reload. Reconcile the local user-managed wishlist with the server snapshot
/// after the mail transfer operation queue has completed.
/// </summary>
internal sealed class WishlistSyncPatch : ModulePatch
{
    private const string WishlistRoute = "/bronzeman/client/wishlist";
    private const int RequestTimeoutMilliseconds = 5000;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.DeclaredMethod(typeof(TransferItemsScreen), nameof(TransferItemsScreen.Close));

    [HarmonyPriority(Priority.Last)]
    [PatchPostfix]
    private static async void Postfix(ItemUiContext ____itemUiContext)
    {
        try
        {
            if (____itemUiContext?.ClientSession is not ClientBackendSession session)
                return;

            // The transfer screen can close while inventory operations are still
            // queued. Wait until SPT has processed the mail Move/Split/Merge/
            // Transfer and Bronzeman's server post-router has saved the unlock.
            await session.FlushOperationQueue();
            await Task.Yield();

            var serverWishlist = await FetchServerWishlist(session);
            if (serverWishlist is null)
                return;

            ReconcileWishlist(session, serverWishlist);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Bronzeman wishlist synchronization failed: {exception}");
        }
    }

    private static async Task<Dictionary<string, int>?> FetchServerWishlist(ClientBackendSession session)
    {
        var completion = new TaskCompletionSource<Dictionary<string, int>?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var request = new SendRequest
            {
                Url = session._backendUrls.Main + WishlistRoute,
                Retries = SendRequest.NoRetries,
                HandlingMode = ERequestHandlingMode.Ignore,
            };

            var requestTask = session.method_5(
                request,
                new Callback<Dictionary<string, int>>(result =>
                {
                    if (!result.Succeed)
                    {
                        Plugin.Log.LogWarning(
                            $"Bronzeman wishlist sync request failed: {result.Error}");
                        completion.TrySetResult(null);
                        return;
                    }

                    completion.TrySetResult(result.Value ?? new Dictionary<string, int>());
                }));

            _ = requestTask.ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        Plugin.Log.LogWarning(
                            $"Bronzeman wishlist sync request faulted: " +
                            $"{task.Exception?.GetBaseException().Message}");
                    }

                    completion.TrySetResult(null);
                },
                TaskContinuationOptions.NotOnRanToCompletion);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Bronzeman wishlist sync request threw: {exception.Message}");
            return null;
        }

        if (await Task.WhenAny(
                completion.Task,
                Task.Delay(RequestTimeoutMilliseconds)) != completion.Task)
        {
            Plugin.Log.LogWarning("Bronzeman wishlist sync request timed out.");
            return null;
        }

        return await completion.Task;
    }

    private static void ReconcileWishlist(
        ClientBackendSession session,
        IReadOnlyDictionary<string, int> serverWishlist)
    {
        var profile = session.Profile;
        var manager = profile?.WishlistManager;
        if (manager is null)
            return;

        // UserItems contains the explicit server-backed wishlist. GetWishlist()
        // may additionally contain client-generated QoL/hideout entries, which
        // must not be removed by synchronization.
        var localWishlist = manager.UserItems.ToDictionary(
            entry => entry.Key,
            entry => entry.Value);

        var serverByMongoId = new Dictionary<MongoID, EWishlistGroup>();
        foreach (var entry in serverWishlist)
        {
            if (string.IsNullOrWhiteSpace(entry.Key)
                || entry.Key.Length != 24
                || !Enum.IsDefined(typeof(EWishlistGroup), entry.Value))
            {
                continue;
            }

            try
            {
                serverByMongoId[new MongoID(entry.Key)] = (EWishlistGroup)entry.Value;
            }
            catch
            {
                // Ignore malformed IDs rather than risking the EFT UI thread.
            }
        }

        var removed = 0;
        var added = 0;
        var changed = 0;

        foreach (var localEntry in localWishlist)
        {
            if (!serverByMongoId.TryGetValue(localEntry.Key, out var serverGroup))
            {
                manager.RemoveFromWishlist(localEntry.Key, simulate: false);
                removed++;
                continue;
            }

            if (localEntry.Value != serverGroup)
            {
                manager.ChangeItemGroup(localEntry.Key, serverGroup, simulate: false);
                changed++;
            }
        }

        foreach (var serverEntry in serverByMongoId)
        {
            if (localWishlist.ContainsKey(serverEntry.Key))
                continue;

            manager.AddToWishlist(serverEntry.Key, serverEntry.Value, simulate: false);
            added++;
        }

        Plugin.Log.LogInfo(
            $"Bronzeman wishlist synchronized after transfer: " +
            $"server={serverByMongoId.Count}, removed={removed}, " +
            $"added={added}, changed={changed}.");
    }
}
