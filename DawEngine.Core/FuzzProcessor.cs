using System;

namespace DawEngine.Core
{
    public class FuzzProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // Ganancia masiva para el sonido Fuzz (g)
        private float _gain = 20.0f;

        public void UpdateParameter(string name, float value)
        {
            if (name == "Gain") _gain = Math.Max(value, 1f);
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                // 1. Amplificamos la señal agresivamente
                float x_g = buffer[i] * _gain;

                // 2. Aplicamos la matemática del Fuzz (Rectificación): y = x * sign(x)
                // Usamos MathF.Abs(x_g) que es matemáticamente idéntico y más rápido.
                float y = MathF.Abs(x_g);

                // 3. Pequeña compensación de volumen (La rectificación sube la energía)
                buffer[i] = y * 0.7f;
            }
        }
    }
}