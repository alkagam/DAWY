using System;

namespace DawEngine.Core
{
    public class NoiseGateProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // "T" en tu fórmula. 
        // En punto flotante, 0.0 es silencio y 1.0 es el máximo volumen digital.
        // El ruido de fondo de una guitarra suele estar entre 0.001 y 0.02.
        private float _threshold = 0.01f;

        public void UpdateParameter(string name, float value)
        {
            if (name == "Threshold")
            {
                _threshold = value;
            }
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float x = buffer[i];

                // Aplicamos la condición matemática:
                // Si el valor absoluto de la muestra supera el umbral, pasa.
                // Si no, se multiplica por cero (se silencia).
                if (MathF.Abs(x) > _threshold)
                {
                    buffer[i] = x;
                }
                else
                {
                    buffer[i] = 0f;
                }
            }
        }
    }
}