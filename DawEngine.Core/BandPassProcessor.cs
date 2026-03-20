using System;

namespace DawEngine.Core
{
    public class BandPassProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // Reutilizamos las clases que ya programaste
        private readonly HighPassProcessor _highPass;
        private readonly LowPassFilter _lowPass; // Asumiendo que tu archivo se llama así

        public BandPassProcessor(int sampleRate = 48000)
        {
            _highPass = new HighPassProcessor(sampleRate);
            _lowPass = new LowPassFilter();

            // Configuramos un ancho de banda por defecto (ej. de 500Hz a 2000Hz)
            _highPass.UpdateParameter("Cutoff", 500f);
            _lowPass.UpdateParameter("Cutoff", 2000f);
        }

        public void UpdateParameter(string name, float value)
        {
            // 'Center' mueve todo el bloque de frecuencias juntas
            if (name == "Center")
            {
                float centerFreq = Math.Clamp(value, 100f, 10000f);
                // Mantenemos un ancho de banda fijo para que suene como un pedal real
                _highPass.UpdateParameter("Cutoff", centerFreq * 0.5f);
                _lowPass.UpdateParameter("Cutoff", centerFreq * 2.0f);
            }
        }

        public void Process(Span<float> buffer)
        {
            // Procesamos en serie: x[n] -> HighPass -> LowPass -> y[n]
            _highPass.Process(buffer);
            _lowPass.Process(buffer);
        }
    }
}