using FikaServer.Controllers;
using FikaServer.Helpers;
using FikaServer.Models.Fika.Config;
using FikaServer.Models.Fika.Routes.Raid.Create;
using FikaServer.Services;
using FikaServer.Services.Headless;
using PrivateStack.FairPlay.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using FikaServer.Models.Fika.Headless;
using FikaServer.Models.Enums;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using FikaServer.Callbacks;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using System.Text.Json;

int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
var config=new FikaServerConfig();
var jsonUtil = new JsonUtil(Array.Empty<IJsonConverterRegistrator>());
var service=new HeadlessService(null!,null!,jsonUtil,config,null!);
var profiles=new HeadlessProfileService(null!,null!,null!,config,null!,null!,null!,null!,null!);
var helper=new HeadlessHelper(config,null!,service,profiles,null!);
var adapter=new FairPlayService(helper,config);
await adapter.OnLoadAsync(CancellationToken.None);
var raid=new RaidController(null!,null!,null!,null!,null!,null!,null!);
var human=new MongoId("111111111111111111111111");
bool denied=false;
try{_ = raid.HandleRaidCreate(new FikaRaidCreateRequestData{ServerId=human,Settings=new()},human);}catch(InvalidOperationException e){denied=e.Message==RaidRules.Denial;}
Check(denied,"actual Fika RaidController prefix rejects human before original null dependencies");
denied=false;try{_ = service.StartHeadlessRaid(human.ToString(),human.ToString(),new(){HeadlessSessionID=human.ToString()});}catch(InvalidOperationException e){denied=e.Message=="FAIRPLAY_INVALID_HEADLESS_REQUEST";}
Check(denied,"actual Fika HeadlessService prefix rejects unconfigured target");
var client=new ClientService(null!,null!,null!,config);
config.Client.CanEditRaidSettings=true;config.Client.AllowFreeCam=true;config.Client.AllowSpectateFreeCam=true;config.Client.AllowSpectateBots=true;
var result=client.GetClientConfig();
Check(!result.CanEditRaidSettings&&!result.AllowFreeCam&&!result.AllowSpectateFreeCam&&!result.AllowSpectateBots,"actual client-config getter postfix reapplies permissions");
// Native unconfigured validation returns early; our postfix still requires the exact own addon.
config.Server.LogClientModsInConsole=false;
var validation=client.GetCheckModsResponse(new(),human);
Check(validation.MissingRequired.Contains("private.spt.clientfairplay"),"actual native validation path adds human addon requirement");
var one = new MongoId("aaaaaaaaaaaaaaaaaaaaaaaa");
var two = new MongoId("bbbbbbbbbbbbbbbbbbbbbbbb");
profiles.HeadlessProfiles.Add(new SptProfile { ProfileInfo = new() { ProfileId = one } });
profiles.HeadlessProfiles.Add(new SptProfile { ProfileInfo = new() { ProfileId = two } });
config.Headless.Profiles.Aliases[one] = "One"; config.Headless.Profiles.Aliases[two] = "Two";
service.HeadlessClients[one] = new(new FixtureSocket(WebSocketState.Open), EHeadlessStatus.READY);
service.HeadlessClients[two] = new(new FixtureSocket(WebSocketState.Open), EHeadlessStatus.READY);
Check(helper.GetAvailableHeadlessClients().Length == 2, "actual native available helper retains two free configured hosts");
service.HeadlessClients[two].RequesterSessionID = human.ToString();
Check(helper.GetAvailableHeadlessClients().Select(h=>h.HeadlessSessionID).SequenceEqual(new[]{one}), "actual helper removes READY host already reserved during loading");
Check(await service.StartHeadlessRaid(two.ToString(), "222222222222222222222222", new(){HeadlessSessionID=two.ToString()}) == null, "actual service prefix denies second requester before native SendAsync");
service.HeadlessClients[two].Reset();
service.HeadlessClients[two].WebSocket = new FixtureSocket(WebSocketState.CloseReceived);
Check(helper.GetAvailableHeadlessClients().Length == 1, "actual helper excludes closing WebSocket despite READY flag");
service.HeadlessClients[two].WebSocket = new FixtureSocket(WebSocketState.Open);
service.HeadlessClients[two].State = EHeadlessStatus.IN_RAID;
Check(helper.GetAvailableHeadlessClients().Length == 1, "actual helper excludes IN_RAID host");
service.HeadlessClients[one].WebSocket = new FixtureSocket(WebSocketState.Closed);
Check(helper.GetAvailableHeadlessClients().Length == 0, "actual helper returns zero rather than local-host fallback");

var gate = new HeadlessAdmission();
var clients = new ConcurrentDictionary<MongoId, HeadlessClientInfo>();
clients[one] = new(new FixtureSocket(WebSocketState.Open), EHeadlessStatus.READY);
clients[two] = new(new FixtureSocket(WebSocketState.Open), EHeadlessStatus.READY);
var attempts = new ConcurrentBag<bool>();
Parallel.For(0, 128, i => attempts.Add(gate.Reserve(clients, one, i.ToString())));
Check(attempts.Count(x=>x)==1, "128 simultaneous fixture requests reserve one headless exactly once");
clients[one].Reset(); clients[two].Reset();
Check(gate.Reserve(clients,one,"same requester") && !gate.Reserve(clients,two,"same requester"), "one requester cannot accidentally start both headless instances");
Check(gate.Reserve(clients,two,"different requester"), "two different requesters can reserve different headless instances");
Check(!HeadlessAdmission.Available(clients[one]) && !HeadlessAdmission.Available(clients[two]), "both reservations excluded before native IN_RAID transition");
clients[one].Reset();
Check(gate.Reserve(clients,one,"same requester"), "native Reset reopens completed headless for the next raid");
clients[one] = new(new FixtureSocket(WebSocketState.Open), EHeadlessStatus.READY);
Check(gate.Reserve(clients,one,"same requester"), "native reconnect new info clears old uncertain reservation");
var constructor = typeof(RaidController).GetConstructors().Single();
var unavailableController = (RaidController)constructor.Invoke(constructor.GetParameters().Select(p=>p.ParameterType==typeof(HeadlessHelper)?(object)helper:null).ToArray());
var callbacks = new RaidCallbacks(new HttpResponseUtil(jsonUtil,null!),unavailableController);
var body = await callbacks.HandleRaidStartHeadless("/fika/raid/headless/start",new(){HeadlessSessionID="000000000000000000000000"},human.ToString());
using var envelope = JsonDocument.Parse(body);
Check(envelope.RootElement.GetProperty("error").GetString()!=null && envelope.RootElement.GetProperty("matchId").ValueKind==JsonValueKind.Null && !envelope.RootElement.TryGetProperty("Result",out _), "actual callback awaits native result and emits direct error DTO instead of serialized Task");
var send = new TaskCompletionSource();
var socket = new FixtureSocket(WebSocketState.Open,send.Task);
service.HeadlessClients[one] = new(socket,EHeadlessStatus.READY);
var accepted = service.StartHeadlessRaid(one.ToString(),human.ToString(),new(){HeadlessSessionID=one.ToString()});
Check(socket.Sent==1 && !accepted.IsCompleted, "actual Fika service serializes and dispatches one native WS command while awaiting controlled send");
Check(await service.StartHeadlessRaid(one.ToString(),"222222222222222222222222",new(){HeadlessSessionID=one.ToString()})==null && socket.Sent==1, "second real Harmony service call cannot dispatch a duplicate command during first SendAsync");
send.SetResult();
Check(await accepted==one.ToString(), "actual Fika service returns selected match ID after native send completion");
Console.WriteLine("{\"status\":\"PASS_REAL_FIKA_HARMONY_DENIAL_FIXTURE_NOT_HTTP_OR_RAID\",\"passed\":"+passed+"}");

sealed class FixtureSocket(WebSocketState state,Task? pendingSend=null) : WebSocket
{
    public int Sent { get; private set; }
    public override WebSocketState State => state;
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;
    public override void Abort() { }
    public override void Dispose() { }
    public override Task CloseAsync(WebSocketCloseStatus s,string? d,CancellationToken t)=>Task.CompletedTask;
    public override Task CloseOutputAsync(WebSocketCloseStatus s,string? d,CancellationToken t)=>Task.CompletedTask;
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b,CancellationToken t)=>throw new NotSupportedException("No real sockets in fixture");
    public override Task SendAsync(ArraySegment<byte> b,WebSocketMessageType m,bool e,CancellationToken t) { Sent++; return pendingSend??Task.CompletedTask; }
}
