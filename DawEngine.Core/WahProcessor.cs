using System;

namespace DawEngine.Core
{
    public class WahProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        private float _sampleRate = 48000f;

        // Memoria del filtro SVF (n-1)
        private float _lowPass = 0f;
        private float _bandPass = 0f;

        // Parámetros del Wah
        private float _rate = 2.0f;       // Velocidad del "pie" moviendo el pedal (Hz)
        private float _fMin = 300f;       // El talón del pedal (Graves)
        private float _fMax = 2500f;      // La punta del pedal (Agudos)
        private float _q = 0.2f;          // Resonancia (Mientras más bajo, más "chillón" es el Wah)

        // Reloj del LFO
        private float _phase = 0f;

        public WahProcessor(int sampleRate = 48000)
        {
            _sampleRate = sampleRate;
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Rate") _rate = Math.Max(0.1f, value);
            else if (name == "MinFreq") _fMin = Math.Clamp(value, 100f, 1000f);
            else if (name == "MaxFreq") _fMax = Math.Clamp(value, 1000f, 5000f);
        }

        public void Process(Span<float> buffer)
        {
            float phaseIncrement = 2f * MathF.PI * _rate / _sampleRate;

            for (int i = 0; i < buffer.Length; i++)
            {
                float x_n = buffer[i];

                // 1. Calculamos el LFO. Usamos el Seno pero lo normalizamos de 0.0 a 1.0 
                // para que represente el pedal de Wah yendo de abajo hacia arriba.
                float lfo = (MathF.Sin(_phase) + 1f) / 2f;

                // 2. Tu ecuación dinámica: f_c(t) = f_min + (f_max - f_min) * LFO(t)
                float fc = _fMin + (_fMax - _fMin) * lfo;

                // 3. Matemática del Filtro SVF (Ajuste de frecuencia para espacio digital)
                // f_coef = 2 * sin(pi * fc / fs)
                float fCoef = 2f * MathF.Sin(MathF.PI * fc / _sampleRate);

                // 4. Ecuaciones del SVF (Calculando las 3 bandas simultáneamente)
                float highPass = x_n - _lowPass - (_q * _bandPass);

                _bandPass += fCoef * highPass;
                _lowPass += fCoef * _bandPass;

                // 5. La salida de nuestro pedal es la banda central (El "Wah")
                buffer[i] = _bandPass;

                // 6. Avanzamos el reloj del pie virtual
                _phase += phaseIncrement;
                if (_phase >= 2f * MathF.PI) _phase -= 2f * MathF.PI;
            }
        }
    }
}