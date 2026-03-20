using System;

namespace DawEngine.Core
{
    public class PanProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // 'p' en tu fórmula. Por defecto al centro (0.5).
        private float _pan = 0.5f;

        public void UpdateParameter(string name, float value)
        {
            if (name == "Pan")
            {
                _pan = Math.Clamp(value, 0f, 1f);
            }
        }

        public void Process(Span<float> buffer)
        {
            // Procesamos de 2 en 2 porque ASIO nos da: [L, R, L, R, L, R...]
            for (int i = 0; i < buffer.Length - 1; i += 2)
            {
                float x_l = buffer[i];     // Canal Izquierdo (Índices pares: 0, 2, 4...)
                float x_r = buffer[i + 1]; // Canal Derecho (Índices impares: 1, 3, 5...)

                // Aplicamos tu ecuación lineal
                // Multiplicamos por 2f al final para compensar la caída de volumen. 
                // Si p = 0.5 (Centro), 1 - 0.5 = 0.5. Si no compensamos, el centro suena a la mitad.
                float l_out = x_l * (1f - _pan) * 2f;
                float r_out = x_r * _pan * 2f;

                buffer[i] = l_out;
                buffer[i + 1] = r_out;
            }
        }
    }
}