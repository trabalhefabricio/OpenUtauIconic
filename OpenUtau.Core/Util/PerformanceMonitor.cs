using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// Performance monitoring and profiling utility for tracking operation times.
    /// Helps identify bottlenecks and optimize performance.
    /// </summary>
    public class PerformanceMonitor {
        private static readonly ConcurrentDictionary<string, OperationStats> stats 
            = new ConcurrentDictionary<string, OperationStats>();

        private static bool enabled = true;

        /// <summary>
        /// Enables or disables performance monitoring.
        /// </summary>
        public static bool Enabled {
            get => enabled;
            set => enabled = value;
        }

        /// <summary>
        /// Starts timing an operation.
        /// </summary>
        public static IDisposable Time(string operationName) {
            if (!enabled) {
                return new NoOpTimer();
            }

            return new OperationTimer(operationName);
        }

        /// <summary>
        /// Records an operation duration.
        /// </summary>
        public static void Record(string operationName, TimeSpan duration) {
            if (!enabled) {
                return;
            }

            stats.AddOrUpdate(operationName,
                key => new OperationStats(key, duration),
                (key, existing) => {
                    existing.Record(duration);
                    return existing;
                });
        }

        /// <summary>
        /// Gets statistics for a specific operation.
        /// </summary>
        public static OperationStats GetStats(string operationName) {
            stats.TryGetValue(operationName, out var result);
            return result;
        }

        /// <summary>
        /// Gets all operation statistics.
        /// </summary>
        public static IReadOnlyDictionary<string, OperationStats> GetAllStats() {
            return stats;
        }

        /// <summary>
        /// Gets the slowest operations.
        /// </summary>
        public static IEnumerable<OperationStats> GetSlowestOperations(int count = 10) {
            return stats.Values
                .OrderByDescending(s => s.AverageDuration)
                .Take(count);
        }

        /// <summary>
        /// Gets the most frequent operations.
        /// </summary>
        public static IEnumerable<OperationStats> GetMostFrequentOperations(int count = 10) {
            return stats.Values
                .OrderByDescending(s => s.Count)
                .Take(count);
        }

        /// <summary>
        /// Clears all statistics.
        /// </summary>
        public static void Clear() {
            stats.Clear();
            Log.Information("Performance statistics cleared");
        }

        /// <summary>
        /// Logs a summary of performance statistics.
        /// </summary>
        public static void LogSummary() {
            if (!enabled || stats.IsEmpty) {
                Log.Information("Performance monitoring: No data collected");
                return;
            }

            Log.Information("=== Performance Summary ===");
            Log.Information($"Total operations tracked: {stats.Count}");

            var slowest = GetSlowestOperations(5).ToList();
            if (slowest.Any()) {
                Log.Information("Slowest operations (avg):");
                foreach (var stat in slowest) {
                    Log.Information($"  {stat.Name}: {stat.AverageDuration.TotalMilliseconds:F2}ms " +
                                  $"(count: {stat.Count}, total: {stat.TotalDuration.TotalSeconds:F2}s)");
                }
            }

            var frequent = GetMostFrequentOperations(5).ToList();
            if (frequent.Any()) {
                Log.Information("Most frequent operations:");
                foreach (var stat in frequent) {
                    Log.Information($"  {stat.Name}: {stat.Count} times " +
                                  $"(avg: {stat.AverageDuration.TotalMilliseconds:F2}ms)");
                }
            }
        }

        private class OperationTimer : IDisposable {
            private readonly string operationName;
            private readonly Stopwatch stopwatch;

            public OperationTimer(string operationName) {
                this.operationName = operationName;
                this.stopwatch = Stopwatch.StartNew();
            }

            public void Dispose() {
                stopwatch.Stop();
                Record(operationName, stopwatch.Elapsed);
            }
        }

        private class NoOpTimer : IDisposable {
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Statistics for a specific operation.
    /// </summary>
    public class OperationStats {
        private readonly object lockObject = new object();
        private long totalTicks;
        private long count;
        private long minTicks = long.MaxValue;
        private long maxTicks;

        public string Name { get; }
        
        public long Count {
            get {
                lock (lockObject) {
                    return count;
                }
            }
        }

        public TimeSpan TotalDuration {
            get {
                lock (lockObject) {
                    return TimeSpan.FromTicks(totalTicks);
                }
            }
        }

        public TimeSpan AverageDuration {
            get {
                lock (lockObject) {
                    return count > 0 ? TimeSpan.FromTicks(totalTicks / count) : TimeSpan.Zero;
                }
            }
        }

        public TimeSpan MinDuration {
            get {
                lock (lockObject) {
                    return minTicks != long.MaxValue ? TimeSpan.FromTicks(minTicks) : TimeSpan.Zero;
                }
            }
        }

        public TimeSpan MaxDuration {
            get {
                lock (lockObject) {
                    return TimeSpan.FromTicks(maxTicks);
                }
            }
        }

        public OperationStats(string name, TimeSpan initialDuration) {
            Name = name;
            Record(initialDuration);
        }

        public void Record(TimeSpan duration) {
            lock (lockObject) {
                var ticks = duration.Ticks;
                totalTicks += ticks;
                count++;
                
                if (ticks < minTicks) {
                    minTicks = ticks;
                }
                
                if (ticks > maxTicks) {
                    maxTicks = ticks;
                }
            }
        }

        public override string ToString() {
            return $"{Name}: count={Count}, avg={AverageDuration.TotalMilliseconds:F2}ms, " +
                   $"min={MinDuration.TotalMilliseconds:F2}ms, max={MaxDuration.TotalMilliseconds:F2}ms";
        }
    }

    /// <summary>
    /// Extension methods for performance monitoring.
    /// </summary>
    public static class PerformanceMonitorExtensions {
        /// <summary>
        /// Executes an action with performance monitoring.
        /// </summary>
        public static void WithTiming(this Action action, string operationName) {
            using (PerformanceMonitor.Time(operationName)) {
                action();
            }
        }

        /// <summary>
        /// Executes a function with performance monitoring.
        /// </summary>
        public static T WithTiming<T>(this Func<T> func, string operationName) {
            using (PerformanceMonitor.Time(operationName)) {
                return func();
            }
        }
    }
}
