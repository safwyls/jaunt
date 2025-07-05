using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace Jaunt.Util;

public class LRUSoundCache<T> where T : IDisposable
{
    private readonly int capacity;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> cacheMap;
    private readonly LinkedList<CacheEntry> lruList;

    private class CacheEntry
    {
        public string Key;
        public T Value;
    }

    public LRUSoundCache(int capacity)
    {
        this.capacity = capacity;
        cacheMap = new Dictionary<string, LinkedListNode<CacheEntry>>();
        lruList = new LinkedList<CacheEntry>();
    }

    public T Get(string key)
    {
        if (!cacheMap.TryGetValue(key, out var node)) return default;

        // Move node to front (most recently used)
        lruList.Remove(node);
        lruList.AddFirst(node);
        return node.Value.Value;
    }

    public void Add(string key, T value)
    {
        if (cacheMap.TryGetValue(key, out var node))
        {
            // Update and move to front
            lruList.Remove(node);
            node.Value.Value = value;
            lruList.AddFirst(node);
        }
        else
        {
            // Evict LRU if full
            if (cacheMap.Count >= capacity)
            {
                var lruNode = lruList.Last;
                if (lruNode == null) return; // Should not happen, but just in case
                if (((ILoadedSound)lruNode.Value.Value).HasStopped)
                {
                    // If the sound is stopped, we can safely dispose it
                    lruList.RemoveLast();
                    cacheMap.Remove(lruNode.Value.Key);
                    lruNode.Value.Value.Dispose();  // Dispose the evicted sound
                }
            }

            var newNode = new LinkedListNode<CacheEntry>(new CacheEntry { Key = key, Value = value });
            lruList.AddFirst(newNode);
            cacheMap[key] = newNode;
        }
    }

    public bool Contains(string key)
    {
        return cacheMap.ContainsKey(key);
    }

    public void Clear()
    {
        foreach (var node in lruList) node.Value.Dispose();

        lruList.Clear();
        cacheMap.Clear();
    }
}