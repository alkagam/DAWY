using System;

namespace DawEngine.Core
{
    public class PhaserProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        private float _sampleRate = 48000f;

        // Estructura interna para guardar la memoria de CADA etapa All-Pass
        private struct AllPassStage
        {
            // y[n-1] y x[n-1] en tu fórmula
            public float x_1;
            public float y_1;
        }

        // Definimos un Phaser de 6 etapas (una configuración clásica y rica)
        private readonly AllPassStage[] _stages = new AllPassStage[6];

        // Memoria para el Feedback global
        private float _feedbackStorage = 0f;

        // Parámetros
        private float _rate = 1.0f;       // Velocidad del LFO en Hz
        private float _depth = 0.8f;      // Profundidad de la modulación (0.0 a 1.0)
        private float _feedback = 0.5f;   // Realimentación (f) (0.0 a 0.9)
        private float _mix = 0.5f;        // 50% seco, 50% mojado

        // Reloj del LFO
        private float _phase = 0f;

        public PhaserProcessor(int sampleRate = 48000)
        {
            _sampleRate = sampleRate;
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Rate") _rate = Math.Max(0.1f, value);
            else if (name == "Depth") _depth = Math.Clamp(value, 0f, 1f);
            else if (name == "Feedback") _feedback = Math.Clamp(value, 0f, 0.95f);
            else if (name == "Mix") _mix = Math.Clamp(value, 0f, 1f);
        }

        public void Process(Span<float> buffer)
        {
            float phaseIncrement = 2f * MathF.PI * _rate / _sampleRate;

            for (int i = 0; i < buffer.Length; i++)
            {
                float dry = buffer[i]; // Señal original x[n]

                // 1. Calculamos el LFO (-1.0 a 1.0)
                float lfo = MathF.Sin(_phase);

                // 2. Mapeamos el LFO al coeficiente 'a' de tu fórmula (0.1 a 0.9)
                // Esto hace que la frecuencia central del Phaser se mueva
                float a = 0.5f + (lfo * 0.4f * _depth);

                // 3. Aplicamos Feedback global (f)
                float phaserInput = dry + (_feedbackStorage * _feedback);

                float currentSample = phaserInput;

                // 4. EL RETO: Procesamiento en CAScada (6 filtros en serie)
                for (int j = 0; j < _stages.Length; j++)
                {
                    float x_n = currentSample;

                    // Ecuación matemática de tu lista: y[n] = x[n] + a*y[n-1] - x[n-1]
                    float y_n = x_n + a * _stages[j].y_1 - _stages[j].x_1;

                    // Actualizamos la memoria (n-1) para esta etapa específica
                    _stages[j].x_1 = x_n;
                    _stages[j].y_1 = y_n;

                    // El resultado de esta etapa entra a la siguiente
                    currentSample = y_n;
                }

                // 5. Guardamos la salida para el siguiente ciclo de feedback
                _feedbackStorage = currentSample;

                // 6. Mezclamos: Señal original + Señal desfasada
                buffer[i] = dry * (1f - _mix) + currentSample * _mix;

                // 7. Avanzamos el reloj
                _phase += phaseIncrement;
                if (_phase >= 2f * MathF.PI) _phase -= 2f * MathF.PI;
            }
        }
    }
}