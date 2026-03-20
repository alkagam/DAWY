using System;

namespace DawEngine.Core
{
    public interface IAudioProcessor
    {
        bool IsEnabled { get; set; }

        // El corazón del procesamiento DSP
        void Process(Span<float> buffer);

        // Para que el LLM o la GUI actualicen parámetros matemáticos
        void UpdateParameter(string name, float value);
    }
}