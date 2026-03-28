using System.Collections.Generic;
using System.Linq;

namespace DawEngine.Core.Ai
{
    public static class InstrumentProfileService
    {
        // ── Registro completo de perfiles ─────────────────────────────────────
        // Clave externa: nombre del instrumento (igual que en ModeSelectWindow)
        // Clave interna: nombre del preset
        private static readonly Dictionary<string, Dictionary<string, InstrumentProfile>> _profiles = new()
        {
            ["Guitarra eléctrica"] = GuitarProfiles(),
            ["Bajo eléctrico"]     = BassProfiles(),
            ["Voz / Micrófono"]    = VoiceProfiles(),
            ["Teclado / Synth"]    = KeysProfiles(),
            ["Batería electrónica"]= DrumsProfiles(),
        };

        // Frases clave que mapean a presets — Draw.ia las busca en el prompt
        private static readonly List<(string[] Keywords, string Instrument, string Preset)> _keywordMap = new()
        {
            // Guitarra
            (new[]{"metallica","metal","pesado","heavy","agresivo","distorsion","distorsión"}, "Guitarra eléctrica", "Metal"),
            (new[]{"clean","limpio","kristal","cristal","funk","poppy"},                       "Guitarra eléctrica", "Clean"),
            (new[]{"crunch","rock","overdrive","classic rock","clasico"},                      "Guitarra eléctrica", "Crunch"),
            (new[]{"blues","bb king","srv","stevie","pentatonica"},                            "Guitarra eléctrica", "Blues"),
            (new[]{"shoegaze","my bloody","ambient","dreamy","reverb","reverberado"},          "Guitarra eléctrica", "Shoegaze"),
            (new[]{"jazz","suave","warm","calido","cálido"},                                   "Guitarra eléctrica", "Jazz"),

            // Bajo
            (new[]{"slap","flea","peppers","rhcp","funk bass"},                                "Bajo eléctrico", "Slap"),
            (new[]{"distorsion bajo","muse","bass fuzz","doom","stoner"},                      "Bajo eléctrico", "Distorsionado"),
            (new[]{"clean bass","limpio","natural","jazz bass"},                               "Bajo eléctrico", "Limpio"),
            (new[]{"funk","groovy","groove"},                                                  "Bajo eléctrico", "Funk"),

            // Voz
            (new[]{"radio","lo-fi","lofi","telefono","vintage"},                               "Voz / Micrófono", "Radio"),
            (new[]{"pop","produccion","brillante","bright","autotune"},                        "Voz / Micrófono", "Pop"),
            (new[]{"natural","limpia","clean voice","seca"},                                   "Voz / Micrófono", "Natural"),
            (new[]{"coral","church","catedral","cathedral","hall"},                            "Voz / Micrófono", "Cathedral"),

            // Teclado
            (new[]{"pad","ambient","flotar","atmospheric","cinematic"},                        "Teclado / Synth", "Pad Ambient"),
            (new[]{"lead","synth lead","bright","agudo","punzante"},                           "Teclado / Synth", "Lead"),
            (new[]{"piano","natural piano","acustico","acústico"},                             "Teclado / Synth", "Piano Natural"),
            (new[]{"retro","vintage synth","80s","ochenta","analog"},                          "Teclado / Synth", "Retro 80s"),

            // Batería
            (new[]{"punch","kick","fuerte","poderoso","rock drums"},                           "Batería electrónica", "Punch Rock"),
            (new[]{"hip hop","trap","electronic","electronica","lo-fi drums"},                 "Batería electrónica", "Electronic"),
            (new[]{"jazz drums","brushes","suave drums","natural drums"},                      "Batería electrónica", "Jazz"),
        };

        // ── API pública ───────────────────────────────────────────────────────

        // Obtener el perfil por defecto de un instrumento (primer preset)
        public static InstrumentProfile? GetDefault(string instrument)
        {
            if (!_profiles.TryGetValue(instrument, out var presets)) return null;
            return presets.Values.FirstOrDefault();
        }

        // Obtener un preset específico
        public static InstrumentProfile? Get(string instrument, string preset)
        {
            if (!_profiles.TryGetValue(instrument, out var presets)) return null;
            return presets.TryGetValue(preset, out var p) ? p : null;
        }

        // Buscar preset por prompt de lenguaje natural
        public static (InstrumentProfile? Profile, string? Instrument, string? Preset)
            FindByPrompt(string prompt, string? currentInstrument = null)
        {
            string lower = prompt.ToLower();

            foreach (var (keywords, instrument, preset) in _keywordMap)
            {
                // Si hay instrumento activo, priorizar sus presets
                if (currentInstrument != null && instrument != currentInstrument) continue;

                if (keywords.Any(k => lower.Contains(k)))
                {
                    var profile = Get(instrument, preset);
                    if (profile != null) return (profile, instrument, preset);
                }
            }

            // Segunda pasada: buscar en todos los instrumentos
            if (currentInstrument != null)
            {
                foreach (var (keywords, instrument, preset) in _keywordMap)
                {
                    if (keywords.Any(k => lower.Contains(k)))
                    {
                        var profile = Get(instrument, preset);
                        if (profile != null) return (profile, instrument, preset);
                    }
                }
            }

            return (null, null, null);
        }

        // Listar presets disponibles para un instrumento
        public static IEnumerable<string> GetPresetNames(string instrument)
        {
            if (!_profiles.TryGetValue(instrument, out var presets))
                return System.Array.Empty<string>();
            return presets.Keys;
        }

        // ══════════════════════════════════════════════════════════════════════
        // PERFILES DE GUITARRA ELÉCTRICA
        // ══════════════════════════════════════════════════════════════════════
        private static Dictionary<string, InstrumentProfile> GuitarProfiles() => new()
        {
            ["Clean"] = new InstrumentProfile
            {
                Name = "Guitarra eléctrica", PresetLabel = "Clean",
                Description = "Tono cristalino sin distorsión. Ideal para funk y arpeggios.",
                Effects = new()
                {
                    ["GAIN"]      = P("Gain", 1.2f),
                    ["GATE"]      = P("Threshold", 0.015f),
                    ["LP FILTER"] = P("Cutoff", 8000f),
                    ["HP FILTER"] = P("Cutoff", 80f),
                    ["REVERB"]    = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.5f }, new EffectSetting { ParameterName="Mix", Value=0.2f } },
                },
                DisabledEffects = new() { "HARD CLIP", "OVERDRIVE", "FUZZ", "BITCRUSHER", "CHORUS", "PHASER", "WAH", "RING MOD" },
            },

            ["Crunch"] = new InstrumentProfile
            {
                Name = "Guitarra eléctrica", PresetLabel = "Crunch",
                Description = "Overdrive suave. Rock clásico, riffs potentes.",
                Effects = new()
                {
                    ["GAIN"]      = P("Gain", 1.5f),
                    ["GATE"]      = P("Threshold", 0.02f),
                    ["OVERDRIVE"] = P("Gain", 4.0f),
                    ["LP FILTER"] = P("Cutoff", 6000f),
                    ["HP FILTER"] = P("Cutoff", 100f),
                    ["DELAY"]     = new(){ new EffectSetting { ParameterName="Time", Value=320f }, new EffectSetting { ParameterName="Feedback", Value=0.25f }, new EffectSetting { ParameterName="Mix", Value=0.2f } },
                },
                DisabledEffects = new() { "HARD CLIP", "FUZZ", "CHORUS", "PHASER", "BITCRUSHER", "WAH", "RING MOD" },
            },

            ["Metal"] = new InstrumentProfile
            {
                Name = "Guitarra eléctrica", PresetLabel = "Metal",
                Description = "Distorsión agresiva estilo Metallica / Pantera.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 2.0f),
                    ["GATE"]       = P("Threshold", 0.05f),
                    ["HARD CLIP"]  = new(){ new EffectSetting { ParameterName="Drive", Value=16f }, new EffectSetting { ParameterName="Threshold", Value=0.35f } },
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.3f }, new EffectSetting { ParameterName="Ratio", Value=8f } },
                    ["HP FILTER"]  = P("Cutoff", 120f),
                    ["LP FILTER"]  = P("Cutoff", 5000f),
                    ["REVERB"]     = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.6f }, new EffectSetting { ParameterName="Mix", Value=0.15f } },
                },
                DisabledEffects = new() { "OVERDRIVE", "FUZZ", "CHORUS", "PHASER", "BITCRUSHER", "WAH", "RING MOD", "DELAY" },
            },

            ["Blues"] = new InstrumentProfile
            {
                Name = "Guitarra eléctrica", PresetLabel = "Blues",
                Description = "Tono cálido con overdrive suave y reverb.",
                Effects = new()
                {
                    ["GAIN"]      = P("Gain", 1.3f),
                    ["GATE"]      = P("Threshold", 0.01f),
                    ["OVERDRIVE"] = P("Gain", 2.5f),
                    ["LP FILTER"] = P("Cutoff", 4500f),
                    ["REVERB"]    = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.65f }, new EffectSetting { ParameterName="Mix", Value=0.3f } },
                    ["DELAY"]     = new(){ new EffectSetting { ParameterName="Time", Value=400f }, new EffectSetting { ParameterName="Feedback", Value=0.2f }, new EffectSetting { ParameterName="Mix", Value=0.15f } },
                },
                DisabledEffects = new() { "HARD CLIP", "FUZZ", "COMPRESSOR", "CHORUS", "PHASER", "BITCRUSHER", "WAH", "RING MOD" },
            },

            ["Shoegaze"] = new InstrumentProfile
            {
                Name = "Guitarra eléctrica", PresetLabel = "Shoegaze",
                Description = "Muro de sonido ambient con chorus y reverb profundo.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.8f),
                    ["HARD CLIP"]  = new(){ new EffectSetting { ParameterName="Drive", Value=8f }, new EffectSetting { ParameterName="Threshold", Value=0.6f } },
                    ["CHORUS"]     = new(){ new EffectSetting { ParameterName="Rate", Value=0.8f }, new EffectSetting { ParameterName="Depth", Value=12f } },
                    ["REVERB"]     = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.95f }, new EffectSetting { ParameterName="Mix", Value=0.6f } },
                    ["LP FILTER"]  = P("Cutoff", 3500f),
                    ["DELAY"]      = new(){ new EffectSetting { ParameterName="Time", Value=600f }, new EffectSetting { ParameterName="Feedback", Value=0.45f }, new EffectSetting { ParameterName="Mix", Value=0.35f } },
                },
                DisabledEffects = new() { "OVERDRIVE", "FUZZ", "COMPRESSOR", "HP FILTER", "PHASER", "BITCRUSHER", "WAH", "RING MOD" },
            },

            ["Jazz"] = new InstrumentProfile
            {
                Name = "Guitarra eléctrica", PresetLabel = "Jazz",
                Description = "Tono oscuro y suave. Sin distorsión, mucho cuerpo.",
                Effects = new()
                {
                    ["GAIN"]      = P("Gain", 1.0f),
                    ["LP FILTER"] = P("Cutoff", 2800f),
                    ["HP FILTER"] = P("Cutoff", 60f),
                    ["REVERB"]    = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.4f }, new EffectSetting { ParameterName="Mix", Value=0.25f } },
                },
                DisabledEffects = new() { "GATE", "HARD CLIP", "OVERDRIVE", "FUZZ", "COMPRESSOR", "CHORUS", "PHASER", "DELAY", "BITCRUSHER", "WAH", "RING MOD" },
            },
        };

        // ══════════════════════════════════════════════════════════════════════
        // PERFILES DE BAJO ELÉCTRICO
        // ══════════════════════════════════════════════════════════════════════
        private static Dictionary<string, InstrumentProfile> BassProfiles() => new()
        {
            ["Limpio"] = new InstrumentProfile
            {
                Name = "Bajo eléctrico", PresetLabel = "Limpio",
                Description = "Bajo natural con compresión suave.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.3f),
                    ["GATE"]       = P("Threshold", 0.01f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.4f }, new EffectSetting { ParameterName="Ratio", Value=3f } },
                    ["HP FILTER"]  = P("Cutoff", 40f),
                    ["LP FILTER"]  = P("Cutoff", 5000f),
                },
                DisabledEffects = new() { "HARD CLIP", "OVERDRIVE", "FUZZ", "CHORUS", "PHASER", "DELAY", "REVERB", "BITCRUSHER", "WAH", "RING MOD" },
            },

            ["Slap"] = new InstrumentProfile
            {
                Name = "Bajo eléctrico", PresetLabel = "Slap",
                Description = "Ataque percusivo estilo RHCP / Flea.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.6f),
                    ["GATE"]       = P("Threshold", 0.025f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.25f }, new EffectSetting { ParameterName="Ratio", Value=6f } },
                    ["HP FILTER"]  = P("Cutoff", 60f),
                    ["LP FILTER"]  = P("Cutoff", 8000f),
                    ["BAND PASS"]  = P("Center", 1200f),
                },
                DisabledEffects = new() { "HARD CLIP", "OVERDRIVE", "FUZZ", "CHORUS", "PHASER", "DELAY", "REVERB", "BITCRUSHER", "WAH", "RING MOD" },
            },

            ["Distorsionado"] = new InstrumentProfile
            {
                Name = "Bajo eléctrico", PresetLabel = "Distorsionado",
                Description = "Fuzz pesado. Muse / doom / stoner.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.8f),
                    ["GATE"]       = P("Threshold", 0.04f),
                    ["FUZZ"]       = P("Gain", 18f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.3f }, new EffectSetting { ParameterName="Ratio", Value=5f } },
                    ["HP FILTER"]  = P("Cutoff", 50f),
                    ["LP FILTER"]  = P("Cutoff", 4000f),
                },
                DisabledEffects = new() { "HARD CLIP", "OVERDRIVE", "CHORUS", "PHASER", "DELAY", "REVERB", "BITCRUSHER", "WAH", "RING MOD" },
            },

            ["Funk"] = new InstrumentProfile
            {
                Name = "Bajo eléctrico", PresetLabel = "Funk",
                Description = "Groovy con wah automático y compresión fuerte.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.4f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.2f }, new EffectSetting { ParameterName="Ratio", Value=5f } },
                    ["WAH"]        = new(){ new EffectSetting { ParameterName="Rate", Value=3.5f }, new EffectSetting { ParameterName="Q", Value=0.15f } },
                    ["HP FILTER"]  = P("Cutoff", 55f),
                    ["LP FILTER"]  = P("Cutoff", 6000f),
                },
                DisabledEffects = new() { "HARD CLIP", "OVERDRIVE", "FUZZ", "CHORUS", "PHASER", "DELAY", "REVERB", "BITCRUSHER", "RING MOD" },
            },
        };

        // ══════════════════════════════════════════════════════════════════════
        // PERFILES DE VOZ / MICRÓFONO
        // ══════════════════════════════════════════════════════════════════════
        private static Dictionary<string, InstrumentProfile> VoiceProfiles() => new()
        {
            ["Natural"] = new InstrumentProfile
            {
                Name = "Voz / Micrófono", PresetLabel = "Natural",
                Description = "Voz limpia con procesamiento mínimo.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.4f),
                    ["HP FILTER"]  = P("Cutoff", 120f),
                    ["GATE"]       = P("Threshold", 0.012f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.45f }, new EffectSetting { ParameterName="Ratio", Value=3f } },
                    ["REVERB"]     = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.35f }, new EffectSetting { ParameterName="Mix", Value=0.15f } },
                },
                DisabledEffects = new() { "HARD CLIP", "OVERDRIVE", "FUZZ", "CHORUS", "PHASER", "DELAY", "BITCRUSHER", "WAH", "RING MOD", "PITCH SHIFT" },
            },

            ["Radio"] = new InstrumentProfile
            {
                Name = "Voz / Micrófono", PresetLabel = "Radio",
                Description = "Efecto de radio / teléfono retro. Lo-fi.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 2.0f),
                    ["HP FILTER"]  = P("Cutoff", 400f),
                    ["LP FILTER"]  = P("Cutoff", 3200f),
                    ["GATE"]       = P("Threshold", 0.02f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.2f }, new EffectSetting { ParameterName="Ratio", Value=8f } },
                    ["BITCRUSHER"] = new(){ new EffectSetting { ParameterName="BitDepth", Value=10f }, new EffectSetting { ParameterName="Downsample", Value=2f } },
                },
                DisabledEffects = new() { "HARD CLIP", "OVERDRIVE", "FUZZ", "CHORUS", "PHASER", "DELAY", "REVERB", "WAH", "RING MOD", "PITCH SHIFT" },
            },

            ["Pop"] = new InstrumentProfile
            {
                Name = "Voz / Micrófono", PresetLabel = "Pop",
                Description = "Producción pop brillante. Compresor fuerte y reverb de sala.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.6f),
                    ["HP FILTER"]  = P("Cutoff", 100f),
                    ["GATE"]       = P("Threshold", 0.015f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.3f }, new EffectSetting { ParameterName="Ratio", Value=5f } },
                    ["LP FILTER"]  = P("Cutoff", 12000f),
                    ["REVERB"]     = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.55f }, new EffectSetting { ParameterName="Mix", Value=0.28f } },
                    ["DELAY"]      = new(){ new EffectSetting { ParameterName="Time", Value=180f }, new EffectSetting { ParameterName="Feedback", Value=0.15f }, new EffectSetting { ParameterName="Mix", Value=0.12f } },
                },
                DisabledEffects = new() { "HARD CLIP", "OVERDRIVE", "FUZZ", "CHORUS", "PHASER", "BITCRUSHER", "WAH", "RING MOD", "PITCH SHIFT" },
            },

            ["Cathedral"] = new InstrumentProfile
            {
                Name = "Voz / Micrófono", PresetLabel = "Cathedral",
                Description = "Reverb épico de catedral. Coros y voces corales.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.3f),
                    ["HP FILTER"]  = P("Cutoff", 100f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.5f }, new EffectSetting { ParameterName="Ratio", Value=2.5f } },
                    ["REVERB"]     = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.97f }, new EffectSetting { ParameterName="Mix", Value=0.55f } },
                    ["CHORUS"]     = new(){ new EffectSetting { ParameterName="Rate", Value=0.5f }, new EffectSetting { ParameterName="Depth", Value=4f } },
                },
                DisabledEffects = new() { "GATE", "HARD CLIP", "OVERDRIVE", "FUZZ", "LP FILTER", "PHASER", "DELAY", "BITCRUSHER", "WAH", "RING MOD", "PITCH SHIFT" },
            },
        };

        // ══════════════════════════════════════════════════════════════════════
        // PERFILES DE TECLADO / SYNTH
        // ══════════════════════════════════════════════════════════════════════
        private static Dictionary<string, InstrumentProfile> KeysProfiles() => new()
        {
            ["Piano Natural"] = new InstrumentProfile
            {
                Name = "Teclado / Synth", PresetLabel = "Piano Natural",
                Description = "Emulación de piano acústico. Cálido y limpio.",
                Effects = new()
                {
                    ["GAIN"]   = P("Gain", 1.1f),
                    ["REVERB"] = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.5f }, new EffectSetting { ParameterName="Mix", Value=0.22f } },
                    ["LP FILTER"] = P("Cutoff", 9000f),
                },
                DisabledEffects = new() { "GATE", "HARD CLIP", "OVERDRIVE", "FUZZ", "COMPRESSOR", "HP FILTER", "BAND PASS", "CHORUS", "PHASER", "DELAY", "BITCRUSHER", "WAH", "RING MOD", "PITCH SHIFT", "TREMOLO" },
            },

            ["Pad Ambient"] = new InstrumentProfile
            {
                Name = "Teclado / Synth", PresetLabel = "Pad Ambient",
                Description = "Pad suave y etéreo. Cinematic / ambient.",
                Effects = new()
                {
                    ["GAIN"]   = P("Gain", 1.0f),
                    ["CHORUS"] = new(){ new EffectSetting { ParameterName="Rate", Value=0.4f }, new EffectSetting { ParameterName="Depth", Value=8f } },
                    ["REVERB"] = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.92f }, new EffectSetting { ParameterName="Mix", Value=0.5f } },
                    ["LP FILTER"] = P("Cutoff", 5000f),
                    ["TREMOLO"]   = new(){ new EffectSetting { ParameterName="Rate", Value=0.8f }, new EffectSetting { ParameterName="Depth", Value=0.2f } },
                },
                DisabledEffects = new() { "GATE", "HARD CLIP", "OVERDRIVE", "FUZZ", "COMPRESSOR", "HP FILTER", "BAND PASS", "PHASER", "DELAY", "BITCRUSHER", "WAH", "RING MOD", "PITCH SHIFT" },
            },

            ["Lead"] = new InstrumentProfile
            {
                Name = "Teclado / Synth", PresetLabel = "Lead",
                Description = "Synth lead brillante y cortante.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.5f),
                    ["OVERDRIVE"]  = P("Gain", 2.5f),
                    ["LP FILTER"]  = P("Cutoff", 7000f),
                    ["PHASER"]     = new(){ new EffectSetting { ParameterName="Rate", Value=2f }, new EffectSetting { ParameterName="Feedback", Value=0.4f } },
                    ["DELAY"]      = new(){ new EffectSetting { ParameterName="Time", Value=250f }, new EffectSetting { ParameterName="Feedback", Value=0.3f }, new EffectSetting { ParameterName="Mix", Value=0.2f } },
                },
                DisabledEffects = new() { "GATE", "HARD CLIP", "FUZZ", "COMPRESSOR", "HP FILTER", "BAND PASS", "CHORUS", "REVERB", "BITCRUSHER", "WAH", "RING MOD", "PITCH SHIFT", "TREMOLO" },
            },

            ["Retro 80s"] = new InstrumentProfile
            {
                Name = "Teclado / Synth", PresetLabel = "Retro 80s",
                Description = "Sonido vintage con chorus y delay.",
                Effects = new()
                {
                    ["GAIN"]   = P("Gain", 1.3f),
                    ["CHORUS"] = new(){ new EffectSetting { ParameterName="Rate", Value=1.5f }, new EffectSetting { ParameterName="Depth", Value=10f } },
                    ["PHASER"] = new(){ new EffectSetting { ParameterName="Rate", Value=0.5f }, new EffectSetting { ParameterName="Feedback", Value=0.5f } },
                    ["DELAY"]  = new(){ new EffectSetting { ParameterName="Time", Value=375f }, new EffectSetting { ParameterName="Feedback", Value=0.4f }, new EffectSetting { ParameterName="Mix", Value=0.3f } },
                    ["REVERB"] = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.7f }, new EffectSetting { ParameterName="Mix", Value=0.3f } },
                },
                DisabledEffects = new() { "GATE", "HARD CLIP", "OVERDRIVE", "FUZZ", "COMPRESSOR", "HP FILTER", "LP FILTER", "BAND PASS", "BITCRUSHER", "WAH", "RING MOD", "PITCH SHIFT", "TREMOLO" },
            },
        };

        // ══════════════════════════════════════════════════════════════════════
        // PERFILES DE BATERÍA ELECTRÓNICA
        // ══════════════════════════════════════════════════════════════════════
        private static Dictionary<string, InstrumentProfile> DrumsProfiles() => new()
        {
            ["Punch Rock"] = new InstrumentProfile
            {
                Name = "Batería electrónica", PresetLabel = "Punch Rock",
                Description = "Kick y snare potentes. Rock / metal.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.6f),
                    ["GATE"]       = P("Threshold", 0.03f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.25f }, new EffectSetting { ParameterName="Ratio", Value=6f } },
                    ["HP FILTER"]  = P("Cutoff", 60f),
                    ["LP FILTER"]  = P("Cutoff", 14000f),
                    ["HARD CLIP"]  = new(){ new EffectSetting { ParameterName="Drive", Value=3f }, new EffectSetting { ParameterName="Threshold", Value=0.85f } },
                },
                DisabledEffects = new() { "OVERDRIVE", "FUZZ", "BAND PASS", "CHORUS", "PHASER", "DELAY", "REVERB", "BITCRUSHER", "WAH", "RING MOD", "PITCH SHIFT", "TREMOLO" },
            },

            ["Electronic"] = new InstrumentProfile
            {
                Name = "Batería electrónica", PresetLabel = "Electronic",
                Description = "Hi-hats crujientes y kick procesado. Trap / EDM.",
                Effects = new()
                {
                    ["GAIN"]       = P("Gain", 1.4f),
                    ["GATE"]       = P("Threshold", 0.02f),
                    ["COMPRESSOR"] = new(){ new EffectSetting { ParameterName="Threshold", Value=0.2f }, new EffectSetting { ParameterName="Ratio", Value=8f } },
                    ["BITCRUSHER"] = new(){ new EffectSetting { ParameterName="BitDepth", Value=12f }, new EffectSetting { ParameterName="Downsample", Value=1f } },
                    ["HP FILTER"]  = P("Cutoff", 80f),
                    ["REVERB"]     = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.4f }, new EffectSetting { ParameterName="Mix", Value=0.15f } },
                },
                DisabledEffects = new() { "HARD CLIP", "OVERDRIVE", "FUZZ", "LP FILTER", "BAND PASS", "CHORUS", "PHASER", "DELAY", "WAH", "RING MOD", "PITCH SHIFT", "TREMOLO" },
            },

            ["Jazz"] = new InstrumentProfile
            {
                Name = "Batería electrónica", PresetLabel = "Jazz",
                Description = "Batería suave y natural. Escobillas, ligereza.",
                Effects = new()
                {
                    ["GAIN"]   = P("Gain", 1.1f),
                    ["REVERB"] = new(){ new EffectSetting { ParameterName="RoomSize", Value=0.6f }, new EffectSetting { ParameterName="Mix", Value=0.25f } },
                    ["LP FILTER"] = P("Cutoff", 10000f),
                },
                DisabledEffects = new() { "GATE", "HARD CLIP", "OVERDRIVE", "FUZZ", "COMPRESSOR", "HP FILTER", "BAND PASS", "CHORUS", "PHASER", "DELAY", "BITCRUSHER", "WAH", "RING MOD", "PITCH SHIFT", "TREMOLO" },
            },
        };

        // ── Helper: crear lista de un solo ajuste ─────────────────────────────
        private static List<EffectSetting> P(string param, float value) =>
            new() { new EffectSetting { ParameterName = param, Value = value } };
    }
}
