using System;

namespace DawEngine.Core
{
    public class CompressorProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // Parámetros de la fórmula
        private float _threshold = 0.5f; // "T": A partir de qué volumen empezamos a aplastar (0.0 a 1.0)
        private float _ratio = 4.0f;     // "R": Cuánto aplastamos (ej. 4:1)

        // Memoria del Envelope Follower (Bloque 8)
        private float _envelope = 0f;

        // La velocidad con la que el compresor reacciona (Alpha)
        // 0.99f es un suavizado alto para que no distorsione la onda
        private float _alpha = 0.99f;

        public void UpdateParameter(string name, float value)
        {
            if (name == "Threshold") _threshold = Math.Clamp(value, 0.001f, 1f);
            else if (name == "Ratio") _ratio = Math.Max(value, 1f);
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float x = buffer[i];

                // 1. Calculamos la envolvente (El volumen percibido en este instante)
                // env[n] = (1-a)*|x[n]| + a*env[n-1]
                _envelope = (1f - _alpha) * MathF.Abs(x) + _alpha * _envelope;

                float gainMultiplier = 1.0f;

                // 2. Aplicamos la lógica de tu fórmula del Bloque 1 a la ENVOLVENTE
                if (_envelope > _threshold)
                {
                    // Calculamos cuál debería ser el nivel de salida comprimido según tu ecuación
                    float targetLevel = _threshold + ((_envelope - _threshold) / _ratio);

                    // Derivamos un factor de ganancia (0.0 a 1.0) para aplicarlo a la onda original
                    gainMultiplier = targetLevel / _envelope;
                }

                // 3. Multiplicamos la muestra cruda por el factor de reducción
                buffer[i] = x * gainMultiplier;
            }
        }
    }
}