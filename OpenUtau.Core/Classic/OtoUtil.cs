using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace OpenUtau.Classic {
    /// <summary>
    /// Utility class for oto.ini operations including validation, import/export, and manipulation.
    /// Provides comprehensive oto management functionality.
    /// </summary>
    public static class OtoUtil {
        /// <summary>
        /// Validates all otos in a voicebank and returns a report.
        /// </summary>
        public static OtoValidationReport ValidateVoicebank(Voicebank voicebank) {
            var report = new OtoValidationReport();
            
            if (voicebank == null) {
                report.Errors.Add("Voicebank is null");
                return report;
            }

            report.VoicebankName = voicebank.Name;
            report.TotalOtos = voicebank.TotalOtoCount;

            foreach (var otoSet in voicebank.OtoSets) {
                foreach (var oto in otoSet.Otos) {
                    if (!oto.IsValid) {
                        report.InvalidOtos.Add((oto, otoSet.File, oto.Error));
                    } else {
                        // Validate timing even for "valid" otos
                        if (!oto.ValidateTiming(out string error)) {
                            report.Warnings.Add($"Oto '{oto.Alias}' in {otoSet.Name}: {error}");
                        }
                    }

                    // Check for missing wav file
                    if (!string.IsNullOrEmpty(oto.Wav) && !string.IsNullOrEmpty(voicebank.BasePath)) {
                        var wavPath = Path.Combine(Path.GetDirectoryName(otoSet.File), oto.Wav);
                        if (!File.Exists(wavPath)) {
                            report.Warnings.Add($"Oto '{oto.Alias}' references missing wav file: {oto.Wav}");
                        }
                    }
                }
            }

            report.ValidOtos = report.TotalOtos - report.InvalidOtos.Count;
            return report;
        }

        /// <summary>
        /// Finds duplicate aliases in a voicebank.
        /// </summary>
        public static Dictionary<string, List<(OtoSet set, Oto oto)>> FindDuplicateAliases(Voicebank voicebank) {
            var duplicates = new Dictionary<string, List<(OtoSet, Oto)>>();

            if (voicebank == null) {
                return duplicates;
            }

            var aliasGroups = voicebank.OtoSets
                .SelectMany(set => set.Otos.Select(oto => (set, oto)))
                .GroupBy(x => x.oto.Alias)
                .Where(g => g.Count() > 1);

            foreach (var group in aliasGroups) {
                duplicates[group.Key] = group.ToList();
            }

            return duplicates;
        }

        /// <summary>
        /// Exports otos to a string in oto.ini format.
        /// </summary>
        public static string ExportOtosToString(IEnumerable<Oto> otos, Encoding encoding = null) {
            if (otos == null) {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var oto in otos) {
                if (oto == null) continue;

                var line = $"{oto.Wav}={oto.Alias},{oto.Offset:F2},{oto.Consonant:F2},{oto.Cutoff:F2},{oto.Preutter:F2},{oto.Overlap:F2}";
                sb.AppendLine(line);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Backs up an oto.ini file before modification.
        /// </summary>
        public static bool BackupOtoFile(string otoPath) {
            if (string.IsNullOrWhiteSpace(otoPath) || !File.Exists(otoPath)) {
                Log.Warning($"Cannot backup non-existent file: {otoPath}");
                return false;
            }

            try {
                var backupPath = $"{otoPath}.backup_{DateTime.Now:yyyyMMddHHmmss}";
                File.Copy(otoPath, backupPath, true);
                Log.Information($"Created oto backup: {backupPath}");
                return true;
            } catch (Exception e) {
                Log.Error(e, $"Failed to backup oto file: {otoPath}");
                return false;
            }
        }

        /// <summary>
        /// Cleans up old oto backup files, keeping only the most recent ones.
        /// </summary>
        public static void CleanupOtoBackups(string directory, int keepCount = 5) {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) {
                return;
            }

            try {
                var backupFiles = Directory.GetFiles(directory, "oto.ini.backup_*")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(keepCount)
                    .ToList();

                foreach (var file in backupFiles) {
                    try {
                        file.Delete();
                        Log.Debug($"Deleted old backup: {file.Name}");
                    } catch (Exception e) {
                        Log.Warning(e, $"Failed to delete backup: {file.FullName}");
                    }
                }

                if (backupFiles.Any()) {
                    Log.Information($"Cleaned up {backupFiles.Count} old oto backup(s)");
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to cleanup oto backups in: {directory}");
            }
        }

        /// <summary>
        /// Checks if an oto.ini file is locked by another process.
        /// </summary>
        public static bool IsFileLocked(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) {
                return false;
            }

            try {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None)) {
                    stream.Close();
                }
                return false;
            } catch (IOException) {
                return true;
            } catch (Exception e) {
                Log.Warning(e, $"Error checking file lock status: {filePath}");
                return false;
            }
        }

        /// <summary>
        /// Gets statistics about a voicebank's otos.
        /// </summary>
        public static OtoStatistics GetStatistics(Voicebank voicebank) {
            var stats = new OtoStatistics();

            if (voicebank == null) {
                return stats;
            }

            stats.TotalOtos = voicebank.TotalOtoCount;
            stats.ValidOtos = voicebank.ValidOtoCount;
            stats.InvalidOtos = voicebank.InvalidOtoCount;
            stats.OtoSets = voicebank.OtoSets.Count;

            var allOtos = voicebank.GetAllOtos().ToList();
            if (allOtos.Any()) {
                stats.AverageOffset = allOtos.Average(o => o.Offset);
                stats.AverageConsonant = allOtos.Average(o => o.Consonant);
                stats.AveragePreutter = allOtos.Average(o => o.Preutter);
                stats.AverageOverlap = allOtos.Average(o => o.Overlap);
            }

            stats.UniqueAliases = allOtos.Select(o => o.Alias).Distinct().Count();
            stats.UniqueWavFiles = allOtos.Select(o => o.Wav).Distinct().Count();

            return stats;
        }
    }

    /// <summary>
    /// Report of oto validation results.
    /// </summary>
    public class OtoValidationReport {
        public string VoicebankName { get; set; }
        public int TotalOtos { get; set; }
        public int ValidOtos { get; set; }
        public List<(Oto oto, string file, string error)> InvalidOtos { get; set; } = new List<(Oto, string, string)>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();

        public bool HasIssues => InvalidOtos.Any() || Warnings.Any() || Errors.Any();

        public override string ToString() {
            var sb = new StringBuilder();
            sb.AppendLine($"Validation Report for: {VoicebankName}");
            sb.AppendLine($"Total Otos: {TotalOtos}");
            sb.AppendLine($"Valid Otos: {ValidOtos}");
            sb.AppendLine($"Invalid Otos: {InvalidOtos.Count}");
            
            if (Errors.Any()) {
                sb.AppendLine("\nErrors:");
                foreach (var error in Errors) {
                    sb.AppendLine($"  - {error}");
                }
            }

            if (InvalidOtos.Any()) {
                sb.AppendLine("\nInvalid Otos:");
                foreach (var (oto, file, error) in InvalidOtos.Take(10)) {
                    sb.AppendLine($"  - {oto.Alias} in {Path.GetFileName(file)}: {error}");
                }
                if (InvalidOtos.Count > 10) {
                    sb.AppendLine($"  ... and {InvalidOtos.Count - 10} more");
                }
            }

            if (Warnings.Any()) {
                sb.AppendLine("\nWarnings:");
                foreach (var warning in Warnings.Take(10)) {
                    sb.AppendLine($"  - {warning}");
                }
                if (Warnings.Count > 10) {
                    sb.AppendLine($"  ... and {Warnings.Count - 10} more");
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Statistics about oto entries in a voicebank.
    /// </summary>
    public class OtoStatistics {
        public int TotalOtos { get; set; }
        public int ValidOtos { get; set; }
        public int InvalidOtos { get; set; }
        public int OtoSets { get; set; }
        public int UniqueAliases { get; set; }
        public int UniqueWavFiles { get; set; }
        public double AverageOffset { get; set; }
        public double AverageConsonant { get; set; }
        public double AveragePreutter { get; set; }
        public double AverageOverlap { get; set; }

        public override string ToString() {
            var sb = new StringBuilder();
            sb.AppendLine("Oto Statistics:");
            sb.AppendLine($"  Total Otos: {TotalOtos}");
            sb.AppendLine($"  Valid: {ValidOtos}, Invalid: {InvalidOtos}");
            sb.AppendLine($"  Oto Sets: {OtoSets}");
            sb.AppendLine($"  Unique Aliases: {UniqueAliases}");
            sb.AppendLine($"  Unique Wav Files: {UniqueWavFiles}");
            sb.AppendLine($"  Average Offset: {AverageOffset:F2}ms");
            sb.AppendLine($"  Average Consonant: {AverageConsonant:F2}ms");
            sb.AppendLine($"  Average Preutter: {AveragePreutter:F2}ms");
            sb.AppendLine($"  Average Overlap: {AverageOverlap:F2}ms");
            return sb.ToString();
        }
    }
}
