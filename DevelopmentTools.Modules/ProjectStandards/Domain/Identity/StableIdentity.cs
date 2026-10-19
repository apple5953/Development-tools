using System;

namespace DevelopmentTools.ProjectStandards.Domain.Identity
{
    /// <summary>
    /// Stable, cross-document identifier for Revit standards and elements.
    /// STRICT RULE: Never relies on transient Document-scoped ElementIds.
    /// </summary>
    public class StableIdentity : IEquatable<StableIdentity>
    {
        public IdentityType Type { get; set; }

        public string Key { get; set; }

        public Guid? GuidValue { get; set; }

        public string DisplayName { get; set; }

        public string SecondaryKey { get; set; }

        public StableIdentity()
        {
        }

        public StableIdentity(string key, string displayName, IdentityType type)
        {
            Key = key ?? string.Empty;
            DisplayName = displayName ?? key ?? string.Empty;
            Type = type;
        }

        public StableIdentity(string key, string displayName)
            : this(key, displayName, IdentityType.CompositeKey)
        {
        }

        public StableIdentity(Guid guid, string displayName)
        {
            GuidValue = guid;
            Key = guid.ToString();
            DisplayName = displayName ?? guid.ToString();
            Type = IdentityType.Guid;
        }

        public static StableIdentity ForSharedParameter(Guid guid, string name)
        {
            var id = new StableIdentity();
            id.Type = IdentityType.Guid;
            id.GuidValue = guid;
            id.Key = guid.ToString("D");
            id.DisplayName = name;
            return id;
        }

        public static StableIdentity ForFamilyType(string familyName, string typeName)
        {
            string key = string.Format("{0}::{1}", familyName ?? string.Empty, typeName ?? string.Empty);
            var id = new StableIdentity();
            id.Type = IdentityType.CompositeKey;
            id.Key = key;
            id.DisplayName = string.Format("{0} : {1}", familyName, typeName);
            id.SecondaryKey = familyName;
            return id;
        }

        public static StableIdentity ForLinePattern(string name, string patternDefinition)
        {
            string key = string.Format("LinePattern::{0}::{1}", name, patternDefinition ?? string.Empty);
            var id = new StableIdentity();
            id.Type = IdentityType.CompositeKey;
            id.Key = key;
            id.DisplayName = name;
            id.SecondaryKey = patternDefinition;
            return id;
        }

        public static StableIdentity ForViewTemplate(string viewType, string templateName)
        {
            string key = string.Format("ViewTemplate::{0}::{1}", viewType ?? string.Empty, templateName ?? string.Empty);
            var id = new StableIdentity();
            id.Type = IdentityType.CompositeKey;
            id.Key = key;
            id.DisplayName = templateName;
            id.SecondaryKey = viewType;
            return id;
        }

        public static StableIdentity ForViewFilter(string name, string categorySignature, string ruleSignature)
        {
            string key = string.Format("ViewFilter::{0}::{1}::{2}", name, categorySignature ?? string.Empty, ruleSignature ?? string.Empty);
            var id = new StableIdentity();
            id.Type = IdentityType.CompositeKey;
            id.Key = key;
            id.DisplayName = name;
            id.SecondaryKey = string.Format("{0}|{1}", categorySignature, ruleSignature);
            return id;
        }

        public static StableIdentity ForSchedule(string name, string category, string scheduleType)
        {
            string key = string.Format("Schedule::{0}::{1}::{2}", name, category ?? string.Empty, scheduleType ?? string.Empty);
            var id = new StableIdentity();
            id.Type = IdentityType.CompositeKey;
            id.Key = key;
            id.DisplayName = name;
            id.SecondaryKey = category;
            return id;
        }

        public static StableIdentity ForNamedStandard(string categoryKey, string name)
        {
            string key = string.Format("{0}::{1}", categoryKey, name ?? string.Empty);
            var id = new StableIdentity();
            id.Type = IdentityType.Name;
            id.Key = key;
            id.DisplayName = name;
            return id;
        }

        public bool Equals(StableIdentity other)
        {
            if (other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (GuidValue.HasValue && other.GuidValue.HasValue)
            {
                return GuidValue.Value.Equals(other.GuidValue.Value);
            }

            return string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as StableIdentity);
        }

        public override int GetHashCode()
        {
            if (GuidValue.HasValue)
            {
                return GuidValue.Value.GetHashCode();
            }
            return Key != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Key) : 0;
        }

        public override string ToString()
        {
            return string.Format("{0} ({1})", DisplayName, Key);
        }
    }
}
