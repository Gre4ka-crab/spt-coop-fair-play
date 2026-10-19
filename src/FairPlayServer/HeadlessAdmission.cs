using FikaServer.Models.Enums;
using FikaServer.Models.Fika.Headless;
using SPTarkov.Server.Core.Models.Common;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace PrivateStack.FairPlay.Server;

public sealed class HeadlessAdmission
{
    readonly object sync = new();
    public static bool Available(HeadlessClientInfo? info) => info != null && info.State == EHeadlessStatus.READY && info.WebSocket?.State == WebSocketState.Open && string.IsNullOrEmpty(info.RequesterSessionID);
    public bool Reserve(ConcurrentDictionary<MongoId, HeadlessClientInfo> clients, MongoId id, string requester)
    {
        lock (sync)
        {
            if (!clients.TryGetValue(id, out var info) || !Available(info) || clients.Values.Any(h => h.RequesterSessionID == requester)) return false;
            // Native StartRaid sets this same field. Reserve before leaving the synchronous prefix.
            // Uncertain SendAsync failure keeps it reserved until native reset/reconnect, never auto-retry.
            info.RequesterSessionID = requester; return true;
        }
    }
}
