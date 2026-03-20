using System;

namespace DawEngine.Core
{
    public class OverdriveProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // La 'g' en tu fórmula. A mayor ganancia, más se aplasta la onda contra los límites.
        private float _gain = 2.0f;

        public void UpdateParameter(string name, float value)
        {
            if (name == "Gain")
            {
                _gain = value;
            }
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                // Aplicamos la función de transferencia: y = tanh(g * x)
                buffer[i] = MathF.Tanh(_gain * buffer[i]);
            }
        }
    }
}