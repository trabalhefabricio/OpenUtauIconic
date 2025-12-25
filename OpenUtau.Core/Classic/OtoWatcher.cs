using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.Classic {
    /// <summary>
    /// Watches for changes to oto.ini files with debouncing to handle rapid changes.
    /// Improved to reduce excessive reloads and handle errors gracefully.
    /// </summary>
    class OtoWatcher : IDisposable {
        public bool Paused { get; set; }

        private ClassicSinger singer;
        private FileSystemWatcher watcher;
        private CancellationTokenSource debounceCancellation;
        private const int DebounceDelayMs = 500; // Wait 500ms after last change before reloading
        private readonly object lockObject = new object();

        public OtoWatcher(ClassicSinger singer, string path) {
            this.singer = singer;

            if (!Directory.Exists(path)) {
                Log.Warning($"Cannot watch non-existent directory: {path}");
                return;
            }

            try {
                watcher = new FileSystemWatcher(path);
                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += OnFileChanged;
                watcher.Error += OnError;
                watcher.Filter = "oto.ini";
                watcher.IncludeSubdirectories = true;
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
                watcher.EnableRaisingEvents = true;
                
                Log.Information($"Started watching oto.ini files in {path}");
            } catch (Exception e) {
                Log.Error(e, $"Failed to create file watcher for {path}");
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e) {
            if (Paused) {
                return;
            }

            if (watcher == null || singer == null) {
                return;
            }

            Log.Information($"Oto file \"{e.FullPath}\" {e.ChangeType}");

            // Debounce: cancel previous reload and schedule a new one
            lock (lockObject) {
                debounceCancellation?.Cancel();
                debounceCancellation?.Dispose();
                debounceCancellation = new CancellationTokenSource();

                var token = debounceCancellation.Token;
                Task.Run(async () => {
                    try {
                        await Task.Delay(DebounceDelayMs, token);
                        
                        if (!token.IsCancellationRequested) {
                            Log.Information($"Triggering singer reload for {singer.Id} after debounce");
                            SingerManager.Inst.ScheduleReload(singer);
                        }
                    } catch (TaskCanceledException) {
                        // Expected when debouncing
                        Log.Debug("Oto reload debounce cancelled");
                    } catch (Exception ex) {
                        Log.Error(ex, "Error during oto reload debounce");
                    }
                }, token);
            }
        }

        private void OnError(object sender, ErrorEventArgs e) {
            var exception = e.GetException();
            if (exception != null) {
                Log.Error(exception, "File watcher error");
            } else {
                Log.Error("File watcher error (no exception details)");
            }

            // Try to restart the watcher
            try {
                if (watcher != null && !watcher.EnableRaisingEvents) {
                    watcher.EnableRaisingEvents = true;
                    Log.Information("File watcher restarted after error");
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to restart file watcher");
            }
        }

        public void Dispose() {
            lock (lockObject) {
                debounceCancellation?.Cancel();
                debounceCancellation?.Dispose();
                debounceCancellation = null;
            }

            if (watcher != null) {
                try {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                    Log.Information("Oto watcher disposed successfully");
                } catch (Exception e) {
                    Log.Warning(e, "Error disposing oto watcher");
                }
                watcher = null;
            }
        }
    }
}
