using System;

namespace DawEngine.Core
{
    public class GainProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // 'g' en tu fórmula. 
        // 0.0 es silencio, 1.0 es volumen original, > 1.0 es amplificación pura.
        private float _gain = 1.0f;

        public void UpdateParameter(string name, float value)
        {
            if (name == "Gain")
            {
                _gain = Math.Max(0f, value);
            }
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                // Multiplicamos cada muestra por el multiplicador de ganancia
                buffer[i] = buffer[i] * _gain;
            }
        }
    }
}