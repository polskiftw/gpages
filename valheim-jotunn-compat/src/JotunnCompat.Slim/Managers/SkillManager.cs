using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using UnityEngine;

namespace Jotunn.Managers
{
    /// <summary>
    /// Generic Jotunn-compatible custom skill registry and Valheim lifecycle
    /// integration. No dependent mod needs to be present.
    /// </summary>
    public class SkillManager
    {
        private static SkillManager instance;
        public static SkillManager Instance => instance ??= new SkillManager();

        // Preserve the upstream field name and exact generic shape. Mods can
        // inspect this registry using reflection even when they do not call
        // SkillManager.AddSkill themselves.
        internal Dictionary<Skills.SkillType, SkillConfig> CustomSkills =
            new Dictionary<Skills.SkillType, SkillConfig>();

        private bool addedSkillsToTerminal;

        private SkillManager()
        {
            Main.LogInit("SkillManager");
            Main.Harmony.PatchAll(typeof(Patches));

            // Some Valheim versions expose these methods on Skills; others
            // move/remove them. Never abort Jotunn startup for an obsolete
            // optional hook, but patch the instance method when available.
            PatchOptionalSkillMethod(
                "IsSkillValid",
                "SkillsIsValid",
                false);
            PatchOptionalSkillMethod(
                "GetSkill",
                "SkillsGetSkill",
                true);

            // Terminal command registration also moved in Valheim 1.0.
            // Hook its initialization when available, not just Awake.
            var terminalInit = AccessTools.Method(typeof(Terminal), "InitTerminal");
            var terminalPatch = AccessTools.Method(typeof(Patches), "TerminalAwake");
            if (terminalInit != null && terminalPatch != null)
            {
                Main.Harmony.Patch(
                    terminalInit,
                    postfix: new HarmonyMethod(terminalPatch));
            }
        }

        private static readonly FieldInfo SkillDataField =
            AccessTools.Field(typeof(Skills), "m_skillData");

        private static readonly FieldInfo CommandsField =
            AccessTools.Field(typeof(Terminal), "commands");

        private static void PatchOptionalSkillMethod(
            string targetName,
            string patchName,
            bool prefix)
        {
            var target = AccessTools.Method(typeof(Skills), targetName);
            var patch = AccessTools.Method(typeof(Patches), patchName);
            if (target == null || patch == null)
            {
                Logger.LogDebug(
                    "Optional custom-skill hook unavailable: Skills." + targetName);
                return;
            }

            try
            {
                var harmonyMethod = new HarmonyMethod(patch);
                if (prefix)
                {
                    Main.Harmony.Patch(target, prefix: harmonyMethod);
                }
                else
                {
                    Main.Harmony.Patch(target, postfix: harmonyMethod);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "Could not attach optional Skills." + targetName +
                    " hook: " + ex.Message);
            }
        }

        public Skills.SkillType AddSkill(SkillConfig skillConfig)
        {
            if (skillConfig == null || string.IsNullOrEmpty(skillConfig.Identifier))
            {
                Logger.LogError("Cannot register a custom skill without an identifier");
                return Skills.SkillType.None;
            }

            // Match upstream's stable UID semantics and avoid silently
            // overwriting a definition already registered by another plugin.
            CustomSkills.Add(skillConfig.UID, skillConfig);

            // Skills.Awake normally runs after plugins initialize, but support
            // registrations made after the player is already in a world too.
            var loadedSkills = Player.m_localPlayer?.GetSkills();
            if (loadedSkills != null)
            {
                RegisterSkill(loadedSkills, skillConfig);
            }

            return skillConfig.UID;
        }

        [Obsolete("Use AddSkill(SkillConfig) instead")]
        public Skills.SkillType AddSkill(
            string identifer,
            string name,
            string description,
            float increaseStep = 1f,
            Sprite icon = null)
        {
            return AddSkill(new SkillConfig
            {
                Identifier = identifer,
                Name = name,
                Description = description,
                IncreaseStep = increaseStep,
                Icon = icon
            });
        }

        public void AddSkillsFromJson(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Logger.LogWarning("Missing custom skill JSON path");
                return;
            }

            try
            {
                string fullPath = Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(Paths.PluginPath, path);

                foreach (var skill in SkillConfig.ListFromJson(File.ReadAllText(fullPath)))
                {
                    AddSkill(skill);
                }
            }
            catch (Exception error)
            {
                Logger.LogError(
                    "Could not load custom skills from " + path + ": " + error);
            }
        }

        public Skills.SkillDef GetSkill(Skills.SkillType skillType)
        {
            if (CustomSkills.TryGetValue(skillType, out var config))
            {
                return config.ToSkillDef();
            }

            return Player.m_localPlayer?.GetSkills()?.m_skills?
                .FirstOrDefault(skill => skill.m_skill == skillType);
        }

        public Skills.SkillDef GetSkill(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return null;
            }

            int hash = identifier.GetStableHashCode();
            return hash == int.MinValue
                ? null
                : GetSkill((Skills.SkillType)Math.Abs(hash));
        }

        private void RegisterCustomSkills(Skills skills)
        {
            if (skills == null)
            {
                return;
            }

            foreach (var config in CustomSkills.Values)
            {
                RegisterSkill(skills, config);
            }
        }

        private static void RegisterSkill(Skills skills, SkillConfig config)
        {
            if (skills.m_skills == null)
            {
                return;
            }

            if (!skills.m_skills.Any(def => def.m_skill == config.UID))
            {
                skills.m_skills.Add(config.ToSkillDef());
            }

            LocalizationManager.Instance.AddToken(
                "skill_" + (int)config.UID,
                config.LocalizedName);
        }

        private bool RaiseCustomSkill(Skills skills, string name, float amount)
        {
            foreach (var config in CustomSkills.Values)
            {
                if (!config.IsFromName(name))
                {
                    continue;
                }

                // Valheim 1.0 removed the previous public Skills.GetSkill
                // accessor. Retrieve the live skill from its backing data
                // store without depending on whether the field is public.
                var data = SkillDataField?.GetValue(skills) as IDictionary;
                object skill = data != null && data.Contains(config.UID)
                    ? data[config.UID]
                    : null;

                var levelField = skill == null
                    ? null
                    : AccessTools.Field(skill.GetType(), "m_level");

                if (levelField == null)
                {
                    // The game can fall through to its own command handling
                    // if this particular skill is not initialized yet.
                    return true;
                }

                float level = Mathf.Clamp(
                    Convert.ToSingle(levelField.GetValue(skill)) + amount,
                    0f,
                    100f);
                levelField.SetValue(skill, level);

                var player = Player.m_localPlayer;
                if (player != null)
                {
                    player.Message(
                        MessageHud.MessageType.TopLeft,
                        "Skill increased " + config.LocalizedName + ": " +
                        (int)level,
                        0,
                        config.Icon);
                }

                if (Console.instance != null)
                {
                    Console.instance.Print(
                        "Skill " + config.LocalizedName + " = " + level);
                }

                return false;
            }

            return true;
        }

        private bool ResetCustomSkill(Skills skills, string name)
        {
            foreach (var config in CustomSkills.Values)
            {
                if (!config.IsFromName(name))
                {
                    continue;
                }

                skills.ResetSkill(config.UID);
                if (Console.instance != null)
                {
                    Console.instance.Print(
                        "Skill " + config.LocalizedName + " reset");
                }

                return false;
            }

            return true;
        }

        private void AddSkillsToTerminal()
        {
            if (addedSkillsToTerminal)
            {
                return;
            }

            var commands = CommandsField?.GetValue(null) as
                IDictionary<string, Terminal.ConsoleCommand>;
            if (commands == null ||
                !commands.ContainsKey("raiseskill") ||
                !commands.ContainsKey("resetskill"))
            {
                return;
            }

            AddSkillOptions(commands["raiseskill"]);
            AddSkillOptions(commands["resetskill"]);
            addedSkillsToTerminal = true;
        }

        private void AddSkillOptions(Terminal.ConsoleCommand command)
        {
            // Tab-completion is optional and the field has moved/changed
            // visibility across game revisions. The skill registry and
            // gameplay behavior must not depend on it.
            var optionsField = AccessTools.Field(
                command.GetType(),
                "m_tabOptionsFetcher");
            if (optionsField == null)
            {
                return;
            }

            try
            {
                var previous = optionsField.GetValue(command) as Delegate;
                Func<List<string>> fetcher = () =>
                {
                    var options = previous?.DynamicInvoke() as List<string>
                        ?? new List<string>();
                    options.AddRange(
                        CustomSkills.Values.Select(config => config.LocalizedName));
                    return options;
                };

                var replacement = Delegate.CreateDelegate(
                    optionsField.FieldType,
                    fetcher.Target,
                    fetcher.Method);
                optionsField.SetValue(command, replacement);
            }
            catch (Exception exception)
            {
                Logger.LogDebug(
                    "Custom skill tab-completion unavailable: " +
                    exception.Message);
            }
        }

        private static class Patches
        {
            [HarmonyPatch(typeof(Skills), nameof(Skills.Awake))]
            [HarmonyPostfix]
            private static void SkillsAwake(Skills __instance)
            {
                Instance.RegisterCustomSkills(__instance);
            }

            private static void SkillsIsValid(
                Skills.SkillType type,
                ref bool __result)
            {
                int number = (int)type;
                if (number != int.MinValue)
                {
                    __result = __result ||
                        Instance.CustomSkills.ContainsKey(
                            (Skills.SkillType)Math.Abs(number));
                }
            }

            private static void SkillsGetSkill(ref Skills.SkillType skillType)
            {
                int number = (int)skillType;
                if (number < 0 && number != int.MinValue &&
                    Instance.CustomSkills.ContainsKey(
                        (Skills.SkillType)Math.Abs(number)))
                {
                    skillType = (Skills.SkillType)Math.Abs(number);
                }
            }

            [HarmonyPatch(typeof(Skills), nameof(Skills.CheatRaiseSkill))]
            [HarmonyPrefix]
            private static bool SkillsCheatRaise(
                Skills __instance,
                string name,
                float value)
            {
                return Instance.RaiseCustomSkill(__instance, name, value);
            }

            [HarmonyPatch(typeof(Skills), nameof(Skills.CheatResetSkill))]
            [HarmonyPrefix]
            private static bool SkillsCheatReset(
                Skills __instance,
                string name)
            {
                return Instance.ResetCustomSkill(__instance, name);
            }

            [HarmonyPatch(typeof(Terminal), nameof(Terminal.Awake))]
            [HarmonyPostfix]
            private static void TerminalAwake()
            {
                Instance.AddSkillsToTerminal();
            }
        }
    }
}
