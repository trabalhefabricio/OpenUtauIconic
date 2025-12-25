using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Classic {
    public static class ClassicSingerLoader {
        static USinger AdjustSingerType(Voicebank v) {
            switch (v.SingerType) {
                case USingerType.Enunu:
                    return new Core.Enunu.EnunuSinger(v) as USinger;
                case USingerType.DiffSinger:
                    return new Core.DiffSinger.DiffSingerSinger(v) as USinger;
                case USingerType.Voicevox:
                    return new Core.Voicevox.VoicevoxSinger(v) as USinger;
                default:
                    return new ClassicSinger(v) as USinger;
            }
        }

        /// <summary>
        /// Finds all singers with parallel loading for better performance.
        /// Uses concurrent processing to speed up singer discovery.
        /// </summary>
        public static IEnumerable<USinger> FindAllSingers() {
            var singers = new ConcurrentBag<USinger>();
            var paths = PathManager.Inst.SingersPaths;

            try {
                // Parallel load singers from all paths
                Parallel.ForEach(paths, path => {
                    try {
                        var loader = new VoicebankLoader(path);
                        var voicebanks = loader.SearchAll();
                        
                        foreach (var voicebank in voicebanks) {
                            try {
                                var singer = AdjustSingerType(voicebank);
                                if (singer != null) {
                                    singers.Add(singer);
                                }
                            } catch (Exception e) {
                                Log.Warning(e, $"Failed to load voicebank at {voicebank.File}");
                            }
                        }
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to search singers in path {path}");
                    }
                });
            } catch (Exception e) {
                Log.Error(e, "Failed to find singers with parallel loading, falling back to sequential");
                // Fallback to sequential loading
                return FindAllSingersSequential();
            }

            return singers.ToList();
        }

        /// <summary>
        /// Fallback method for sequential singer loading.
        /// Used when parallel loading fails.
        /// </summary>
        private static IEnumerable<USinger> FindAllSingersSequential() {
            List<USinger> singers = new List<USinger>();
            foreach (var path in PathManager.Inst.SingersPaths) {
                try {
                    var loader = new VoicebankLoader(path);
                    singers.AddRange(loader.SearchAll()
                        .Select(AdjustSingerType)
                        .Where(s => s != null));
                } catch (Exception e) {
                    Log.Error(e, $"Failed to search singers in path {path}");
                }
            }
            return singers;
        }
    }
}
