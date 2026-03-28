using System;
using System.Collections.Generic;

namespace DawEngine.Core.Ai
{
    public class AiCommandProcessor
    {
        private readonly EffectChain _chain;

        // Callback para actualizar LEDs y knobs en la UI
        // Firma: (effectName, paramName, value, enable?)
        public Action<string, string, float, bool?>? OnEffectChanged { get; set; }

        public AiCommandProcessor(EffectChain chain)
        {
            _chain = chain;
        }

        // Ejecuta todos los cambios de una AiResponse
        public string Execute(AiResponse response)
        {
            if (response.Changes == null || response.Changes.Count == 0)
                return response.Message;

            int applied = 0;

            foreach (var change in response.Changes)
            {
                var processor = FindProcessor(change.EffectName);
                if (processor == null) continue;

                // Cambiar estado (encender/apagar)
                if (change.Enable.HasValue)
                    processor.IsEnabled = change.Enable.Value;

                // Cambiar parámetro
                if (!string.IsNullOrEmpty(change.ParameterName))
                    processor.UpdateParameter(change.ParameterName, change.Value);

                // Notificar a la UI para que mueva LEDs y knobs
                OnEffectChanged?.Invoke(
                    change.EffectName,
                    change.ParameterName ?? "",
                    change.Value,
                    change.Enable);

                applied++;
            }

            return response.Message;
        }

        private IAudioProcessor? FindProcessor(string effectName)
        {
            if (string.IsNullOrEmpty(effectName)) return null;

            foreach (var p in _chain.GetProcessors())
            {
                // Comparamos por nombre de clase (ej: "HardClipperProcessor" contiene "HARD CLIP")
                string typeName = p.GetType().Name.ToUpper().Replace("PROCESSOR", "").Replace("FILTER", "").Trim();

                // Mapa explícito para nombres que no coinciden directamente
                bool match = effectName.ToUpper() switch
                {
                    "GAIN"         => typeName.Contains("GAIN"),
                    "GATE"         => typeName.Contains("NOISE") || typeName.Contains("GATE"),
                    "HARD CLIP"    => typeName.Contains("HARDCLIPPER") || typeName.Contains("HARD"),
                    "OVERDRIVE"    => typeName.Contains("OVERDRIVE"),
                    "FUZZ"         => typeName.Contains("FUZZ"),
                    "COMPRESSOR"   => typeName.Contains("COMPRESSOR"),
                    "LP FILTER"    => typeName.Contains("LOWPASS") || typeName.Contains("LOW"),
                    "HP FILTER"    => typeName.Contains("HIGHPASS") || typeName.Contains("HIGH"),
                    "BAND PASS"    => typeName.Contains("BANDPASS") || typeName.Contains("BAND"),
                    "WAH"          => typeName.Contains("WAH"),
                    "BITCRUSHER"   => typeName.Contains("BITCRUSHER") || typeName.Contains("BIT"),
                    "RING MOD"     => typeName.Contains("RING"),
                    "PITCH SHIFT"  => typeName.Contains("PITCH"),
                    "TREMOLO"      => typeName.Contains("TREMOLO"),
                    "CHORUS"       => typeName.Contains("CHORUS"),
                    "PHASER"       => typeName.Contains("PHASER"),
                    "DELAY"        => typeName.Contains("DELAY"),
                    "REVERB"       => typeName.Contains("SCHROEDER") || typeName.Contains("REVERB"),
                    "PAN"          => typeName.Contains("PAN"),
                    _              => typeName.Contains(effectName.ToUpper().Replace(" ", "")),
                };

                if (match) return p;
            }

            return null;
        }
    }
}
