using FikaServer.Controllers;
using FikaServer.Helpers;
using FikaServer.Models.Fika.Config;
using FikaServer.Models.Fika.Routes.Headless;
using FikaServer.Models.Fika.Routes.Raid.Create;
using FikaServer.Services.Headless;
using FikaServer.Services;
using FikaServer.Models.Fika.Routes.Client.Check;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using FikaServer.Models.Fika.Headless;
using FikaServer.Callbacks;
using SPTarkov.Server.Core.Utils;
using System.Reflection;

namespace PrivateStack.FairPlay.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "private.spt.fairplayserver";
    public string Name { get; init; } = "PrivateStack Fair Play Server (QA)";
    public string Author { get; init; } = "PrivateStack";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.2.1");
    public SemanticVersioning.Range SptVersion { get; init; } = new("=4.1.6");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new() { ["Fika"] = new("=2.4.2") };
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload + 12)]
public sealed class FairPlayService(HeadlessHelper helper, FikaServerConfig config) : IOnLoad
{
    readonly HeadlessHelper headless = helper;
    static FairPlayService? current;
    readonly Harmony harmony = new("private.spt.fairplayserver");
    uint clientCrc;
    readonly HeadlessAdmission admission = new();
    static FieldInfo? callbackController, callbackHttp;
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (current != null) throw new InvalidOperationException("FAIRPLAY_DUPLICATE_SERVER");
        using var file = File.OpenRead(typeof(RaidController).Assembly.Location);
        if (Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() != "9ac29e72847783fb2b525342e9e0bb2bef71c5c275aabc05b60ec15427bafc24") throw new InvalidOperationException("FAIRPLAY_SERVER_VERSION_MISMATCH");
        using var resource = typeof(FairPlayService).Assembly.GetManifestResourceStream("FairPlayServer.client-pin.json") ?? throw new InvalidOperationException("FAIRPLAY_CLIENT_PIN_MISSING");
        using var pin = JsonDocument.Parse(resource);
        clientCrc = pin.RootElement.GetProperty("crc32c").GetUInt32();
        var create = AccessTools.Method(typeof(RaidController), nameof(RaidController.HandleRaidCreate), new[] { typeof(FikaRaidCreateRequestData), typeof(MongoId) }) ?? throw new InvalidOperationException("FAIRPLAY_CREATE_CONTRACT");
        var start = AccessTools.Method(typeof(HeadlessService), nameof(HeadlessService.StartHeadlessRaid), new[] { typeof(string), typeof(string), typeof(StartHeadlessRequest) }) ?? throw new InvalidOperationException("FAIRPLAY_START_CONTRACT");
        var validation = AccessTools.Method(typeof(ClientService), nameof(ClientService.GetCheckModsResponse), new[] { typeof(FikaCheckModRequestData), typeof(MongoId) }) ?? throw new InvalidOperationException("FAIRPLAY_VALIDATION_CONTRACT");
        var clientConfig = AccessTools.Method(typeof(ClientService), nameof(ClientService.GetClientConfig), Type.EmptyTypes) ?? throw new InvalidOperationException("FAIRPLAY_CLIENT_CONFIG_CONTRACT");
        var available = AccessTools.Method(typeof(HeadlessHelper), nameof(HeadlessHelper.GetAvailableHeadlessClients), Type.EmptyTypes) ?? throw new InvalidOperationException("FAIRPLAY_AVAILABLE_CONTRACT");
        var response = AccessTools.Method(typeof(RaidController), nameof(RaidController.HandleRaidStartHeadless), new[] { typeof(MongoId), typeof(StartHeadlessRequest) }) ?? throw new InvalidOperationException("FAIRPLAY_START_RESPONSE_CONTRACT");
        var callback = AccessTools.Method(typeof(RaidCallbacks), nameof(RaidCallbacks.HandleRaidStartHeadless), new[] { typeof(string), typeof(StartHeadlessRequest), typeof(string) }) ?? throw new InvalidOperationException("FAIRPLAY_START_CALLBACK_CONTRACT");
        callbackController = AccessTools.Field(typeof(RaidCallbacks), "<raidController>P");
        callbackHttp = AccessTools.Field(typeof(RaidCallbacks), "<httpResponseUtil>P");
        if (callback.ReturnType != typeof(ValueTask<string>) || callbackController?.FieldType != typeof(RaidController) || callbackHttp?.FieldType != typeof(HttpResponseUtil)) throw new InvalidOperationException("FAIRPLAY_START_CALLBACK_FIELDS_CONTRACT");
        current = this;
        // Activation errors abort backend loading; no silent unprotected fallback.
        harmony.Patch(create, prefix: new HarmonyMethod(typeof(FairPlayService), nameof(Creating)) { priority = Priority.First });
        harmony.Patch(start, prefix: new HarmonyMethod(typeof(FairPlayService), nameof(Starting)) { priority = Priority.First });
        harmony.Patch(validation, postfix: new HarmonyMethod(typeof(FairPlayService), nameof(Validated)) { priority = Priority.Last });
        harmony.Patch(clientConfig, postfix: new HarmonyMethod(typeof(FairPlayService), nameof(Configured)) { priority = Priority.Last });
        harmony.Patch(available, postfix: new HarmonyMethod(typeof(FairPlayService), nameof(Available)) { priority = Priority.Last });
        harmony.Patch(response, postfix: new HarmonyMethod(typeof(FairPlayService), nameof(StartResponse)));
        harmony.Patch(callback, prefix: new HarmonyMethod(typeof(FairPlayService), nameof(StartCallback)) { priority = Priority.First });
        config.Client.CanEditRaidSettings = false;
        config.Client.AllowFreeCam = false; config.Client.AllowSpectateFreeCam = false; config.Client.AllowSpectateBots = false;
        return Task.CompletedTask;
    }
    static void Configured(FikaConfigClient __result)
    {
        __result.CanEditRaidSettings = false; __result.AllowFreeCam = false;
        __result.AllowSpectateFreeCam = false; __result.AllowSpectateBots = false;
    }
    static void Validated(FikaCheckModRequestData __0, MongoId __1, FikaCheckModResponse __result)
    {
        var owner = current ?? throw new InvalidOperationException("FAIRPLAY_SERVER_NOT_READY");
        ModRules.Validate(owner.headless.IsHeadlessClient(__1), __0, __result, owner.clientCrc);
    }
    static void Creating(FikaRaidCreateRequestData __0, MongoId __1)
    {
        var owner = current ?? throw new InvalidOperationException("FAIRPLAY_SERVER_NOT_READY");
        var online = owner.headless.HeadlessClients.TryGetValue(__1, out var info) && info.WebSocket?.State == WebSocketState.Open;
        RaidRules.Host(owner.headless.IsHeadlessClient(__1), online, __0 != null && __0.ServerId == __1, __0?.IsSpectator ?? true);
        RaidRules.Canonical(__0!.Settings);
        __0.CustomRaidSettings = RaidRules.Custom();
    }
    static bool Starting(string __0, string __1, StartHeadlessRequest __2, ref Task<string?> __result)
    {
        var owner = current ?? throw new InvalidOperationException("FAIRPLAY_SERVER_NOT_READY");
        if (__2 == null || __2.HeadlessSessionID != __0 || !owner.headless.IsHeadlessClient(new MongoId(__0)) || owner.headless.IsHeadlessClient(new MongoId(__1))) throw new InvalidOperationException("FAIRPLAY_INVALID_HEADLESS_REQUEST");
        __2.MetabolismDisabled = false;
        __2.SpawnPlace = SPTarkov.Server.Core.Models.Enums.PlayersSpawnPlace.DifferentPlaces;
        __2.BotSettings = RaidRules.Bots(); __2.WavesSettings = RaidRules.Waves(); __2.TimeAndWeatherSettings = RaidRules.Weather();
        __2.CustomRaidSettings = RaidRules.Custom(); __2.UseEvent = false;
        if (!owner.admission.Reserve(owner.headless.HeadlessClients, new MongoId(__0), __1)) { __result = Task.FromResult<string?>(null); return false; }
        return true;
    }
    static void Available(ref HeadlessAvailableClients[] __result)
    {
        var owner = current ?? throw new InvalidOperationException("FAIRPLAY_SERVER_NOT_READY");
        __result = __result.Where(h => owner.headless.IsHeadlessClient(h.HeadlessSessionID) && owner.headless.HeadlessClients.TryGetValue(h.HeadlessSessionID, out var info) && HeadlessAdmission.Available(info)).ToArray();
    }
    static void StartResponse(ref Task<StartHeadlessResponse> __result) => __result = Normalize(__result);
    static async Task<StartHeadlessResponse> Normalize(Task<StartHeadlessResponse> task)
    {
        var response = await task.ConfigureAwait(false);
        if (string.IsNullOrEmpty(response.MatchId) && string.IsNullOrEmpty(response.Error)) response.Error = "Выбранный headless занят или недоступен. Обновите список.";
        return response;
    }
    static bool StartCallback(RaidCallbacks __instance, StartHeadlessRequest __1, string __2, ref ValueTask<string> __result)
    {
        __result = new ValueTask<string>(AwaitStart((RaidController)callbackController!.GetValue(__instance)!, (HttpResponseUtil)callbackHttp!.GetValue(__instance)!, __1, __2));
        return false;
    }
    static async Task<string> AwaitStart(RaidController controller, HttpResponseUtil http, StartHeadlessRequest request, string session)
    {
        // Pinned upstream callback passes Task<T> to NoBody<T>, serializing Task.Result/Status.
        // Await the unchanged native controller and serialize only its actual response DTO.
        var response = await controller.HandleRaidStartHeadless(session, request).ConfigureAwait(false);
        return http.NoBody(response);
    }
}
