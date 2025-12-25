using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Classic {
    /// <summary>
    /// Manages resamplers and wavtools with improved error handling and validation.
    /// Provides centralized access to audio processing tools.
    /// </summary>
    public class ToolsManager : SingletonBase<ToolsManager> {
        static object _locker = new object();

        private readonly List<IResampler> resamplers = new List<IResampler>();
        private readonly List<IWavtool> wavtools = new List<IWavtool>();
        private readonly Dictionary<string, IResampler> resamplersMap
            = new Dictionary<string, IResampler>();
        private readonly Dictionary<string, IWavtool> wavtoolsMap
            = new Dictionary<string, IWavtool>();

        // Track failed tools to avoid repeated loading attempts
        private readonly HashSet<string> failedResamplers = new HashSet<string>();
        private readonly HashSet<string> failedWavtools = new HashSet<string>();

        public List<IResampler> Resamplers {
            get {
                lock (_locker) {
                    return resamplers.ToList();
                }
            }
        }

        public List<IWavtool> Wavtools {
            get {
                lock (_locker) {
                    return wavtools.ToList();
                }
            }
        }

        /// <summary>
        /// Loads a resampler with validation and error handling.
        /// </summary>
        IResampler LoadResampler(string filePath, string basePath) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                return null;
            }

            if (failedResamplers.Contains(filePath)) {
                return null; // Already tried and failed
            }

            if (!File.Exists(filePath)) {
                Log.Debug($"Resampler file not found: {filePath}");
                return null;
            }

            try {
                string ext = Path.GetExtension(filePath).ToLower();
                
                if ((OS.IsWindows() || !string.IsNullOrEmpty(Preferences.Default.WinePath)) && 
                    (ext == ".exe" || ext == ".bat")) {
                    var resampler = new ExeResampler(filePath, basePath);
                    Log.Information($"Loaded resampler: {filePath}");
                    return resampler;
                } 
                
                if (!OS.IsWindows() && (ext == ".sh" || string.IsNullOrEmpty(ext))) {
                    var resampler = new ExeResampler(filePath, basePath);
                    Log.Information($"Loaded resampler: {filePath}");
                    return resampler;
                }

                Log.Debug($"Unsupported resampler extension: {ext} for {filePath}");
            } catch (Exception e) {
                Log.Warning(e, $"Failed to load resampler: {filePath}");
                failedResamplers.Add(filePath);
            }

            return null;
        }

        /// <summary>
        /// Loads a wavtool with validation and error handling.
        /// </summary>
        IWavtool LoadWavtool(string filePath, string basePath) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                return null;
            }

            if (failedWavtools.Contains(filePath)) {
                return null; // Already tried and failed
            }

            if (!File.Exists(filePath)) {
                Log.Debug($"Wavtool file not found: {filePath}");
                return null;
            }

            try {
                string ext = Path.GetExtension(filePath).ToLower();
                
                if ((OS.IsWindows() || !string.IsNullOrEmpty(Preferences.Default.WinePath)) && 
                    (ext == ".exe" || ext == ".bat")) {
                    var wavtool = new ExeWavtool(filePath, basePath);
                    Log.Information($"Loaded wavtool: {filePath}");
                    return wavtool;
                } 
                
                if (!OS.IsWindows() && (ext == ".sh" || string.IsNullOrEmpty(ext))) {
                    var wavtool = new ExeWavtool(filePath, basePath);
                    Log.Information($"Loaded wavtool: {filePath}");
                    return wavtool;
                }

                Log.Debug($"Unsupported wavtool extension: {ext} for {filePath}");
            } catch (Exception e) {
                Log.Warning(e, $"Failed to load wavtool: {filePath}");
                failedWavtools.Add(filePath);
            }

            return null;
        }

        public void Initialize() {
            lock (_locker) {
                Log.Information("Initializing ToolsManager...");
                SearchResamplers();
                SearchWavtools();
                Log.Information($"Loaded {resamplers.Count} resampler(s) and {wavtools.Count} wavtool(s)");
            }
        }

        /// <summary>
        /// Searches for resamplers with improved error handling and logging.
        /// </summary>
        public void SearchResamplers() {
            resamplers.Clear();
            resamplersMap.Clear();
            failedResamplers.Clear();

            // Always add built-in Worldline resampler
            try {
                resamplers.Add(new WorldlineResampler());
                Log.Information("Added built-in Worldline resampler");
            } catch (Exception e) {
                Log.Error(e, "Failed to add Worldline resampler");
            }

            string basePath = PathManager.Inst.ResamplersPath;
            
            try {
                Directory.CreateDirectory(basePath);
                Log.Information($"Searching for resamplers in: {basePath}");

                int foundCount = 0;
                foreach (var file in Directory.EnumerateFiles(basePath, "*", new EnumerationOptions() {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                })) {
                    var driver = LoadResampler(file, basePath);
                    if (driver != null) {
                        resamplers.Add(driver);
                        foundCount++;
                    }
                }

                Log.Information($"Found {foundCount} external resampler(s)");
            } catch (Exception e) {
                Log.Error(e, "Failed to search resamplers.");
                // Keep built-in resampler even if search fails
            }

            foreach (var resampler in resamplers) {
                try {
                    string key = resampler.ToString();
                    if (!resamplersMap.ContainsKey(key)) {
                        resamplersMap[key] = resampler;
                    } else {
                        Log.Warning($"Duplicate resampler name: {key}");
                    }
                } catch (Exception e) {
                    Log.Warning(e, "Failed to register resampler");
                }
            }
        }

        /// <summary>
        /// Searches for wavtools with improved error handling and logging.
        /// </summary>
        public void SearchWavtools() {
            wavtools.Clear();
            wavtoolsMap.Clear();
            failedWavtools.Clear();

            // Always add built-in wavtools
            try {
                wavtools.Add(new SharpWavtool(true));
                wavtools.Add(new SharpWavtool(false));
                Log.Information("Added built-in SharpWavtools");
            } catch (Exception e) {
                Log.Error(e, "Failed to add SharpWavtools");
            }

            string basePath = PathManager.Inst.WavtoolsPath;
            
            try {
                Directory.CreateDirectory(basePath);
                Log.Information($"Searching for wavtools in: {basePath}");

                int foundCount = 0;
                foreach (var file in Directory.EnumerateFiles(basePath, "*", new EnumerationOptions() {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                })) {
                    var driver = LoadWavtool(file, basePath);
                    if (driver != null) {
                        wavtools.Add(driver);
                        foundCount++;
                    }
                }

                Log.Information($"Found {foundCount} external wavtool(s)");
            } catch (Exception e) {
                Log.Error(e, "Failed to search wavtools.");
                // Keep built-in wavtools even if search fails
            }

            foreach (var wavtool in wavtools) {
                try {
                    string key = wavtool.ToString();
                    if (!wavtoolsMap.ContainsKey(key)) {
                        wavtoolsMap[key] = wavtool;
                    } else {
                        Log.Warning($"Duplicate wavtool name: {key}");
                    }
                } catch (Exception e) {
                    Log.Warning(e, "Failed to register wavtool");
                }
            }
        }

        /// <summary>
        /// Gets a resampler by name with fallback to default.
        /// </summary>
        public IResampler GetResampler(string name) {
            lock (_locker) {
                if (!string.IsNullOrWhiteSpace(name) && resamplersMap.TryGetValue(name, out var resampler)) {
                    return resampler;
                }
                
                // Fallback to Worldline
                if (resamplersMap.TryGetValue(WorldlineResampler.name, out var worldline)) {
                    if (!string.IsNullOrWhiteSpace(name)) {
                        Log.Warning($"Resampler '{name}' not found, using Worldline");
                    }
                    return worldline;
                }

                Log.Error("No resamplers available!");
                return resamplers.FirstOrDefault();
            }
        }

        /// <summary>
        /// Gets a wavtool by name with fallback to default.
        /// </summary>
        public IWavtool GetWavtool(string name) {
            lock (_locker) {
                if (!string.IsNullOrWhiteSpace(name) && wavtoolsMap.TryGetValue(name, out var wavtool)) {
                    return wavtool;
                }
                
                // Fallback to SharpWavtool convergence
                if (wavtoolsMap.TryGetValue(SharpWavtool.nameConvergence, out var sharp)) {
                    if (!string.IsNullOrWhiteSpace(name)) {
                        Log.Warning($"Wavtool '{name}' not found, using SharpWavtool");
                    }
                    return sharp;
                }

                Log.Error("No wavtools available!");
                return wavtools.FirstOrDefault();
            }
        }
    }
}
