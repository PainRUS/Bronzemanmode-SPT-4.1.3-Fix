using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using EFT.UI;
using SPT.Reflection.Patching;

namespace Bronzeman.Client;

/// <summary>
/// The server-side purchase guard rejects a locked transaction before native SPT
/// trading runs and sends a sentinel NotificationPopup through the existing SPT
/// websocket. EFT notification implementation type names are obfuscated and are
/// not stable compile-time API, so this patch deliberately avoids referencing
/// NotificationManagerClass, NotificationAbstractClass, or notification enums by
/// name.
///
/// Instead, the notification manager is discovered at runtime from the native
/// DisplayMessageNotification method shape. The manager's raw byte[] message
/// processor is patched before JSON notification parsing. Only Bronzeman sentinel
/// websocket messages are consumed; all unrelated EFT/SPT notifications continue
/// through the original pipeline unchanged.
/// </summary>
internal sealed class PurchaseBlockedNotificationPatch : ModulePatch
{
    private const string RussianSentinel = "b10c0ed00000000000000001";
    private const string EnglishSentinel = "b10c0ed00000000000000002";

    private const string RussianMessage = "Bronzemanmode:Товар не доступен к покупке";
    private const string EnglishMessage = "Bronzemanmode:Item is not available for purchase";

    private static readonly ConcurrentQueue<string> PendingMessages = new();
    private static MethodInfo? _displayMessageMethod;

    protected override MethodBase GetTargetMethod()
    {
        var assembly = typeof(TransferItemsScreen).Assembly;
        var types = GetLoadableTypes(assembly).ToArray();

        var allStaticMethods = types
            .SelectMany(type => GetMethodsSafe(
                type,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .ToArray();

        var namedDisplayCandidates = allStaticMethods
            .Where(method => string.Equals(
                method.Name,
                "DisplayMessageNotification",
                StringComparison.Ordinal))
            .Where(IsDisplayMessageMethodShape)
            .ToArray();

        MethodInfo[] displayCandidates;
        if (namedDisplayCandidates.Length > 0)
        {
            displayCandidates = namedDisplayCandidates;
        }
        else
        {
            // Fallback for builds where the method name itself is obfuscated.
            // The signature is distinctive: static void, string first argument,
            // then duration/icon enums, optionally followed by a nullable color.
            displayCandidates = allStaticMethods
                .Where(IsDisplayMessageMethodShape)
                .ToArray();
        }

        _displayMessageMethod = SelectDisplayMethod(displayCandidates);
        var managerType = _displayMessageMethod.DeclaringType
            ?? throw new InvalidOperationException(
                "Bronzeman resolved an EFT notification display method without a declaring type.");

        var rawMessageCandidates = GetMethodsSafe(
                managerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.ReturnType == typeof(void))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 1
                       && parameters[0].ParameterType == typeof(byte[]);
            })
            .ToArray();

        if (rawMessageCandidates.Length != 1)
        {
            var candidateNames = string.Join(
                ", ",
                rawMessageCandidates.Select(method => method.Name));

            throw new InvalidOperationException(
                $"Bronzeman could not uniquely resolve the EFT notification raw-message processor on " +
                $"{managerType.FullName}. Expected 1 void(byte[]) method, found " +
                $"{rawMessageCandidates.Length}. Candidates: {candidateNames}");
        }

        Plugin.Log.LogInfo(
            $"Bronzeman dynamically resolved EFT notification pipeline: " +
            $"manager={managerType.FullName}, raw={rawMessageCandidates[0].Name}, " +
            $"display={_displayMessageMethod.Name}.");

        return rawMessageCandidates[0];
    }

    [PatchPrefix]
    private static bool Prefix(ref byte[] __0)
    {
        if (__0 is null || __0.Length == 0)
            return true;

        try
        {
            var payload = Encoding.UTF8.GetString(__0);
            var lines = payload.Split('\n');
            var remainingLines = new List<string>(lines.Length);
            var intercepted = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                if (line.Contains(RussianSentinel, StringComparison.OrdinalIgnoreCase))
                {
                    PendingMessages.Enqueue(RussianMessage);
                    intercepted = true;
                    continue;
                }

                if (line.Contains(EnglishSentinel, StringComparison.OrdinalIgnoreCase))
                {
                    PendingMessages.Enqueue(EnglishMessage);
                    intercepted = true;
                    continue;
                }

                remainingLines.Add(line);
            }

            if (!intercepted)
                return true;

            // The native notification manager expects one compact JSON message per
            // line. If the websocket packet contained only our sentinel, consume
            // it completely. If unrelated notifications shared the packet, pass
            // those lines back to EFT unchanged.
            if (remainingLines.All(string.IsNullOrWhiteSpace))
                return false;

            __0 = Encoding.UTF8.GetBytes(string.Join("\n", remainingLines));
            return true;
        }
        catch (Exception exception)
        {
            // Notification interception is UX-only. Never break EFT's native
            // notification pipeline if parsing our transport unexpectedly fails.
            Plugin.Log.LogWarning(
                $"Bronzeman blocked-purchase websocket interception failed: {exception}");
            return true;
        }
    }

    /// <summary>
    /// Called from BaseUnityPlugin.Update so UI work is always performed on the
    /// Unity main thread rather than on the websocket callback thread.
    /// </summary>
    internal static void DisplayPendingNotifications()
    {
        while (PendingMessages.TryDequeue(out var message))
        {
            try
            {
                InvokeNativeToast(message);
                Plugin.Log.LogInfo(
                    $"Bronzeman blocked-purchase toast displayed: {message}");
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"Bronzeman failed to display blocked-purchase toast: {exception}");
            }
        }
    }

    private static MethodInfo SelectDisplayMethod(IReadOnlyCollection<MethodInfo> candidates)
    {
        if (candidates.Count == 0)
        {
            throw new MissingMethodException(
                "Bronzeman could not locate EFT's native DisplayMessageNotification-style method.");
        }

        // Prefer the known four-argument overload used by EFT/SPT mods:
        // (string, duration enum, icon enum, nullable text color).
        var preferred = candidates
            .Where(method => method.GetParameters().Length == 4)
            .ToArray();

        if (preferred.Length == 1)
            return preferred[0];

        if (candidates.Count == 1)
            return candidates.First();

        var descriptions = string.Join(
            "; ",
            candidates.Select(method =>
                $"{method.DeclaringType?.FullName}.{method.Name}" +
                $"({string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName))})"));

        throw new InvalidOperationException(
            $"Bronzeman found multiple EFT notification display candidates and cannot select safely: {descriptions}");
    }

    private static bool IsDisplayMessageMethodShape(MethodInfo method)
    {
        if (!method.IsStatic || method.ReturnType != typeof(void))
            return false;

        var parameters = method.GetParameters();
        if (parameters.Length is < 3 or > 4)
            return false;

        if (parameters[0].ParameterType != typeof(string))
            return false;

        var durationType = Nullable.GetUnderlyingType(parameters[1].ParameterType)
                           ?? parameters[1].ParameterType;
        var iconType = Nullable.GetUnderlyingType(parameters[2].ParameterType)
                       ?? parameters[2].ParameterType;

        return durationType.IsEnum && iconType.IsEnum;
    }

    private static void InvokeNativeToast(string message)
    {
        var method = _displayMessageMethod
            ?? throw new InvalidOperationException(
                "Bronzeman notification display method was not initialized.");

        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];

        arguments[0] = message;
        arguments[1] = ResolveEnumValue(parameters[1].ParameterType, "Default");
        arguments[2] = ResolveEnumValue(parameters[2].ParameterType, "Note");

        if (parameters.Length == 4)
            arguments[3] = null;

        method.Invoke(null, arguments);
    }

    private static object ResolveEnumValue(Type parameterType, string preferredName)
    {
        var enumType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (!enumType.IsEnum)
        {
            throw new InvalidOperationException(
                $"Bronzeman expected notification argument type {enumType.FullName} to be an enum.");
        }

        var name = Enum.GetNames(enumType)
            .FirstOrDefault(candidate => string.Equals(
                candidate,
                preferredName,
                StringComparison.OrdinalIgnoreCase));

        return name is not null
            ? Enum.Parse(enumType, name)
            : Enum.ToObject(enumType, 0);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static IEnumerable<MethodInfo> GetMethodsSafe(Type type, BindingFlags flags)
    {
        try
        {
            return type.GetMethods(flags);
        }
        catch
        {
            return Array.Empty<MethodInfo>();
        }
    }
}
