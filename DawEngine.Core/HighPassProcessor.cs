using System;

namespace DawEngine.Core
{
    public class HighPassProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        private float _sampleRate = 48000f;

        // Memoria del filtro (n-1)
        private float _x1 = 0f; // x[n-1]
        private float _y1 = 0f; // y[n-1]

        // Coeficiente matemático
        private float _alpha = 0f;
        private float _cutoffHz = 100f; // Cortamos las frecuencias inútiles debajo de 100Hz por defecto

        public HighPassProcessor(int sampleRate = 48000)
        {
            _sampleRate = sampleRate;
            CalculateAlpha();
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Cutoff")
            {
                _cutoffHz = Math.Clamp(value, 20f, 20000f);
                CalculateAlpha();
            }
        }

        private void CalculateAlpha()
        {
            // Mapeo de frecuencia a coeficiente Alpha para un filtro IIR de 1er orden
            float dt = 1f / _sampleRate;
            float rc = 1f / (2f * MathF.PI * _cutoffHz);
            _alpha = rc / (rc + dt);
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float x_n = buffer[i];

                // Tu ecuación matemática exacta: y[n] = a * (y[n-1] + x[n] - x[n-1])
                float y_n = _alpha * (_y1 + x_n - _x1);

                // Actualizamos la memoria para el siguiente ciclo
                _x1 = x_n;
                _y1 = y_n;

                buffer[i] = y_n;
            }
        }
    }
}