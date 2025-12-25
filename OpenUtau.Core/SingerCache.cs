using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core {
    /// <summary>
    /// Cache manager for singer metadata to improve loading performance.
    /// Stores lightweight singer information to avoid expensive disk I/O.
    /// </summary>
    public class SingerCache {
        private const string CacheFileName = "singer_cache.json";
        private readonly string cacheFilePath;
        private ConcurrentDictionary<string, CachedSingerInfo> cache;
        private readonly object lockObject = new object();

        public SingerCache(string cacheDirectory) {
            cacheFilePath = Path.Combine(cacheDirectory, CacheFileName);
            cache = new ConcurrentDictionary<string, CachedSingerInfo>();
            Load();
        }

        /// <summary>
        /// Loads the cache from disk.
        /// </summary>
        public void Load() {
            lock (lockObject) {
                try {
                    if (!File.Exists(cacheFilePath)) {
                        Log.Information("Singer cache file not found, starting with empty cache");
                        return;
                    }

                    var json = File.ReadAllText(cacheFilePath);
                    var entries = JsonSerializer.Deserialize<List<CachedSingerInfo>>(json);
                    
                    if (entries != null) {
                        cache = new ConcurrentDictionary<string, CachedSingerInfo>(
                            entries.ToDictionary(e => e.Id, e => e));
                        Log.Information($"Loaded {cache.Count} singer(s) from cache");
                    }
                } catch (Exception e) {
                    Log.Warning(e, "Failed to load singer cache, starting fresh");
                    cache = new ConcurrentDictionary<string, CachedSingerInfo>();
                }
            }
        }

        /// <summary>
        /// Saves the cache to disk.
        /// </summary>
        public void Save() {
            lock (lockObject) {
                try {
                    var directory = Path.GetDirectoryName(cacheFilePath);
                    if (!Directory.Exists(directory)) {
                        Directory.CreateDirectory(directory);
                    }

                    var entries = cache.Values.ToList();
                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions {
                        WriteIndented = true
                    });
                    
                    File.WriteAllText(cacheFilePath, json);
                    Log.Debug($"Saved {entries.Count} singer(s) to cache");
                } catch (Exception e) {
                    Log.Warning(e, "Failed to save singer cache");
                }
            }
        }

        /// <summary>
        /// Gets cached info for a singer.
        /// </summary>
        public CachedSingerInfo Get(string singerId) {
            if (string.IsNullOrEmpty(singerId)) {
                return null;
            }

            cache.TryGetValue(singerId, out var info);
            return info;
        }

        /// <summary>
        /// Updates or adds a singer to the cache.
        /// </summary>
        public void Update(USinger singer) {
            if (singer == null || string.IsNullOrEmpty(singer.Id)) {
                return;
            }

            try {
                var info = new CachedSingerInfo {
                    Id = singer.Id,
                    Name = singer.Name,
                    Location = singer.Location,
                    SingerType = singer.SingerType,
                    LastModified = GetLastModified(singer.Location),
                    CachedAt = DateTime.UtcNow
                };

                cache[singer.Id] = info;
            } catch (Exception e) {
                Log.Warning(e, $"Failed to update cache for singer {singer.Id}");
            }
        }

        /// <summary>
        /// Checks if a singer needs to be reloaded based on file modification time.
        /// </summary>
        public bool NeedsReload(string singerId, string location) {
            if (string.IsNullOrEmpty(singerId) || string.IsNullOrEmpty(location)) {
                return true;
            }

            var cached = Get(singerId);
            if (cached == null) {
                return true; // Not in cache
            }

            try {
                var currentModified = GetLastModified(location);
                return currentModified > cached.LastModified;
            } catch (Exception e) {
                Log.Warning(e, $"Failed to check reload status for {singerId}");
                return true; // Assume needs reload on error
            }
        }

        /// <summary>
        /// Removes a singer from the cache.
        /// </summary>
        public void Remove(string singerId) {
            if (!string.IsNullOrEmpty(singerId)) {
                cache.TryRemove(singerId, out _);
            }
        }

        /// <summary>
        /// Clears all cached data.
        /// </summary>
        public void Clear() {
            lock (lockObject) {
                cache.Clear();
                Log.Information("Singer cache cleared");
            }
        }

        /// <summary>
        /// Gets statistics about the cache.
        /// </summary>
        public CacheStatistics GetStatistics() {
            return new CacheStatistics {
                TotalEntries = cache.Count,
                OldestEntry = cache.Values.Any() ? cache.Values.Min(v => v.CachedAt) : DateTime.MinValue,
                NewestEntry = cache.Values.Any() ? cache.Values.Max(v => v.CachedAt) : DateTime.MinValue,
                EntriesByType = cache.Values
                    .GroupBy(v => v.SingerType)
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        /// <summary>
        /// Cleans up cache entries for singers that no longer exist.
        /// </summary>
        public int CleanupStale() {
            int removed = 0;
            var toRemove = new List<string>();

            foreach (var entry in cache) {
                if (!string.IsNullOrEmpty(entry.Value.Location) && !File.Exists(entry.Value.Location)) {
                    toRemove.Add(entry.Key);
                }
            }

            foreach (var key in toRemove) {
                if (cache.TryRemove(key, out _)) {
                    removed++;
                }
            }

            if (removed > 0) {
                Log.Information($"Removed {removed} stale cache entries");
            }

            return removed;
        }

        private DateTime GetLastModified(string filePath) {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                return DateTime.MinValue;
            }

            try {
                return new FileInfo(filePath).LastWriteTimeUtc;
            } catch {
                return DateTime.MinValue;
            }
        }
    }

    /// <summary>
    /// Cached information about a singer.
    /// </summary>
    public class CachedSingerInfo {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public USingerType SingerType { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime CachedAt { get; set; }
    }

    /// <summary>
    /// Statistics about the singer cache.
    /// </summary>
    public class CacheStatistics {
        public int TotalEntries { get; set; }
        public DateTime OldestEntry { get; set; }
        public DateTime NewestEntry { get; set; }
        public Dictionary<USingerType, int> EntriesByType { get; set; }

        public override string ToString() {
            var typeBreakdown = string.Join(", ", 
                EntriesByType.Select(kv => $"{kv.Key}: {kv.Value}"));
            return $"Cache: {TotalEntries} entries ({typeBreakdown})";
        }
    }
}
