using System;

namespace DawEngine.Core
{
    public class SampleAndHoldProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        private float _sampleRate = 48000f;

        // Parámetro: Velocidad del reloj que toma las muestras (en Hz)
        private float _rate = 8.0f;

        // Reloj del LFO interno
        private float _phase = 0f;

        // La variable n_0 de tu fórmula (la muestra retenida)
        private float _n0 = 0f;

        public SampleAndHoldProcessor(int sampleRate = 48000)
        {
            _sampleRate = sampleRate;
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Rate") _rate = Math.Max(0.1f, value);
        }

        public void Process(Span<float> buffer)
        {
            float phaseIncrement = 2f * MathF.PI * _rate / _sampleRate;

            for (int i = 0; i < buffer.Length; i++)
            {
                float x_n = buffer[i];

                // Verificamos si el LFO cruzó el umbral (de negativo a positivo) para tomar una nueva foto
                float currentLfo = MathF.Sin(_phase);
                float nextLfo = MathF.Sin(_phase + phaseIncrement);

                if (currentLfo <= 0f && nextLfo > 0f)
                {
                    // ¡Tomamos la muestra! Actualizamos n_0
                    _n0 = x_n;
                }

                // Tu fórmula matemática: Salida = la última muestra retenida
                buffer[i] = _n0;

                // Avanzamos el reloj trigonométrico
                _phase += phaseIncrement;
                if (_phase >= 2f * MathF.PI) _phase -= 2f * MathF.PI;
            }
        }
    }
}