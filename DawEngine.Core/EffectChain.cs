using System;
using System.Collections.Generic;

namespace DawEngine.Core
{
    public class EffectChain
    {
        // La lista privada que guarda tus pedales de efecto
        private readonly List<IAudioProcessor> _processors = new List<IAudioProcessor>();

        public void AddProcessor(IAudioProcessor processor)
        {
            _processors.Add(processor);
        }

        // El motor matemático que procesa el audio en tiempo real
        public void ProcessBlock(Span<float> buffer)
        {
            foreach (var processor in _processors)
            {
                if (processor.IsEnabled)
                {
                    processor.Process(buffer);
                }
            }
        }

        // --- EL MÉTODO FALTANTE ---
        // Este método le permite a la IA leer qué efectos están en el rack para poder controlarlos
        public IEnumerable<IAudioProcessor> GetProcessors()
        {
            return _processors;
        }
    }
}