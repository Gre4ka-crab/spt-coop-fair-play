using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace PrivateStack.FairPlay;

// This is a settings lock, not a trust boundary against arbitrary code on the player's PC.
public sealed class SettingsLock
{
    readonly ConcurrentDictionary<ConfigEntryBase, object> locked = new ConcurrentDictionary<ConfigEntryBase, object>();
    readonly HashSet<MethodInfo> setters = new HashSet<MethodInfo>();
    readonly Harmony harmony;
    readonly HashSet<ConfigFile> files = new HashSet<ConfigFile>();
    static SettingsLock current;
    readonly Dictionary<ConfigEntryBase, ConfigDescription> descriptions = new Dictionary<ConfigEntryBase, ConfigDescription>();
    readonly FieldInfo descriptionField = typeof(ConfigEntryBase).GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("FAIRPLAY_CONFIG_DESCRIPTION_CONTRACT");
    public int Count => locked.Count;
    public SettingsLock(Harmony harmony) { this.harmony = harmony; if (current != null) throw new InvalidOperationException("FAIRPLAY_DUPLICATE_LOCK"); current = this; }
    public void Add(ConfigEntryBase entry, string serialized)
    {
        if (locked.ContainsKey(entry)) return;
        // Parse using the pinned BepInEx converter, never culture-dependent Convert.ChangeType.
        var expected = serialized == null ? entry.DefaultValue : TomlTypeConverter.ConvertToValue(serialized, entry.SettingType);
        if (!locked.TryAdd(entry, expected)) return;
        if (files.Add(entry.ConfigFile)) entry.ConfigFile.SettingChanged += Changed;
        var setter = entry.GetType().GetProperty("BoxedValue").GetSetMethod();
        if (setters.Add(setter)) harmony.Patch(setter, prefix: new HarmonyMethod(typeof(SettingsLock), nameof(Setting)) { priority = Priority.First });
        descriptions.Add(entry, entry.Description);
        var attributes = new LockedAttributes();
        descriptionField.SetValue(entry, new ConfigDescription(entry.Description.Description, entry.Description.AcceptableValues, new object[] { attributes }));
        // A user's cfg cannot become the approved baseline.
        if (!Equals(entry.BoxedValue, expected))
        {
            var save = entry.ConfigFile.SaveOnConfigSet;
            try { entry.ConfigFile.SaveOnConfigSet = false; entry.BoxedValue = expected; }
            finally { entry.ConfigFile.SaveOnConfigSet = save; }
        }
    }
    static void Setting(ConfigEntryBase __instance, ref object __0)
    {
        if (current != null && current.locked.TryGetValue(__instance, out var expected)) __0 = expected;
    }
    void Changed(object sender, SettingChangedEventArgs args)
    {
        // Also repair a plugin writing ConfigEntry<T>.Value directly.
        if (locked.TryGetValue(args.ChangedSetting, out var expected) && !Equals(args.ChangedSetting.BoxedValue, expected)) args.ChangedSetting.BoxedValue = expected;
    }
    public void Verify()
    {
        foreach (var pair in locked) if (!Equals(pair.Key.BoxedValue, pair.Value)) throw new InvalidOperationException("FAIRPLAY_CONFIG_LOCK_LOST");
        foreach (var setter in setters) if (!(Harmony.GetPatchInfo(setter)?.Prefixes.Any(p => p.owner == harmony.Id) ?? false)) throw new InvalidOperationException("FAIRPLAY_SETTING_PATCH_LOST");
    }
    public void Dispose()
    {
        foreach (var file in files) file.SettingChanged -= Changed;
        foreach (var pair in descriptions) descriptionField.SetValue(pair.Key, pair.Value);
        harmony.UnpatchSelf(); locked.Clear(); if (ReferenceEquals(current, this)) current = null;
    }
    public sealed class LockedAttributes
    {
        public bool? Browsable = false;
        public bool? ReadOnly = true;
        public bool? HideDefaultButton = true;
    }
}
