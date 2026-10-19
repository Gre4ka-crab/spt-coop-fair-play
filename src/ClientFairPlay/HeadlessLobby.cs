using Fika.Core.Networking.Http;
using Fika.Core.Networking.Models.Headless;
using Fika.Core.UI.Custom;
using Fika.Core;
using Fika.Core.Main.Utils;
using Fika.Core.Networking.Websocket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine.Events;

namespace PrivateStack.FairPlay;

// Tick, UI mutations and native callback invocation all run on the Unity main thread.
sealed class HeadlessLobby : IDisposable
{
    readonly MatchMakerUIScript owner;
    readonly MatchMakerUI ui;
    readonly MethodInfo toggleLoading;
    readonly Func<bool> ready;
    readonly Action<string> notify;
    readonly HeadlessAvailability state = new HeadlessAvailability();
    readonly UnityAction<int> selectionChanged;
    Task<AvailableHeadlessClientsRequest[]> probe;
    Task<StartHeadlessResponse> start;
    long probeAt, nextProbe, connectAt;
    bool disposed, expired, confirmProbe, requestExpected, painting, connecting, dispatched;
    string requested;
    string[] optionIds = Array.Empty<string>();
    string optionsKey;
    public static long Now => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
    public bool Alive => !disposed && owner != null && owner.isActiveAndEnabled && ui != null;
    public bool Owns(MatchMakerUIScript candidate) => ReferenceEquals(candidate, owner);
    public bool CanHost => Alive && state.CanHost(Now);
    public HeadlessLobby(MatchMakerUIScript owner, MatchMakerUI ui, MethodInfo toggleLoading, Func<bool> ready, Action<string> notify)
    {
        this.owner = owner; this.ui = ui; this.toggleLoading = toggleLoading; this.ready = ready; this.notify = notify;
        NativeLobbyApi.Validate();
        if (ui.RaidGroupHostButton == null || ui.StartButton == null || ui.HeadlessSelection == null || ui.UseDedicatedHostText == null || ui.HostRaidText == null || ui.DedicatedToggle == null)
            throw new InvalidOperationException("FAIRPLAY_LOBBY_UI_CONTRACT");
        selectionChanged = Select;
        ui.HeadlessSelection.onValueChanged.AddListener(selectionChanged);
        ui.DedicatedToggle.isOn = true; ui.DedicatedToggle.interactable = false;
        ui.HostRaidText.enableAutoSizing = true;
        ui.UseDedicatedHostText.enableAutoSizing = true;
        Paint();
    }
    public bool Confirm()
    {
        if (!state.Begin(Now)) { notify("Нет подтверждённого свободного headless. Выберите доступный экземпляр и дождитесь проверки."); return false; }
        requested = state.SelectedId; confirmProbe = true;
        // A read already in flight predates this click. Discard it and issue a new check.
        if (probe != null) expired = true;
        nextProbe = 0; Paint(); return false;
    }
    public bool Expected(string id) => Alive && requestExpected && state.Busy && requested == id;
    public void Tick()
    {
        if (!Alive) { Dispose(); return; }
        var now = Now;
        if (state.Busy && !dispatched && start == null && ui.DediSelection != null && !ui.DediSelection.activeSelf && (ui.LoadingScreen == null || !ui.LoadingScreen.activeSelf))
        {
            // Closing the dialog cancels read/connection preparation, never a dispatched raid.
            confirmProbe = false; connecting = false; requestExpected = false; requested = null;
            if (probe != null) expired = true;
            state.Release("Запуск отменён."); nextProbe = 0;
        }
        if (connecting)
        {
            if (FikaPlugin.HeadlessRequesterWebSocket?.Connected == true)
            {
                connecting = false; confirmProbe = true; nextProbe = 0; // Recheck availability after the handshake.
            }
            else if (now - connectAt >= 8000)
            {
                connecting = false; state.Release("Нет связи с сервером headless. Повторите проверку."); nextProbe = 0;
            }
        }
        if (start != null && start.IsCompleted)
        {
            var result = start.GetAwaiter().GetResult(); start = null;
            if (!string.IsNullOrEmpty(result.Error))
            {
                FikaBackendUtils.IsHeadlessRequester = false;
                toggleLoading.Invoke(owner, new object[] { false });
                state.Release(result.Error, result.Error == SafeHeadlessStart.UncertainMessage);
                dispatched = false;
                nextProbe = 0; notify(result.Error);
            }
            else FikaBackendUtils.IsHeadlessRequester = true;
            // Success remains locked until the native requester WebSocket joins the raid.
        }
        if (probe != null)
        {
            if (now - probeAt >= 8000 && (!expired || confirmProbe))
            {
                expired = true; state.Release("Сервер не ответил на проверку headless."); confirmProbe = false;
            }
            if (probe.IsCompleted)
            {
                try
                {
                    var rows = probe.GetAwaiter().GetResult();
                    if (!expired)
                    {
                        state.Receive(rows.Select(h => new HeadlessAvailability.Host(h.HeadlessSessionID, h.Alias)), now);
                        if (confirmProbe)
                        {
                            confirmProbe = false;
                            if (state.Confirmed(requested, now) && ready())
                            {
                                Dispatch();
                            }
                            else state.Release("Выбранный headless уже недоступен. Выберите свободный и повторите.");
                        }
                    }
                }
                catch (Exception)
                {
                    if (ui.LoadingScreen != null && ui.LoadingScreen.activeSelf) toggleLoading.Invoke(owner, new object[] { false });
                    state.Release(dispatched ? SafeHeadlessStart.UncertainMessage : "Не удалось проверить или запустить headless. Вернитесь в список и повторите проверку.", dispatched); confirmProbe = false; requestExpected = false;
                }
                finally { probe = null; expired = false; nextProbe = confirmProbe ? 0 : now + 2500; }
            }
        }
        if (probe == null && now >= nextProbe && (!state.Busy || confirmProbe) && !state.Uncertain)
        {
            try { probe = NativeLobbyApi.Available(); probeAt = now; }
            catch (Exception) { state.Release("Сервер недоступен. Создание рейда заблокировано."); confirmProbe = false; nextProbe = now + 2500; }
        }
        Paint();
    }
    void Select(int index)
    {
        if (state.Busy || painting) return;
        state.Select(index > 0 && index <= optionIds.Length ? optionIds[index - 1] : null);
    }
    void Dispatch()
    {
        // The same native requester WS/DTO/HTTP route as pinned Fika, with owned error handling.
        var settings = NativeLobbyApi.Settings(owner) ?? throw new InvalidOperationException("FAIRPLAY_RAID_SETTINGS_MISSING");
        var request = new StartHeadlessRequest
        {
            HeadlessSessionID = requested, Time = settings.SelectedDateTime,
            LocationId = settings.SelectedLocation._Id, SpawnPlace = settings.PlayersSpawnPlace,
            MetabolismDisabled = settings.MetabolismDisabled, BotSettings = settings.BotSettings,
            Side = settings.Side, TimeAndWeatherSettings = settings.TimeAndWeatherSettings,
            WavesSettings = settings.WavesSettings, CustomRaidSettings = FikaBackendUtils.CustomRaidSettings,
            UseEvent = false
        };
        if (FikaPlugin.HeadlessRequesterWebSocket == null) FikaPlugin.HeadlessRequesterWebSocket = new HeadlessRequesterWebSocket();
        if (!FikaPlugin.HeadlessRequesterWebSocket.Connected)
        {
            NativeLobbyApi.Connect(FikaPlugin.HeadlessRequesterWebSocket);
            connecting = true; connectAt = Now; return;
        }
        toggleLoading.Invoke(owner, new object[] { true });
        requestExpected = true;
        dispatched = true;
        start = FikaRequestHandler.StartHeadless(request);
        requestExpected = false;
    }
    void Paint()
    {
        if (!Alive) return;
        var now = Now;
        var loading = ui.LoadingScreen != null && ui.LoadingScreen.activeSelf;
        ui.RaidGroupHostButton.interactable = state.CanHost(now) && !loading;
        ui.StartButton.interactable = state.CanStart(now) && !loading;
        ui.DedicatedToggle.isOn = true; ui.DedicatedToggle.interactable = false;
        ui.HostRaidText.text = state.CanHost(now) ? "Создать рейд (" + state.Hosts.Length + ")" : "Создание недоступно";
        ui.UseDedicatedHostText.text = state.Uncertain ? "Запуск не подтверждён. Вернитесь в меню." : state.Busy ? "Проверка и запуск headless…" : state.Error ?? (!state.Fresh(now) ? "Проверка headless…" : state.Hosts.Length == 0 ? "Нет свободного headless" : "Только headless · Свободно: " + state.Hosts.Length);
        ui.HeadlessSelection.interactable = !state.Busy && state.Fresh(now);
        ui.HeadlessSelection.gameObject.SetActive(state.Hosts.Length > 0);
        var key = string.Join("\n", state.Hosts.Select(h => h.Id + "\t" + h.Alias));
        if (key != optionsKey)
        {
            optionsKey = key; optionIds = state.Hosts.Select(h => h.Id).ToArray();
            var labels = new List<string> { "Выберите headless" };
            for (int i = 0; i < state.Hosts.Length; i++)
            {
                var alias = state.Hosts[i].Alias;
                alias = string.IsNullOrWhiteSpace(alias) ? "Headless" : new string(alias.Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(60).ToArray());
                labels.Add(alias + (state.Hosts.Count(h => h.Alias == state.Hosts[i].Alias) > 1 ? " (" + (i + 1) + ")" : ""));
            }
            painting = true;
            try { ui.HeadlessSelection.ClearOptions(); ui.HeadlessSelection.AddOptions(labels); }
            finally { painting = false; }
        }
        ui.HeadlessSelection.SetValueWithoutNotify(Array.IndexOf(optionIds, state.SelectedId) + 1);
        ui.HeadlessSelection.RefreshShownValue();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (ui != null) { if (ui.RaidGroupHostButton != null) ui.RaidGroupHostButton.interactable = false; if (ui.StartButton != null) ui.StartButton.interactable = false; }
        if (ui != null && ui.HeadlessSelection != null) ui.HeadlessSelection.onValueChanged.RemoveListener(selectionChanged);
        if (probe != null) _ = Observe(probe);
        // No new requests or Unity mutation after disposal; pending read cannot re-enable another lobby.
    }
    static async Task Observe(Task task) { try { await task.ConfigureAwait(false); } catch { } }
}
