using System.Reflection;
using EFT.UI;
using SPT.Reflection.Patching;

namespace Bronzeman.Client;

/// <summary>
/// SPT 4.1.3 only permits BackendErrorCodes.NotEnoughSpace (1505) to remain an
/// ItemEvent warning without promoting the whole response to a backend error.
/// Bronzeman uses that code purely as a transport mechanism so the purchase
/// callback receives FailedResult and does not execute success UI behavior.
///
/// EFT would normally present a 1505 warning as a stash-space modal. This patch
/// suppresses presentation only when the warning object also contains Bronzeman's
/// unique marker. Native 1505 warnings and all unrelated inventory warnings keep
/// their stock behavior.
/// </summary>
internal sealed class PurchaseBlockedWarningSuppressionPatch : ModulePatch
{
    private const string BlockedPurchaseFailureMarker = "BRONZEMAN_PURCHASE_BLOCKED";

    protected override MethodBase GetTargetMethod()
    {
        var assembly = typeof(TransferItemsScreen).Assembly;
        var candidateTypes = GetLoadableTypes(assembly)
            .Where(ImplementsInventoryWarningContract)
            .ToArray();

        var methods = candidateTypes
            .SelectMany(type => GetMethodsSafe(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(IsTryGetMessageShape)
            .ToArray();

        var named = methods
            .Where(method => string.Equals(
                method.Name,
                "TryGetMessage",
                StringComparison.Ordinal))
            .ToArray();

        MethodInfo target;
        if (named.Length == 1)
        {
            target = named[0];
        }
        else if (methods.Length == 1)
        {
            target = methods[0];
        }
        else
        {
            var descriptions = string.Join(
                "; ",
                methods.Select(method =>
                    $"{method.DeclaringType?.FullName}.{method.Name}"));

            throw new InvalidOperationException(
                $"Bronzeman could not uniquely resolve EFT inventory-warning " +
                $"TryGetMessage implementation. Found {methods.Length}. " +
                $"Candidates: {descriptions}");
        }

        Plugin.Log.LogInfo(
            $"Bronzeman dynamically resolved inventory-warning presentation: " +
            $"{target.DeclaringType?.FullName}.{target.Name}.");

        return target;
    }

    [PatchPrefix]
    private static bool Prefix(
        object __instance,
        ref string __0,
        ref string __1,
        ref bool __result)
    {
        if (!ContainsBlockedPurchaseMarker(__instance))
            return true;

        // MainMenuController asks TryGetMessage whether a warning should be
        // displayed. Returning false suppresses only the visual warning. The
        // warning object's ToResult() is untouched, so the purchase callback still
        // receives FailedResult and trader/ragfair success logic remains disabled.
        __0 = string.Empty;
        __1 = string.Empty;
        __result = false;

        Plugin.Log.LogInfo(
            "Bronzeman suppressed the internal blocked-purchase inventory warning UI.");

        return false;
    }

    private static bool ContainsBlockedPurchaseMarker(object instance)
    {
        var type = instance.GetType();

        foreach (var property in GetPropertiesSafe(
                     type,
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.PropertyType != typeof(string)
                || property.GetIndexParameters().Length != 0
                || property.GetMethod is null)
            {
                continue;
            }

            try
            {
                if (string.Equals(
                        property.GetValue(instance) as string,
                        BlockedPurchaseFailureMarker,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // A warning implementation can contain unrelated computed
                // properties. Ignore getters that are unsafe during presentation.
            }
        }

        foreach (var field in GetFieldsSafe(
                     type,
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType != typeof(string))
                continue;

            try
            {
                if (string.Equals(
                        field.GetValue(instance) as string,
                        BlockedPurchaseFailureMarker,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // Reflection failure here must never affect native EFT warnings.
            }
        }

        return false;
    }

    private static bool ImplementsInventoryWarningContract(Type type)
    {
        if (!type.IsClass || type.IsAbstract)
            return false;

        try
        {
            return type.GetInterfaces().Any(interfaceType =>
                string.Equals(
                    interfaceType.Name,
                    "IInventoryWarning",
                    StringComparison.Ordinal)
                || GetMethodsSafe(
                        interfaceType,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(IsTryGetMessageShape));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTryGetMessageShape(MethodInfo method)
    {
        if (method.ReturnType != typeof(bool))
            return false;

        var parameters = method.GetParameters();
        if (parameters.Length != 2)
            return false;

        var stringByRef = typeof(string).MakeByRefType();
        return parameters[0].ParameterType == stringByRef
               && parameters[1].ParameterType == stringByRef;
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

    private static IEnumerable<PropertyInfo> GetPropertiesSafe(Type type, BindingFlags flags)
    {
        try
        {
            return type.GetProperties(flags);
        }
        catch
        {
            return Array.Empty<PropertyInfo>();
        }
    }

    private static IEnumerable<FieldInfo> GetFieldsSafe(Type type, BindingFlags flags)
    {
        try
        {
            return type.GetFields(flags);
        }
        catch
        {
            return Array.Empty<FieldInfo>();
        }
    }
}
