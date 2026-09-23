#if GLOADER_CLIENT
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria.GameContent.UI.States;
using Terraria.UI;

/// <summary>
/// Last-resort retail UI guard for Expanded Worlds.
///
/// UIWorldCreation normally builds its page after gloader installs Harmony
/// patches, so WorldSizeUiFix.cs can inject THICC through THICC 11 from
/// BuildPage(). If Terraria has already constructed that UI state before the mod
/// patch set is installed, however, the BuildPage postfix cannot retroactively
/// run. Draw is guaranteed to execute when the New World page is actually shown,
/// so this guard performs one idempotent recovery attempt for that live instance.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldCreationDrawGuardPatch
{
    private static readonly MethodInfo InjectMethod =
        AccessTools.Method(
            typeof(ExpandedWorldBuildPageSizeRowFix),
            "Inject",
            new[] { typeof(UIWorldCreation) });

    private static readonly FieldInfo OwnerField =
        AccessTools.Field(typeof(ExpandedWorldBuildPageSizeRowFix), "_owner");

    private static readonly FieldInfo ExpandedButtonsField =
        AccessTools.Field(typeof(ExpandedWorldBuildPageSizeRowFix), "_expandedButtons");

    private static readonly PropertyInfo ParentProperty =
        AccessTools.Property(typeof(UIElement), "Parent");

    private static UIWorldCreation _lastRecoveryOwner;

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate =>
                candidate.Name == "Draw" &&
                candidate.GetParameters().Length == 1);

        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "Draw(SpriteBatch)");

        return method;
    }

    [HarmonyPrefix]
    private static void Prefix(UIWorldCreation __instance)
    {
        if (__instance == null || IsInstalledFor(__instance))
            return;

        // Do not hammer reflection every frame if something genuinely fails.
        // A rebuilt UIWorldCreation instance gets its own recovery attempt.
        if (ReferenceEquals(__instance, _lastRecoveryOwner))
            return;

        _lastRecoveryOwner = __instance;

        try
        {
            if (InjectMethod == null)
                throw new MissingMethodException(
                    typeof(ExpandedWorldBuildPageSizeRowFix).FullName,
                    "Inject(UIWorldCreation)");

            InjectMethod.Invoke(null, new object[] { __instance });

            // A Draw-time recovery occurs after Terraria may already have done
            // the normal layout pass. Force the newly appended controls to get
            // calculated dimensions before this frame renders them.
            __instance.Recalculate();

            if (!IsInstalledFor(__instance))
            {
                throw new InvalidOperationException(
                    "Expanded Worlds injected a size row, but it was not attached to the live UIWorldCreation instance.");
            }

            Console.WriteLine("[Expanded Worlds] Draw guard verified the THICC world-size row on the live New World screen.");
        }
        catch (TargetInvocationException ex)
        {
            Exception inner = ex.InnerException ?? ex;
            Console.WriteLine("[Expanded Worlds] Draw guard could not recover world-size buttons: " + inner);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Expanded Worlds] Draw guard could not recover world-size buttons: " + ex);
        }
    }

    private static bool IsInstalledFor(UIWorldCreation owner)
    {
        if (owner == null || OwnerField == null || ExpandedButtonsField == null || ParentProperty == null)
            return false;

        try
        {
            UIWorldCreation installedOwner = OwnerField.GetValue(null) as UIWorldCreation;
            if (!ReferenceEquals(owner, installedOwner))
                return false;

            Array buttons = ExpandedButtonsField.GetValue(null) as Array;
            if (buttons == null || buttons.Length != ExpandedWorldMath.ExpandedPresetCount)
                return false;

            for (int i = 0; i < buttons.Length; i++)
            {
                UIElement button = buttons.GetValue(i) as UIElement;
                if (button == null || ParentProperty.GetValue(button, null) == null)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
#endif
