#if GLOADER_CLIENT
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.UI;

/// <summary>
/// Extends Terraria 1.4.5.8's New World size selector after BuildPage() has created
/// the three vanilla controls. Terraria's original Small / Medium / Large row is
/// left completely untouched. Expanded physical presets are rendered in their own
/// rows beneath it, and the vanilla rows below the size selector are shifted down
/// by the exact extra row height.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldBuildPageSizeRowFix
{
    private const int ExpandedButtonsPerRow = 6;
    private const float VanillaRowStridePixels = 48f;
    private const float ExpandedButtonGapPixels = 6f;

    private static readonly FieldInfo SizeButtonsField =
        AccessTools.Field(typeof(UIWorldCreation), "_sizeButtons");

    private static readonly FieldInfo DifficultyButtonsField =
        AccessTools.Field(typeof(UIWorldCreation), "_difficultyButtons");

    private static readonly FieldInfo EvilButtonsField =
        AccessTools.Field(typeof(UIWorldCreation), "_evilButtons");

    private static readonly FieldInfo DescriptionTextField =
        AccessTools.Field(typeof(UIWorldCreation), "_descriptionText");

    private static readonly MethodInfo UpdatePreviewPlateMethod =
        AccessTools.Method(typeof(UIWorldCreation), "UpdatePreviewPlate", Type.EmptyTypes);

    private static readonly PropertyInfo ParentProperty =
        AccessTools.Property(typeof(UIElement), "Parent");

    private static readonly MethodInfo RemoveMethod =
        AccessTools.Method(typeof(UIElement), "Remove", Type.EmptyTypes);

    private static UIWorldCreation _owner;
    private static UIElement _layoutContainer;
    private static UITextPanel<string>[] _expandedButtons = new UITextPanel<string>[0];

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate =>
                candidate.Name == "BuildPage" &&
                candidate.GetParameters().Length == 0);

        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "BuildPage()");

        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(UIWorldCreation __instance)
    {
        try
        {
            Inject(__instance);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Expanded Worlds] Could not install THICC world-size buttons: " + ex);
        }
    }

    private static void Inject(UIWorldCreation owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        if (SizeButtonsField == null)
            throw new MissingFieldException(typeof(UIWorldCreation).FullName, "_sizeButtons");
        if (DifficultyButtonsField == null)
            throw new MissingFieldException(typeof(UIWorldCreation).FullName, "_difficultyButtons");
        if (EvilButtonsField == null)
            throw new MissingFieldException(typeof(UIWorldCreation).FullName, "_evilButtons");
        if (ParentProperty == null)
            throw new MissingMemberException(typeof(UIElement).FullName, "Parent");

        Array vanillaButtons = SizeButtonsField.GetValue(owner) as Array;
        if (vanillaButtons == null || vanillaButtons.Length < 3)
            throw new InvalidOperationException("Terraria did not expose its three vanilla world-size buttons.");

        UIElement first = vanillaButtons.GetValue(0) as UIElement;
        if (first == null)
            throw new InvalidOperationException("Terraria's Small world-size button was not a UIElement.");

        UIElement container = ParentOf(first);
        if (container == null)
            throw new InvalidOperationException("Terraria's world-size row did not have a live parent container.");

        RemoveOwnButtons();

        int expandedCount = ExpandedWorldMath.ExpandedPresetCount;
        int expandedRowCount = Math.Max(1, (expandedCount + ExpandedButtonsPerRow - 1) / ExpandedButtonsPerRow);

        ApplyExpandedLayout(owner, container, first, expandedRowCount);

        _owner = owner;
        _expandedButtons = new UITextPanel<string>[expandedCount];

        for (int i = 0; i < expandedCount; i++)
        {
            ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionAt(i);
            int row = i / ExpandedButtonsPerRow;
            int rowIndex = i % ExpandedButtonsPerRow;
            int rowStart = row * ExpandedButtonsPerRow;
            int rowCount = Math.Min(ExpandedButtonsPerRow, expandedCount - rowStart);
            float topPixels = first.Top.Pixels + VanillaRowStridePixels * (row + 1);

            UITextPanel<string> button = MakeButton(
                owner,
                definition,
                i + 3,
                rowIndex,
                rowCount,
                topPixels,
                first.Top.Precent,
                first.Height.Pixels,
                first.Height.Precent);

            _expandedButtons[i] = button;
            container.Append(button);
        }

        Refresh(owner);

        Console.WriteLine(
            "[Expanded Worlds] New World size controls installed beneath untouched vanilla Small/Medium/Large row (" +
            expandedRowCount + " THICC row(s)).");
    }

    private static void ApplyExpandedLayout(
        UIWorldCreation owner,
        UIElement container,
        UIElement firstSizeButton,
        int expandedRowCount)
    {
        // Draw-time recovery can call Inject() again for the same live tree. Do
        // not keep adding height or shifting the vanilla controls on repeat calls.
        if (ReferenceEquals(container, _layoutContainer))
            return;

        Array difficultyButtons = DifficultyButtonsField.GetValue(owner) as Array;
        Array evilButtons = EvilButtonsField.GetValue(owner) as Array;
        if (difficultyButtons == null || difficultyButtons.Length == 0)
            throw new InvalidOperationException("Terraria did not expose its world-difficulty row.");
        if (evilButtons == null || evilButtons.Length == 0)
            throw new InvalidOperationException("Terraria did not expose its world-evil row.");

        UIHorizontalSeparator[] separators = container.Children
            .OfType<UIHorizontalSeparator>()
            .OrderBy(separator => separator.Top.Pixels)
            .ToArray();
        if (separators.Length < 4)
            throw new InvalidOperationException("Terraria's New World option panel did not expose its four expected separators.");

        UIElement infoHost = ParentOf(container);
        UIElement panel = ParentOf(infoHost);
        UIElement outer = ParentOf(panel);
        if (infoHost == null || panel == null || outer == null)
            throw new InvalidOperationException("Terraria's New World option panel hierarchy changed from the audited 1.4.5.8 layout.");

        float sizeTop = firstSizeButton.Top.Pixels;
        float addedHeight = VanillaRowStridePixels * expandedRowCount;
        float difficultyTop = sizeTop + VanillaRowStridePixels * (1 + expandedRowCount);
        float evilTop = difficultyTop + VanillaRowStridePixels;
        float descriptionTop = evilTop + VanillaRowStridePixels;

        // The size row itself is intentionally not touched. Only the rows that
        // originally lived underneath it move down to make room for THICC rows.
        SetRowTop(difficultyButtons, difficultyTop);
        SetRowTop(evilButtons, evilTop);

        // Separator 0 is the one immediately above the vanilla size row and
        // therefore also remains exactly where Terraria put it. The other three
        // follow the moved difficulty/evil/description boundaries.
        separators[1].Top.Set(difficultyTop - 8f, 0f);
        separators[2].Top.Set(evilTop - 8f, 0f);
        separators[3].Top.Set(descriptionTop - 8f, 0f);

        // The description panel is VAlign=1 in retail Terraria, so increasing
        // these two audited ancestor heights naturally carries it downward. The
        // Back/Create buttons are also bottom-aligned to the outer container.
        panel.Height.Set(panel.Height.Pixels + addedHeight, panel.Height.Precent);
        outer.Height.Set(outer.Height.Pixels + addedHeight, outer.Height.Precent);

        _layoutContainer = container;
    }

    private static void SetRowTop(Array row, float topPixels)
    {
        for (int i = 0; i < row.Length; i++)
        {
            UIElement button = row.GetValue(i) as UIElement;
            if (button != null)
                button.Top.Set(topPixels, 0f);
        }
    }

    private static UIElement ParentOf(UIElement element)
    {
        if (element == null || ParentProperty == null)
            return null;

        return ParentProperty.GetValue(element, null) as UIElement;
    }

    private static UITextPanel<string> MakeButton(
        UIWorldCreation owner,
        ExpandedWorldDefinition definition,
        int snapIndex,
        int rowIndex,
        int rowCount,
        float topPixels,
        float topPercent,
        float heightPixels,
        float heightPercent)
    {
        var button = new UITextPanel<string>(definition.Label, 0.8f, false);

        // Give every row a fixed six-pixel gutter while still using the full
        // width. The final five-button row therefore gets wider controls and is
        // automatically centered by HAlign.
        float widthPercent = rowCount > 0 ? 1f / rowCount : 1f;
        float widthPixels = -ExpandedButtonGapPixels * Math.Max(0, rowCount - 1) / Math.Max(1, rowCount);

        button.Width.Set(widthPixels, widthPercent);
        button.Height.Set(heightPixels, heightPercent);
        button.Top.Set(topPixels, topPercent);
        button.HAlign = rowCount <= 1 ? 0.5f : rowIndex / (float)(rowCount - 1);
        button.SetPadding(0f);
        button.SetSnapPoint("size", snapIndex);

        button.OnLeftClick += delegate
        {
            SelectCustom(owner, definition.Preset);
        };

        button.OnMouseOver += delegate
        {
            SetDescription(owner, DescriptionFor(definition.Preset));
        };

        button.OnMouseOut += delegate
        {
            SetDescription(owner, string.Empty);
        };

        return button;
    }

    private static string DescriptionFor(ExpandedWorldPreset preset)
    {
        if (preset == ExpandedWorldPreset.None)
            return string.Empty;

        ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionFor(preset);
        return definition.Label + " world: " +
               definition.Width.ToString("N0") + " x " + definition.Height.ToString("N0") +
               " tiles. Vanilla-continuity tier " + definition.OverallTier + ".";
    }

    private static void SelectCustom(UIWorldCreation owner, ExpandedWorldPreset preset)
    {
        ExpandedWorldState.Select(preset);

        // Terraria must continue to see a vanilla Large categorical value. The
        // real physical dimensions are applied when generation is armed.
        WorldGen.SetWorldSize(2);
        DeselectVanillaButtons(owner);
        Refresh(owner);

        try
        {
            UpdatePreviewPlateMethod?.Invoke(owner, null);
        }
        catch
        {
            // The preview plate intentionally remains the vanilla Large plate.
        }

        SoundEngine.PlaySound(SoundID.MenuTick);
        Console.WriteLine("[Expanded Worlds] Selected " + ExpandedWorldState.LabelFor(preset) + ".");
    }

    internal static void ClearCustomSelection(UIWorldCreation owner)
    {
        ExpandedWorldState.ClearSelection();
        Refresh(owner);
    }

    internal static void Refresh(UIWorldCreation owner)
    {
        bool sameOwner = owner != null && ReferenceEquals(owner, _owner);
        for (int i = 0; i < _expandedButtons.Length; i++)
        {
            ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionAt(i);
            SetButtonVisual(
                _expandedButtons[i],
                sameOwner && ExpandedWorldState.Selected == definition.Preset);
        }

        if (sameOwner && ExpandedWorldState.IsCustomSelected)
            DeselectVanillaButtons(owner);
    }

    private static void SetButtonVisual(UITextPanel<string> button, bool selected)
    {
        if (button == null)
            return;

        button.BackgroundColor = selected
            ? new Color(73, 94, 171)
            : new Color(63, 82, 151) * 0.72f;
        button.BorderColor = selected
            ? new Color(255, 240, 140)
            : new Color(89, 116, 213);
    }

    private static void DeselectVanillaButtons(UIWorldCreation owner)
    {
        if (owner == null || SizeButtonsField == null)
            return;

        Array vanillaButtons = SizeButtonsField.GetValue(owner) as Array;
        if (vanillaButtons == null)
            return;

        // _sizeButtons is Terraria's original three-element array. Keeping it
        // that way means private enum assumptions elsewhere remain untouched.
        int count = Math.Min(3, vanillaButtons.Length);
        for (int i = 0; i < count; i++)
        {
            object button = vanillaButtons.GetValue(i);
            if (button == null)
                continue;

            MethodInfo setter = AccessTools.Method(button.GetType(), "SetCurrentOption");
            if (setter == null)
                continue;

            ParameterInfo[] parameters = setter.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
                continue;

            try
            {
                object noVanillaOption = Enum.ToObject(parameters[0].ParameterType, -1);
                setter.Invoke(button, new[] { noVanillaOption });
            }
            catch
            {
                // Cosmetic only. Generation state is separate.
            }
        }
    }

    private static void SetDescription(UIWorldCreation owner, string text)
    {
        if (owner == null || DescriptionTextField == null)
            return;

        try
        {
            UIText description = DescriptionTextField.GetValue(owner) as UIText;
            description?.SetText(text ?? string.Empty);
        }
        catch
        {
        }
    }

    private static void RemoveOwnButtons()
    {
        if (_expandedButtons == null)
            return;

        for (int i = 0; i < _expandedButtons.Length; i++)
            RemoveOwnButton(_expandedButtons[i]);
    }

    private static void RemoveOwnButton(UIElement button)
    {
        if (button == null || RemoveMethod == null)
            return;

        try
        {
            RemoveMethod.Invoke(button, null);
        }
        catch
        {
        }
    }
}

[HarmonyPatch]
internal static class ExpandedWorldBuildPageVanillaSizeSyncPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate => candidate.Name == "ClickSizeOption");
        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "ClickSizeOption");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(UIWorldCreation __instance)
    {
        ExpandedWorldBuildPageSizeRowFix.ClearCustomSelection(__instance);
    }
}

[HarmonyPatch]
internal static class ExpandedWorldBuildPageDefaultSyncPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate => candidate.Name == "SetDefaultOptions");
        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "SetDefaultOptions");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(UIWorldCreation __instance)
    {
        ExpandedWorldBuildPageSizeRowFix.ClearCustomSelection(__instance);
    }
}

[HarmonyPatch]
internal static class ExpandedWorldBuildPageSliderSyncPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate => candidate.Name == "UpdateSliders");
        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "UpdateSliders");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(UIWorldCreation __instance)
    {
        ExpandedWorldBuildPageSizeRowFix.Refresh(__instance);
    }
}
#endif
