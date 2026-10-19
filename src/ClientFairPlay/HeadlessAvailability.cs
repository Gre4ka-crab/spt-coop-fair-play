using System;
using System.Collections.Generic;
using System.Linq;

namespace PrivateStack.FairPlay;

// No Unity/network dependencies: times are monotonic milliseconds, IDs never UI indices.
public sealed class HeadlessAvailability
{
    public sealed class Host
    {
        public string Id { get; }
        public string Alias { get; }
        public Host(string id, string alias) { Id = id; Alias = alias; }
    }
    public const long FreshFor = 5000;
    public Host[] Hosts { get; private set; } = Array.Empty<Host>();
    public string SelectedId { get; private set; }
    public bool Busy { get; private set; }
    public bool Uncertain { get; private set; }
    public string Error { get; private set; }
    long observed;
    bool valid, everSelected;
    public bool Fresh(long now) => valid && now >= observed && now - observed < FreshFor;
    public bool CanHost(long now) => Fresh(now) && Hosts.Length > 0 && !Busy && !Uncertain;
    public bool CanStart(long now) => CanHost(now) && SelectedId != null && Hosts.Any(h => h.Id == SelectedId);
    public void Receive(IEnumerable<Host> hosts, long now)
    {
        valid = false;
        var values = hosts?.ToArray() ?? throw new InvalidOperationException("HEADLESS_INVALID_RESPONSE");
        if (values.Any(h => h == null || string.IsNullOrWhiteSpace(h.Id)) || values.Select(h => h.Id).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException("HEADLESS_INVALID_RESPONSE");
        Hosts = values.OrderBy(h => h.Alias, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.Id, StringComparer.Ordinal).ToArray();
        if (!Hosts.Any(h => h.Id == SelectedId)) SelectedId = null;
        if (Hosts.Length == 1 && !everSelected) Select(Hosts[0].Id);
        observed = now; valid = true; Error = null;
    }
    public void Select(string id)
    {
        SelectedId = Hosts.Any(h => h.Id == id) ? id : null;
        if (SelectedId != null) everSelected = true;
    }
    public bool Begin(long now)
    {
        if (!CanStart(now)) return false;
        Busy = true; valid = false; return true;
    }
    public bool Confirmed(string requestedId, long now) => Busy && Fresh(now) && SelectedId == requestedId && Hosts.Any(h => h.Id == requestedId);
    public void Fail(string message) { valid = false; Error = message; }
    public void Release(string message, bool uncertain = false) { Busy = false; Uncertain |= uncertain; Fail(message); }
}
