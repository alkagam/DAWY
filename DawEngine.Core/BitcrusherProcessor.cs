using System;

namespace DawEngine.Core
{
    public class BitcrusherProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // Parámetros de destrucción
        private float _bitDepth = 8f;       // 'b' en tu fórmula (Desde 16-bits limpios hasta 2-bits de ruido puro)
        private int _downsampleFactor = 1;  // 'k' en tu fórmula (1 = audio normal, 20 = Nintendo de 8-bits)

        // Memoria para el Sample & Hold
        private float _heldSample = 0f;     // x[n_0]: El último valor retenido
        private int _sampleCounter = 0;     // Reloj interno para el n mod k

        public void UpdateParameter(string name, float value)
        {
            if (name == "BitDepth") _bitDepth = Math.Clamp(value, 2f, 16f);
            else if (name == "Downsample") _downsampleFactor = (int)Math.Max(1f, value);
        }

        public void Process(Span<float> buffer)
        {
            // Pre-calculamos los "escalones" digitales: 2^b
            float steps = MathF.Pow(2f, _bitDepth);

            for (int i = 0; i < buffer.Length; i++)
            {
                float x_n = buffer[i];

                // --- 1. DOWNSAMPLING & SAMPLE AND HOLD ---
                // Tu condición matemática: Evaluamos si es momento de tomar una nueva muestra
                _sampleCounter++;
                if (_sampleCounter >= _downsampleFactor)
                {
                    _heldSample = x_n; // Retenemos la nueva muestra (x[n_0])
                    _sampleCounter = 0; // Reiniciamos el reloj (n mod k)
                }

                float x_held = _heldSample;

                // --- 2. BITCRUSHER (Reducción de Resolución) ---
                // Tu ecuación matemática exacta: y = round(x * 2^b) / 2^b
                // Esto fuerza a la onda suave a convertirse en escaleras cuadradas
                float y_n = MathF.Round(x_held * steps) / steps;

                buffer[i] = y_n;
            }
        }
    }
}