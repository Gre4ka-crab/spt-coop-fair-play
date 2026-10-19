namespace PrivateStack.FairPlay;

// Caller supplies monotonic ticks. Faults remain visible; an ordinary denied click does not.
public sealed class NoticeState
{
    public string Text { get; private set; }
    public bool Persistent { get; private set; }
    long until;
    public void Transient(string text, long now, long lifetime)
    {
        if (Persistent) return;
        Text = text; until = now + lifetime;
    }
    public void Fault(string text) { Text = text; Persistent = true; }
    public void ClearTransient() { if (!Persistent) Text = null; }
    public bool Visible(long now) => Text != null && (Persistent || now < until);
}
