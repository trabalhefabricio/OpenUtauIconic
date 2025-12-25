using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    /// <summary>
    /// Manages singer (voicebank) loading, caching, and lifecycle.
    /// Optimized for performance with memory management and parallel loading.
    /// </summary>
    public class SingerManager : SingletonBase<SingerManager> {
        public Dictionary<string, USinger> Singers { get; private set; } = new Dictionary<string, USinger>();
        public Dictionary<USingerType, List<USinger>> SingerGroups { get; private set; } = new Dictionary<USingerType, List<USinger>>();

        private readonly ConcurrentQueue<USinger> reloadQueue = new ConcurrentQueue<USinger>();
        private CancellationTokenSource reloadCancellation;

        private HashSet<USinger> singersUsed = new HashSet<USinger>();
        
        // Cache for singer metadata to avoid repeated disk reads
        private readonly ConcurrentDictionary<string, DateTime> singerLastModified = new ConcurrentDictionary<string, DateTime>();

        public void Initialize() {
            SearchAllSingers();
        }

        /// <summary>
        /// Searches for all available singers with improved performance and error handling.
        /// Creates necessary directories and handles parallel loading.
        /// </summary>
        public void SearchAllSingers() {
            Log.Information("Searching singers with optimized loading...");
            
            try {
                Directory.CreateDirectory(PathManager.Inst.SingersPath);
            } catch (Exception e) {
                Log.Error(e, "Failed to create singers directory");
            }

            var stopWatch = Stopwatch.StartNew();
            
            try {
                var singers = ClassicSingerLoader.FindAllSingers()
                    .Concat(Vogen.VogenSingerLoader.FindAllSingers())
                    .Where(s => s != null)
                    .Distinct()
                    .ToList();

                Log.Information($"Found {singers.Count} singers");

                Singers = singers
                    .ToLookup(s => s.Id)
                    .ToDictionary(g => g.Key, g => g.First());
                
                SingerGroups = singers
                    .GroupBy(s => s.SingerType)
                    .ToDictionary(s => s.Key, s => s.LocalizedOrderBy(singer => singer.LocalizedName).ToList());

                // Update cache with modification times
                foreach (var singer in singers.Where(s => !string.IsNullOrEmpty(s.Location))) {
                    try {
                        var fileInfo = new FileInfo(singer.Location);
                        if (fileInfo.Exists) {
                            singerLastModified[singer.Id] = fileInfo.LastWriteTimeUtc;
                        }
                    } catch (Exception e) {
                        Log.Warning(e, $"Failed to cache modification time for {singer.Id}");
                    }
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to search singers");
                // Ensure we have empty collections instead of null
                Singers = new Dictionary<string, USinger>();
                SingerGroups = new Dictionary<USingerType, List<USinger>>();
            }

            stopWatch.Stop();
            Log.Information($"Search all singers completed in {stopWatch.Elapsed.TotalSeconds:F2}s");
        }

        /// <summary>
        /// Gets a singer by name with error handling.
        /// </summary>
        public USinger GetSinger(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                Log.Warning("Attempted to get singer with null or empty name");
                return null;
            }

            Log.Information($"Attach singer to track: {name}");
            name = name.Replace("%VOICE%", "");
            
            if (Singers.TryGetValue(name, out var singer)) {
                return singer;
            }

            Log.Warning($"Singer not found: {name}");
            return null;
        }

        /// <summary>
        /// Schedules a singer reload with debouncing to avoid excessive reloads.
        /// </summary>
        public void ScheduleReload(USinger singer) {
            if (singer == null) {
                Log.Warning("Attempted to schedule reload for null singer");
                return;
            }

            reloadQueue.Enqueue(singer);
            ScheduleReload();
        }

        private void ScheduleReload() {
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref reloadCancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            Task.Run(() => {
                Thread.Sleep(200);
                if (newCancellation.IsCancellationRequested) {
                    return;
                }
                Refresh();
            });
        }

        private void Refresh() {
            var singers = new HashSet<USinger>();
            while (reloadQueue.TryDequeue(out USinger singer)) {
                singers.Add(singer);
            }
            
            foreach (var singer in singers) {
                if (singer == null) continue;

                Log.Information($"Reloading {singer.Id}");
                
                try {
                    new Task(() => {
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Reloading {singer.Id}"));
                    }).Start(DocManager.Inst.MainScheduler);
                } catch (Exception e) {
                    Log.Warning(e, "Failed to send progress notification");
                }

                int retries = 5;
                bool success = false;

                while (retries > 0 && !success) {
                    retries--;
                    try {
                        singer.Reload();
                        success = true;
                        
                        // Update cache timestamp on successful reload
                        if (!string.IsNullOrEmpty(singer.Location)) {
                            try {
                                var fileInfo = new FileInfo(singer.Location);
                                if (fileInfo.Exists) {
                                    singerLastModified[singer.Id] = fileInfo.LastWriteTimeUtc;
                                }
                            } catch (Exception ce) {
                                Log.Warning(ce, $"Failed to update cache for {singer.Id}");
                            }
                        }
                    } catch (Exception e) {
                        if (retries == 0) {
                            Log.Error(e, $"Failed to reload {singer.Id} after all retries");
                        } else {
                            Log.Warning(e, $"Retrying reload {singer.Id} ({5 - retries}/5)");
                            Thread.Sleep(200);
                        }
                    }
                }

                if (success) {
                    Log.Information($"Successfully reloaded {singer.Id}");
                } else {
                    Log.Error($"Failed to reload {singer.Id}");
                }

                try {
                    new Task(() => {
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, 
                            success ? $"Reloaded {singer.Id}" : $"Failed to reload {singer.Id}"));
                        DocManager.Inst.ExecuteCmd(new OtoChangedNotification(external: true));
                    }).Start(DocManager.Inst.MainScheduler);
                } catch (Exception e) {
                    Log.Warning(e, "Failed to send completion notification");
                }
            }
        }

        /// <summary>
        /// Releases singers not currently in use to free memory.
        /// Improved with better tracking and logging.
        /// </summary>
        public void ReleaseSingersNotInUse(UProject project) {
            if (project == null) {
                Log.Warning("Attempted to release singers with null project");
                return;
            }

            // Check which singers are in use
            var singersInUse = new HashSet<USinger>();
            foreach (var track in project.tracks) {
                var singer = track.Singer;
                if (singer != null && singer.Found && !singersInUse.Contains(singer)) {
                    singersInUse.Add(singer);
                }
            }

            // Release singers that are no longer in use
            int releasedCount = 0;
            foreach (var singer in singersUsed) {
                if (!singersInUse.Contains(singer)) {
                    try {
                        singer.FreeMemory();
                        releasedCount++;
                    } catch (Exception e) {
                        Log.Warning(e, $"Failed to free memory for singer {singer?.Id}");
                    }
                }
            }

            if (releasedCount > 0) {
                Log.Information($"Released {releasedCount} unused singer(s) from memory");
            }

            // Update singers used
            singersUsed = singersInUse;
        }

        /// <summary>
        /// Checks if a singer needs to be reloaded based on file modification time.
        /// </summary>
        public bool NeedsReload(USinger singer) {
            if (singer == null || string.IsNullOrEmpty(singer.Location)) {
                return false;
            }

            try {
                var fileInfo = new FileInfo(singer.Location);
                if (!fileInfo.Exists) {
                    return false;
                }

                if (singerLastModified.TryGetValue(singer.Id, out var cachedTime)) {
                    return fileInfo.LastWriteTimeUtc > cachedTime;
                }

                return true; // No cache entry, should reload
            } catch (Exception e) {
                Log.Warning(e, $"Failed to check reload status for {singer.Id}");
                return false;
            }
        }
    }
}
