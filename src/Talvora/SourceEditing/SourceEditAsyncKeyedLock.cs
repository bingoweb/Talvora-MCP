using System.Collections.Concurrent;

namespace Talvora.SourceEditing;

internal sealed class SourceEditAsyncKeyedLock
{
    private readonly ConcurrentDictionary<string, Entry> entries;

    public SourceEditAsyncKeyedLock(IEqualityComparer<string> comparer)
    {
        entries = new ConcurrentDictionary<string, Entry>(comparer);
    }

    public async ValueTask<IDisposable> AcquireAsync(
        string key,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries.GetOrAdd(
                key,
                static _ => new Entry());

            lock (entry.Sync)
            {
                if (!entries.TryGetValue(key, out var current) ||
                    !ReferenceEquals(current, entry))
                {
                    continue;
                }

                entry.References++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new Releaser(this, key, entry);
            }
            catch
            {
                ReleaseReference(key, entry);
                throw;
            }
        }
    }

    private void Release(
        string key,
        Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(
        string key,
        Entry entry)
    {
        lock (entry.Sync)
        {
            entry.References--;
            if (entry.References == 0)
            {
                entries.TryRemove(
                    new KeyValuePair<string, Entry>(
                        key,
                        entry));
            }
        }
    }

    private sealed class Entry
    {
        public object Sync { get; } = new();
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class Releaser(
        SourceEditAsyncKeyedLock owner,
        string key,
        Entry entry)
        : IDisposable
    {
        private SourceEditAsyncKeyedLock? owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(
                ref owner,
                null);
            current?.Release(key, entry);
        }
    }
}
