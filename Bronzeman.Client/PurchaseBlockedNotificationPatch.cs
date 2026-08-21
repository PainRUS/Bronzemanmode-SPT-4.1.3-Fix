using System.Reflection;
using EFT.Communications;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Bronzeman.Client;

/// <summary>
/// The server-side purchase guard rejects a locked transaction before native SPT
/// trading runs. It then sends a sentinel NotificationPopup over the existing SPT
/// websocket. Intercept only those sentinel notifications and render them through
/// EFT's non-modal DisplayMessageNotification API instead of allowing the normal
/// NotificationPopup path to create a disruptive popup.
///
/// EFT notification implementation types are obfuscated and their concrete names
/// are not stable compile-time API. Resolve the notification argument type from
/// NotificationManagerClass.OnNotificationReceived at runtime instead of naming
/// NotificationAbstractClass directly.
/// </summary>
internal sealed class PurchaseBlockedNotificationPatch : ModulePatch
{
    private const string RussianSentinel = "b10c0ed00000000000000001";
    private const string EnglishSentinel = "b10c0ed00000000000000002";

    private const string RussianMessage = "Bronzemanmode:Товар не доступен к покупке";
    private const string EnglishMessage = "Bronzemanmode:Item is not available for purchase";

    protected override MethodBase GetTargetMethod()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var notificationEvent = typeof(NotificationManagerClass).GetEvent(
            "OnNotificationReceived",
            flags)
            ?? throw new MissingMemberException(
                typeof(NotificationManagerClass).FullName,
                "OnNotificationReceived");

        var invokeMethod = notificationEvent.EventHandlerType?.GetMethod("Invoke")
            ?? throw new MissingMethodException(
                notificationEvent.EventHandlerType?.FullName,
                "Invoke");

        var invokeParameters = invokeMethod.GetParameters();
        if (invokeParameters.Length != 1)
        {
            throw new InvalidOperationException(
                "Bronzeman expected OnNotificationReceived to have exactly one notification argument.");
        }

        var notificationType = invokeParameters[0].ParameterType;

        var candidates = typeof(NotificationManagerClass)
            .GetMethods(flags)
            .Where(method =>
            {
                if (method.ReturnType != typeof(void))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 1
                       && parameters[0].ParameterType == notificationType;
            })
            .ToArray();

        if (candidates.Length != 1)
        {
            throw new InvalidOperationException(
                $"Bronzeman could not uniquely resolve NotificationManagerClass notification-dispatch method. " +
                $"Expected 1 candidate for {notificationType.FullName}, found {candidates.Length}.");
        }

        return candidates[0];
    }

    [PatchPrefix]
    private static bool Prefix(object __0)
    {
        if (__0 is null || !TryResolveSentinel(__0, out var russian))
            return true;

        var message = russian ? RussianMessage : EnglishMessage;

        NotificationManagerClass.DisplayMessageNotification(
            message,
            ENotificationDurationType.Default,
            ENotificationIconType.Note,
            null);

        Plugin.Log.LogInfo($"Bronzeman blocked-purchase toast displayed: {message}");

        // Suppress the sentinel NotificationPopup itself. Only the lightweight
        // DisplayMessageNotification toast should reach the UI.
        return false;
    }

    private static bool TryResolveSentinel(object notification, out bool russian)
    {
        if (ContainsSentinel(notification, RussianSentinel))
        {
            russian = true;
            return true;
        }

        if (ContainsSentinel(notification, EnglishSentinel))
        {
            russian = false;
            return true;
        }

        russian = false;
        return false;
    }

    private static bool ContainsSentinel(object instance, string sentinel)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();

        foreach (var field in type.GetFields(flags))
        {
            try
            {
                if (Matches(field.GetValue(instance), sentinel))
                    return true;
            }
            catch
            {
                // Reflection against obfuscated EFT notification types must not
                // break the notification pipeline.
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length != 0 || property.GetMethod is null)
                continue;

            try
            {
                if (Matches(property.GetValue(instance), sentinel))
                    return true;
            }
            catch
            {
                // Ignore inaccessible/throwing getters from unrelated notification types.
            }
        }

        return false;
    }

    private static bool Matches(object? value, string sentinel)
    {
        return value is not null
               && string.Equals(
                   value.ToString(),
                   sentinel,
                   StringComparison.OrdinalIgnoreCase);
    }
}
