using System;

namespace DawEngine.Core
{
    public class SchroederReverbProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // --- SUB-MÓDULOS MATEMÁTICOS ---

        // 1. Filtro Comb de Schroeder: y[n] = x[n] + g * y[n-D]
        private class CombFilter
        {
            private readonly float[] _buffer;
            private int _index = 0;
            public float Feedback { get; set; } = 0.8f; // La 'g' de tu fórmula (Room Size)

            public CombFilter(int bufferSize) { _buffer = new float[bufferSize]; }

            public float Process(float input)
            {
                float delayed = _buffer[_index];
                // Tu ecuación: Salida = Entrada + g * Pasado
                float output = input + (delayed * Feedback);
                _buffer[_index] = output; // Guardamos para el futuro

                _index++;
                if (_index >= _buffer.Length) _index = 0;

                return delayed; // El Comb saca la señal retrasada
            }
        }

        // 2. Filtro All-Pass de Schroeder: y[n] = -g * x[n] + x[n-D] + g * y[n-D]
        private class AllPassFilter
        {
            private readonly float[] _buffer;
            private int _index = 0;
            private readonly float _g = 0.7f; // Ganancia fija estándar para All-Pass

            public AllPassFilter(int bufferSize) { _buffer = new float[bufferSize]; }

            public float Process(float input)
            {
                float delayed = _buffer[_index];

                // Matemática de All-Pass (Difusión de fase)
                float feedback = input + (delayed * _g);
                float output = -input * _g + delayed;

                _buffer[_index] = feedback;

                _index++;
                if (_index >= _buffer.Length) _index = 0;

                return output;
            }
        }

        // --- EL MOTOR PRINCIPAL DE LA REVERB ---

        // 4 Combs en paralelo
        private readonly CombFilter[] _combs;
        // 2 All-Pass en serie
        private readonly AllPassFilter[] _allPasses;

        private float _mix = 0.4f;      // Mezcla (0.0 a 1.0)
        private float _roomSize = 0.8f; // Ganancia 'g' de los combs (0.0 a 0.98)

        public SchroederReverbProcessor()
        {
            // Tiempos mágicos de Freeverb/Schroeder (Ajustados a números primos para 48kHz)
            // Esto evita que la reverb suene metálica (Resonancia modal)
            _combs = new CombFilter[]
            {
                new CombFilter(1687), // ~35 ms
                new CombFilter(1601), // ~33 ms
                new CombFilter(2053), // ~42 ms
                new CombFilter(2251)  // ~46 ms
            };

            _allPasses = new AllPassFilter[]
            {
                new AllPassFilter(347), // ~7 ms
                new AllPassFilter(113)  // ~2 ms
            };
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Mix") _mix = Math.Clamp(value, 0f, 1f);
            else if (name == "RoomSize")
            {
                _roomSize = Math.Clamp(value, 0.5f, 0.98f); // 0.98 es infinito (peligro de saturación)
                foreach (var comb in _combs) comb.Feedback = _roomSize;
            }
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float dry = buffer[i];
                float reverbSignal = 0f;

                // PASO 1: Los 4 Filtros Comb procesan la guitarra EN PARALELO y se suman
                foreach (var comb in _combs)
                {
                    reverbSignal += comb.Process(dry);
                }

                // Atenuamos un poco porque sumamos 4 señales (evitar clipping digital)
                reverbSignal *= 0.25f;

                // PASO 2: La señal sumada pasa por los 2 Filtros All-Pass EN SERIE
                foreach (var ap in _allPasses)
                {
                    reverbSignal = ap.Process(reverbSignal);
                }

                // PASO 3: Salida final (Dry/Wet)
                buffer[i] = dry * (1f - _mix) + reverbSignal * _mix;
            }
        }
    }
}