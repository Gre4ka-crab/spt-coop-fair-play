using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fika.Core.Networking.Http;
using Fika.Core.Networking.Models.Headless;
using Fika.Core.UI.Custom;
using Fika.Core.Networking.Websocket;

namespace PrivateStack.FairPlay;

static class NativeLobbyApi
{
    // Private members verified against the pinned 2.4.5 DLL. No guessed REST or raw HTTP client.
    static readonly MethodInfo get = typeof(FikaRequestHandler).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(m => m.Name == "GetJsonAsync" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string))
        .MakeGenericMethod(typeof(AvailableHeadlessClientsRequest[]));
    static readonly FieldInfo settings = typeof(MatchMakerUIScript).GetField("RaidSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    static readonly FieldInfo socket = typeof(HeadlessRequesterWebSocket).GetField("_webSocket", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo connectAsync = socket?.FieldType.GetMethod("ConnectAsync", Type.EmptyTypes);
    public static void Validate()
    {
        if (get.ReturnType != typeof(Task<AvailableHeadlessClientsRequest[]>) || settings?.FieldType != typeof(EFT.RaidSettings) || connectAsync?.ReturnType != typeof(void)) throw new InvalidOperationException("FAIRPLAY_NATIVE_LOBBY_API_CONTRACT");
    }
    public static Task<AvailableHeadlessClientsRequest[]> Available() => (Task<AvailableHeadlessClientsRequest[]>)get.Invoke(null, new object[] { "/fika/headless/available" });
    public static EFT.RaidSettings Settings(MatchMakerUIScript owner) => (EFT.RaidSettings)settings.GetValue(owner);
    public static void Connect(HeadlessRequesterWebSocket requester) => connectAsync.Invoke(socket.GetValue(requester), null);
}
