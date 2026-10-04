namespace ContentDB.Infrastructure.Caching;

/// <summary>Process-local keyed exclusion. Entries live only while holders or waiters exist.</summary>
public sealed class KeyedAsyncLock
{
	private readonly object _sync = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

	internal int EntryCount { get { lock (_sync) return _entries.Count; } }

	public async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken ct = default)
	{
		Entry entry;
		lock (_sync)
		{
			if (!_entries.TryGetValue(key, out entry!))
				_entries[key] = entry = new Entry();
			entry.Users++;
		}

		try
		{
			await entry.Gate.WaitAsync(ct);
			return new Lease(this, key, entry);
		}
		catch
		{
			RemoveUser(key, entry);
			throw;
		}
	}

	private void RemoveUser(string key, Entry entry)
	{
		lock (_sync)
		{
			if (--entry.Users == 0)
			{
				_entries.Remove(key);
				entry.Gate.Dispose();
			}
		}
	}

	private sealed class Entry
	{
		public readonly SemaphoreSlim Gate = new(1, 1);
		public int Users;
	}

	private sealed class Lease(KeyedAsyncLock owner, string key, Entry entry) : IDisposable
	{
		private int _disposed;
		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
			entry.Gate.Release();
			owner.RemoveUser(key, entry);
		}
	}
}
