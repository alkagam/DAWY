using System;
using NAudio.Wave;
using DawEngine.Core;

namespace DawEngine.UI
{
    public class DspEngine : ISampleProvider
    {
        public WaveFormat WaveFormat { get; }

        // Mutable — se puede cambiar la cadena sin reiniciar ASIO
        private EffectChain _chain;

        private readonly float[] _processingBuffer;
        private int _lastSampleCount;

        public DspEngine(int sampleRate, int outputChannels, EffectChain chain)
        {
            WaveFormat        = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, outputChannels);
            _chain            = chain;
            _processingBuffer = new float[8192];
        }

        // Permite cambiar el chain en caliente sin reiniciar el driver ASIO
        public void UpdateChain(EffectChain chain)
        {
            _chain = chain;
        }

        public void ProcessInput(float[] inputBuffer, int samples)
        {
            _lastSampleCount = samples;
            Array.Copy(inputBuffer, _processingBuffer, samples);
            Span<float> span = _processingBuffer.AsSpan(0, samples);
            _chain.ProcessBlock(span);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_lastSampleCount == 0)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            if (WaveFormat.Channels == 2)
            {
                int inputIndex = 0;
                for (int i = 0; i < count; i += 2)
                {
                    if (inputIndex < _lastSampleCount)
                    {
                        float sample = _processingBuffer[inputIndex++];
                        buffer[offset + i]     = sample;
                        buffer[offset + i + 1] = sample;
                    }
                    else
                    {
                        buffer[offset + i]     = 0f;
                        buffer[offset + i + 1] = 0f;
                    }
                }
            }
            else
            {
                int samplesToCopy = Math.Min(count, _lastSampleCount);
                Array.Copy(_processingBuffer, 0, buffer, offset, samplesToCopy);
                if (count > samplesToCopy)
                    Array.Clear(buffer, offset + samplesToCopy, count - samplesToCopy);
            }

            return count;
        }
    }
}
