using FikaServer.Models.Fika.Routes.Client.Check;

namespace PrivateStack.FairPlay.Server;

public static class ModRules
{
    public static void Validate(bool isHeadless, FikaCheckModRequestData request, FikaCheckModResponse result, uint expected)
    {
        const string guid = "private.spt.clientfairplay";
        if (isHeadless)
        {
            if (request.ContainsKey(guid) && !result.Forbidden.Contains(guid)) result.Forbidden.Add(guid);
            return;
        }
        if (!request.TryGetValue(guid, out var crc))
        {
            if (!result.MissingRequired.Contains(guid)) result.MissingRequired.Add(guid);
            return;
        }
        if (crc != expected)
        {
            if (!result.HashMismatch.Contains(guid)) result.HashMismatch.Add(guid);
            return;
        }
        // Role-allow only this exact own build. Never clear another mod's native validation failure.
        result.Forbidden.RemoveAll(g => g == guid);
    }
}
