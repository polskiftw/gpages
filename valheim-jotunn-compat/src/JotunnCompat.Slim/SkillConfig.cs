using System;
using System.Collections.Generic;
using Jotunn.Utils;
using UnityEngine;

namespace Jotunn.Configs
{
    /// <summary>
    /// Jotunn-compatible, stable skill registration data.
    /// Skill IDs must be derived from identifiers exactly as in upstream Jotunn
    /// so existing Valheim character progress survives a runtime swap.
    /// </summary>
    public class SkillConfig
    {
        private string identifier;

        public Skills.SkillType UID { get; private set; }

        public string Identifier
        {
            get => identifier;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentException("A skill identifier is required", nameof(value));
                }

                int hash = value.GetStableHashCode();
                if (hash == int.MinValue)
                {
                    throw new ArgumentException("Skill identifier has an invalid stable hash", nameof(value));
                }

                int numericID = Math.Abs(hash);
                if (numericID <= (int)Skills.SkillType.All)
                {
                    throw new ArgumentException(
                        "This identifier may conflict with default Valheim skills", nameof(value));
                }

                identifier = value;
                UID = (Skills.SkillType)numericID;
            }
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public Sprite Icon { get; set; }
        public float IncreaseStep { get; set; } = 1f;

        public string IconPath
        {
            set
            {
                if (Icon)
                {
                    Logger.LogWarning(
                        "Icon and IconPath were both set for skill " + Identifier +
                        "; keeping the existing icon");
                    return;
                }

                var texture = AssetUtils.LoadTexture(value);
                if (texture)
                {
                    Icon = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                }
            }
        }

        internal string LocalizedName
        {
            get
            {
                if (string.IsNullOrEmpty(Name))
                {
                    return Identifier ?? string.Empty;
                }

                return Name.StartsWith("$", StringComparison.Ordinal) &&
                       Localization.instance != null
                    ? Localization.instance.Localize(Name)
                    : Name;
            }
        }

        public Skills.SkillDef ToSkillDef()
        {
            return new Skills.SkillDef
            {
                m_description = Description,
                m_icon = Icon,
                m_increseStep = IncreaseStep,
                m_skill = UID
            };
        }

        internal bool IsFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return string.Equals(Name, name, StringComparison.CurrentCultureIgnoreCase) ||
                   string.Equals(Identifier, name, StringComparison.Ordinal) ||
                   string.Equals(LocalizedName, name, StringComparison.CurrentCultureIgnoreCase);
        }

        [Obsolete("For migrating SkillInjector skills only; use Identifier for new skills")]
        public static SkillConfig FromSkillInjector(
            string identifier,
            Skills.SkillType uid,
            string name,
            string description,
            float increaseStep,
            Sprite icon)
        {
            return new SkillConfig
            {
                Identifier = identifier,
                UID = uid,
                Name = name,
                Description = description,
                IncreaseStep = increaseStep,
                Icon = icon
            };
        }

        // JSON is a YAML-compatible flow syntax. Reuse the dependency already
        // shipped with our package rather than adding a JSON runtime assembly.
        public static SkillConfig FromJson(string json)
        {
            return new YamlDotNet.Serialization.DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<SkillConfig>(json);
        }

        public static List<SkillConfig> ListFromJson(string json)
        {
            return new YamlDotNet.Serialization.DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<List<SkillConfig>>(json) ?? new List<SkillConfig>();
        }

        public override string ToString()
        {
            return "SkillConfig(Identifier='" + Identifier +
                   "', UID=" + UID + ", Name='" + Name + "')";
        }
    }
}
