namespace FilePilot.App.Widget;

/// <summary>Only a resident instance retains the named kernel object.</summary>
internal sealed class ResidentInstance : IDisposable
{
    private readonly Mutex mutex;
    private ResidentInstance(Mutex mutex) => this.mutex = mutex;
    internal static ResidentInstance? TryStart(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name, out var created);
        if (created) return new(mutex);
        // A one-shot fallback must not keep the resident marker alive after its owner exits.
        mutex.Dispose();
        return null;
    }
    public void Dispose() => mutex.Dispose();
}
