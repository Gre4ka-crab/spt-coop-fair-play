using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Fika.Core.Main.Utils;
using Fika.Core.Networking.Http;
using Fika.Core.Networking.Models;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using Fika.Core.UI.Custom;

namespace PrivateStack.FairPlay;

[BepInPlugin(Guid, "PrivateStack Fair Play (QA)", "0.2.1")]
[BepInDependency("com.fika.core", "2.4.5")]
[BepInDependency("com.mpstark.dynamicmaps", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.tyfon.uifixes", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.zgfuedkx.allquestscheckmarks", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class FairPlayPlugin : BaseUnityPlugin
{
    public const string Guid = "private.spt.clientfairplay";
    readonly Harmony harmony = new Harmony(Guid);
    SettingsLock settings;
    Policy policy;
    bool faulted;
    readonly NoticeState notice = new NoticeState();
    static FieldInfo confirmOwner, matchmakerUi;
    static MethodInfo toggleLoading;
    HeadlessLobby lobby;
    bool buildingUi;
    float next;
    static FairPlayPlugin instance;
    readonly HashSet<string> checkedPlugins = new HashSet<string>();
    void Awake()
    {
        instance = this;
        if (FikaBackendUtils.IsHeadless) { Logger.LogError("Human-client addon must not be installed on headless"); enabled = false; return; }
        try
        {
            InstallGates();
            using (var stream = typeof(FairPlayPlugin).Assembly.GetManifestResourceStream("ClientFairPlay.policy.json"))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("FAIRPLAY_POLICY_MISSING"))) policy = JsonConvert.DeserializeObject<Policy>(reader.ReadToEnd());
            foreach (var pin in policy.pins)
            {
                var path = Path.Combine(BepInEx.Paths.GameRootPath, pin.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path) && !pin.Key.Contains("/Fika/")) continue; // Optional mods may be absent.
                if (Hash(path) != pin.Value) throw new InvalidOperationException("FAIRPLAY_VERSION_MISMATCH: " + Path.GetFileName(path));
            }
            settings = new SettingsLock(harmony);
            Apply();
            Logger.LogInfo("Fair Play QA active; headless-only host; controlled settings are hidden and pinned");
        }
        catch (Exception ex) { Fail(ex); }
    }
    void InstallGates()
    {
        NativeLobbyApi.Validate();
        var create = AccessTools.Method(typeof(FikaBackendUtils), "CreateMatch", new[] { typeof(string), typeof(string), typeof(EFT.RaidSettings) });
        var join = AccessTools.Method(typeof(FikaBackendUtils), "JoinMatch", new[] { typeof(string), typeof(string), typeof(CreateMatch).MakeByRefType(), typeof(string).MakeByRefType() });
        var start = AccessTools.Method(typeof(FikaRequestHandler), "StartHeadless", new[] { typeof(Fika.Core.Networking.Models.Headless.StartHeadlessRequest) });
        var closure = typeof(MatchMakerUIScript).GetNestedType("<>c__DisplayClass22_0", BindingFlags.NonPublic);
        var confirm = closure?.GetMethod("<CreateMatchMakerUI>b__5", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        confirmOwner = closure?.GetField("<>4__this", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        matchmakerUi = typeof(MatchMakerUIScript).GetField("_fikaMatchMakerUi", BindingFlags.Instance | BindingFlags.NonPublic);
        toggleLoading = AccessTools.Method(typeof(MatchMakerUIScript), "ToggleLoading", new[] { typeof(bool) });
        var build = AccessTools.Method(typeof(MatchMakerUIScript), "CreateMatchMakerUI");
        var host = closure?.GetMethod("<CreateMatchMakerUI>b__2", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var available = AccessTools.Method(typeof(FikaRequestHandler), "GetAvailableHeadlesses");
        if (create?.ReturnType != typeof(Task) || join?.ReturnType != typeof(bool) || start?.ReturnType != typeof(Task<Fika.Core.Networking.Models.Headless.StartHeadlessResponse>) || confirm?.ReturnType != typeof(void) || confirmOwner == null || matchmakerUi?.FieldType != typeof(MatchMakerUI) || toggleLoading == null || build == null || host == null || available == null) throw new InvalidOperationException("FAIRPLAY_FIKA_CONTRACT");
        harmony.Patch(create, prefix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(DenyHost)) { priority = Priority.First });
        harmony.Patch(join, prefix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(Joining)) { priority = Priority.First });
        harmony.Patch(start, prefix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(DemandReady)) { priority = Priority.First }, postfix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(StartResponse)));
        harmony.Patch(build, prefix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(BuildingUi)), postfix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(BuiltUi)), finalizer: new HarmonyMethod(typeof(FairPlayPlugin), nameof(BuildFailed)));
        harmony.Patch(available, prefix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(InitialAvailable)));
        harmony.Patch(host, prefix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(BeforeHostPanel)) { priority = Priority.First });
        // The pinned native async-void handler sets ToggleLoading(true) before awaiting CreateMatch.
        // Reject at its entry, before a denied Task can strand the native UI in loading state.
        harmony.Patch(confirm, prefix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(BeforeHostConfirm)) { priority = Priority.First });
    }
    static bool BeforeHostConfirm(object __instance)
    {
        var p = instance;
        if (p == null || p.faulted) return false;
        try
        {
            var owner = (MatchMakerUIScript)confirmOwner.GetValue(__instance);
            if (p.lobby != null && p.lobby.Owns(owner) && p.Ready()) p.lobby.Confirm();
            return false;
        }
        catch (Exception ex) { p.Fail(ex); return false; }
    }
    static bool BeforeHostPanel(object __instance) => instance != null && !instance.faulted && instance.lobby != null && instance.lobby.Owns((MatchMakerUIScript)confirmOwner.GetValue(__instance)) && instance.lobby.CanHost;
    static void BuildingUi() { if (instance != null) { instance.lobby?.Dispose(); instance.lobby = null; instance.buildingUi = true; } }
    static void BuiltUi(MatchMakerUIScript __instance)
    {
        var p = instance; if (p == null) return; p.buildingUi = false;
        try
        {
            var ui = (MatchMakerUI)matchmakerUi.GetValue(__instance);
            p.lobby = new HeadlessLobby(__instance, ui, toggleLoading, p.Ready, p.ShowNotice);
        }
        catch (Exception ex) { p.Fail(ex); }
    }
    static Exception BuildFailed(Exception __exception) { if (instance != null) { instance.buildingUi = false; if (__exception != null) instance.Fail(__exception); } return __exception; }
    static bool InitialAvailable(ref Fika.Core.Networking.Models.Headless.AvailableHeadlessClientsRequest[] __result)
    {
        if (instance == null || !instance.buildingUi) return true;
        __result = Array.Empty<Fika.Core.Networking.Models.Headless.AvailableHeadlessClientsRequest>(); return false;
    }
    void ShowNotice(string message) { notice.Transient(message, Stopwatch.GetTimestamp(), Stopwatch.Frequency * 10L); Logger.LogWarning(message); }
    void ShowHostDenied()
    {
        const string message = "Рейд разрешён только на headless. Выберите доступный headless; локальный хост заблокирован.";
        notice.Transient(message, Stopwatch.GetTimestamp(), Stopwatch.Frequency * 10L);
        Logger.LogWarning(message);
    }
    bool Ready()
    {
        if (faulted || policy == null || settings == null) return false;
        try
        {
            Apply();
            foreach (var group in policy.settings)
            {
                if (!Chainloader.PluginInfos.TryGetValue(group.Key, out var plugin)) continue;
                foreach (var rule in group.Value) if (!plugin.Instance.Config.ContainsKey(new ConfigDefinition(rule.section, rule.key))) throw new InvalidOperationException("FAIRPLAY_REQUIRED_SETTING_MISSING: " + group.Key + "/" + rule.key);
            }
            notice.ClearTransient(); return true;
        }
        catch (Exception ex) { Fail(ex); return false; }
    }
    static bool Joining(ref bool __result, ref CreateMatch result, ref string errorMessage)
    {
        if (instance != null && instance.Ready()) return true;
        result = default; errorMessage = "Fair Play: политика клиента не прошла проверку. Проверьте версии модов и журнал BepInEx.";
        __result = false; return false;
    }
    static bool DemandReady(Fika.Core.Networking.Models.Headless.StartHeadlessRequest __0, ref Task<Fika.Core.Networking.Models.Headless.StartHeadlessResponse> __result)
    {
        if (instance != null && instance.Ready() && instance.lobby?.Expected(__0.HeadlessSessionID) == true) return true;
        __result = Task.FromResult(new Fika.Core.Networking.Models.Headless.StartHeadlessResponse { Error = "Fair Play: запуск headless не прошёл проверку." }); return false;
    }
    static void StartResponse(Fika.Core.Networking.Models.Headless.StartHeadlessRequest __0, ref Task<Fika.Core.Networking.Models.Headless.StartHeadlessResponse> __result)
    {
        __result = SafeHeadlessStart.Normalize(__result, __0.HeadlessSessionID);
    }
    static bool DenyHost(ref Task __result)
    {
        // Human addon never runs the native host creator, including when policy activation failed.
        instance?.ShowHostDenied();
        __result = Task.FromException(new InvalidOperationException("FAIRPLAY_HEADLESS_REQUIRED")); return false;
    }
    void Apply()
    {
        foreach (var group in policy.settings)
        {
            if (!Chainloader.PluginInfos.TryGetValue(group.Key, out var plugin)) continue;
            if (checkedPlugins.Add(group.Key) && Hash(plugin.Location) != policy.plugin_pins[group.Key]) throw new InvalidOperationException("FAIRPLAY_LOADED_PLUGIN_MISMATCH: " + group.Key);
            var cfg = plugin.Instance.Config;
            var byKey = group.Value.ToDictionary(e => e.section + "\n" + e.key, StringComparer.Ordinal);
            foreach (var entry in cfg.Select(p => p.Value).ToArray())
            {
                if (byKey.TryGetValue(entry.Definition.Section + "\n" + entry.Definition.Key, out var rule)) settings.Add(entry, rule.value);
                else if (policy.freeze_defaults.Contains(group.Key) && !(policy.unlocked_sections.TryGetValue(group.Key, out var unowned) && unowned.Contains(entry.Definition.Section))) throw new InvalidOperationException("FAIRPLAY_UNKNOWN_SETTING: " + group.Key + "/" + entry.Definition.Key);
            }
            // Fika settings bind after locales/backend load. Missing keys are checked at the host/join gate.
        }
        var fika = Fika.Core.FikaPlugin.Instance?.Settings;
        if (fika != null) { fika.CanEditRaidSettings = false; fika.AllowFreeCam = false; fika.AllowSpectateFreeCam = false; fika.AllowSpectateBots = false; }
        settings.Verify();
    }
    void Update()
    {
        try { if (lobby != null) { if (faulted) { lobby.Dispose(); lobby = null; } else lobby.Tick(); } } catch (Exception ex) { Fail(ex); }
        if (faulted || Time.unscaledTime < next) return; next = Time.unscaledTime + 0.5f;
        try { Apply(); } catch (Exception ex) { Fail(ex); }
    }
    void Fail(Exception ex)
    {
        faulted = true; var message = "Fair Play: проверка версии или политики не пройдена. Подключение запрещено. " + ex.Message;
        notice.Fault(message); Logger.LogError(message);
        // Keep/establish deny gates even when an optional mod changed underneath us.
        var create = AccessTools.Method(typeof(FikaBackendUtils), "CreateMatch", new[] { typeof(string), typeof(string), typeof(EFT.RaidSettings) });
        if (create != null && !(Harmony.GetPatchInfo(create)?.Owners.Contains(Guid) ?? false)) harmony.Patch(create, prefix: new HarmonyMethod(typeof(FairPlayPlugin), nameof(DenyHost)));
    }
    void OnGUI()
    {
        if (!notice.Visible(Stopwatch.GetTimestamp())) return;
        GUI.Box(new Rect(20, 20, Math.Min(Screen.width - 40, 850), 75), notice.Text);
    }
    void OnDestroy() { lobby?.Dispose(); settings?.Dispose(); harmony.UnpatchSelf(); if (ReferenceEquals(instance, this)) instance = null; }
    static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    public sealed class Policy { public Dictionary<string, string> pins, plugin_pins; public Dictionary<string, List<Entry>> settings; public string[] freeze_defaults; public Dictionary<string, string[]> unlocked_sections; }
    public sealed class Entry { public string section, key, value; }
}
