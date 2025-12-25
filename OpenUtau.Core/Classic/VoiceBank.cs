using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Classic {
    /// <summary>
    /// Represents a voicebank with support for oto.ini management.
    /// </summary>
    public class Voicebank {
        public string BasePath;
        public string File;
        public string Name;
        public Dictionary<string, string> LocalizedNames = new Dictionary<string, string>();
        public string Image;
        public string Portrait;
        public float PortraitOpacity;
        public int PortraitHeight;
        public string Author;
        public string Voice;
        public string Web;
        public string Version;
        public string Sample;
        public string OtherInfo;
        public string DefaultPhonemizer;
        public Encoding TextFileEncoding;
        public USingerType SingerType = USingerType.Classic;
        public List<OtoSet> OtoSets = new List<OtoSet>();
        public List<Subbank> Subbanks = new List<Subbank>();
        public string Id;
        public bool? UseFilenameAsAlias = null;

        /// <summary>
        /// Reloads the voicebank data from disk.
        /// </summary>
        public void Reload() {
            Name = null;
            LocalizedNames.Clear();
            Image = null;
            Portrait = null;
            PortraitOpacity = 0;
            PortraitHeight = 0;
            Author = null;
            Voice = null;
            Web = null;
            Version = null;
            Sample = null;
            OtherInfo = null;
            TextFileEncoding = null;
            SingerType = USingerType.Classic;
            OtoSets.Clear();
            Subbanks.Clear();
            Id = null;
            UseFilenameAsAlias = null;
            VoicebankLoader.LoadVoicebank(this);
        }

        /// <summary>
        /// Gets the total number of oto entries across all sets.
        /// </summary>
        public int TotalOtoCount => OtoSets.Sum(set => set.Otos.Count);

        /// <summary>
        /// Gets the number of valid oto entries.
        /// </summary>
        public int ValidOtoCount => OtoSets.Sum(set => set.Otos.Count(o => o.IsValid));

        /// <summary>
        /// Gets the number of invalid oto entries.
        /// </summary>
        public int InvalidOtoCount => OtoSets.Sum(set => set.Otos.Count(o => !o.IsValid));

        /// <summary>
        /// Checks if the voicebank has any oto entries.
        /// </summary>
        public bool HasOtos => OtoSets.Any() && OtoSets.Any(set => set.Otos.Any());

        /// <summary>
        /// Gets all oto entries from all sets.
        /// </summary>
        public IEnumerable<Oto> GetAllOtos() {
            return OtoSets.SelectMany(set => set.Otos);
        }

        /// <summary>
        /// Finds an oto by alias.
        /// </summary>
        public Oto FindOto(string alias) {
            if (string.IsNullOrEmpty(alias)) {
                return null;
            }
            return GetAllOtos().FirstOrDefault(o => o.Alias == alias);
        }

        /// <summary>
        /// Gets all invalid otos with their error messages.
        /// </summary>
        public IEnumerable<(Oto oto, string error)> GetInvalidOtos() {
            return GetAllOtos()
                .Where(o => !o.IsValid)
                .Select(o => (o, o.Error));
        }

        public override string ToString() {
            return Name;
        }
    }

    /// <summary>
    /// Represents a set of oto entries from a single oto.ini file.
    /// </summary>
    public class OtoSet {
        public string File;
        public string Name;
        public List<Oto> Otos = new List<Oto>();

        /// <summary>
        /// Gets the number of valid otos in this set.
        /// </summary>
        public int ValidCount => Otos.Count(o => o.IsValid);

        /// <summary>
        /// Gets the number of invalid otos in this set.
        /// </summary>
        public int InvalidCount => Otos.Count(o => !o.IsValid);

        public override string ToString() {
            return Name;
        }
    }

    /// <summary>
    /// Represents a single oto entry with timing and phonetic information.
    /// </summary>
    public class Oto {
        public string Alias;
        public string Phonetic;
        public string Wav;

        // Wav layout:
        // |-offset-|-consonant-(fixed)-|-stretched-|-cutoff-|
        // |        |-preutter-----|
        // |        |-overlap-|
        // Note position:
        // ... ----------prev-note-|-this-note-- ...
        // Phoneme overlap:
        // ... --prev-phoneme-\
        //          /-this-phoneme-------------- ...

        // Length of left offset.
        public double Offset;
        // Length of unstretched consonant in wav, AKA fixed.
        public double Consonant;
        // Length of right cutoff, AKA end blank. If negative, length of (consonant + stretched). 
        public double Cutoff;
        // Length before note start, usually within consonant range.
        public double Preutter;
        // Length overlap with previous note, usually within consonant range.
        public double Overlap;

        public bool IsValid;
        public string Error = string.Empty;
        public FileTrace? FileTrace;

        /// <summary>
        /// Validates the oto timing parameters.
        /// </summary>
        public bool ValidateTiming(out string error) {
            error = string.Empty;

            if (Offset < 0) {
                error = "Offset cannot be negative";
                return false;
            }

            if (Consonant < 0) {
                error = "Consonant cannot be negative";
                return false;
            }

            if (Preutter < 0) {
                error = "Preutter cannot be negative";
                return false;
            }

            if (Overlap < 0) {
                error = "Overlap cannot be negative";
                return false;
            }

            return true;
        }

        public override string ToString() {
            return Alias;
        }
    }
}
