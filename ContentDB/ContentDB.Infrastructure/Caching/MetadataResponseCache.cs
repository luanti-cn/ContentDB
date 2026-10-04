using ContentDB.Core.Abstractions;

namespace ContentDB.Infrastructure.Caching;

/// <summary>Host-owned, bounded LRU cache. Eviction never flushes unrelated hot entries.</summary>
public sealed class MetadataResponseCache(TimeProvider clock, int capacity = 500)
{
	private readonly object _sync = new();
	private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
	private readonly LinkedList<Entry> _lru = new();
	private static readonly TimeSpan MaxFresh = TimeSpan.FromSeconds(60);

	public bool TryGet(string key, out CachedApiResult result)
	{
		lock (_sync)
		{
			if (_entries.TryGetValue(key, out var node))
			{
				if (node.Value.ExpiresAt > clock.GetUtcNow())
				{
					_lru.Remove(node);
					_lru.AddLast(node);
					result = node.Value.Result;
					return true;
				}
				Remove(node);
			}
		}
		result = null!;
		return false;
	}

	public void Remember(string key, CachedApiResult result, DateTimeOffset expiresAt)
	{
		var now = clock.GetUtcNow();
		if (capacity <= 0 || result.StatusCode != 200 || expiresAt <= now) return;
		if (expiresAt > now + MaxFresh) expiresAt = now + MaxFresh;
		lock (_sync)
		{
			if (_entries.TryGetValue(key, out var existing)) Remove(existing);
			while (_entries.Count >= capacity) Remove(_lru.First!);
			_entries[key] = _lru.AddLast(new Entry(key, result, expiresAt));
		}
	}

	private void Remove(LinkedListNode<Entry> node)
	{
		_entries.Remove(node.Value.Key);
		_lru.Remove(node);
	}

	private sealed record Entry(string Key, CachedApiResult Result, DateTimeOffset ExpiresAt);
}
