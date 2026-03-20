using System;

namespace DawEngine.Core
{
    public class DownsamplerProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // La 'k' en tu fórmula: Factor de reducción (1 = 48kHz, 2 = 24kHz, 4 = 12kHz, etc.)
        private int _k = 4;

        // La 'n' en tu fórmula: Nuestro contador de tiempo absoluto
        private int _n = 0;

        // Memoria para sostener el valor
        private float _heldValue = 0f;

        public void UpdateParameter(string name, float value)
        {
            if (name == "Factor")
            {
                _k = (int)Math.Max(1f, value);
            }
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float x = buffer[i];

                // Tu matemática exacta: Evaluamos el módulo (n mod k)
                if (_n % _k == 0)
                {
                    // Si el residuo es 0, actualizamos el valor
                    _heldValue = x;
                }

                // La salida es el valor retenido
                buffer[i] = _heldValue;

                // Avanzamos el reloj de tiempo 'n'
                _n++;

                // Pequeño truco para evitar desbordamiento de enteros a largo plazo
                if (_n >= 480000) _n = 0;
            }
        }
    }
}