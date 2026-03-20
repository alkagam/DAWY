using System;
using NAudio.Wave;
using DawEngine.Core;

namespace DawEngine.UI
{
    public class DspEngine : ISampleProvider
    {
        public WaveFormat WaveFormat { get; }
        private readonly EffectChain _chain;

        // Memoria pre-asignada para evitar alocaciones y Garbage Collection en tiempo real
        private readonly float[] _processingBuffer;
        private int _lastSampleCount;

        public DspEngine(int sampleRate, int outputChannels, EffectChain chain)
        {
            // Forzamos el formato a 32-bit flotante (estándar de DSP matemático)
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, outputChannels);
            _chain = chain;

            // Un buffer de 8192 muestras es suficiente para cualquier tamaño de bloque ASIO
            _processingBuffer = new float[8192];
        }

        // 1. Recibe el audio crudo de la guitarra (Input)
        public void ProcessInput(float[] inputBuffer, int samples)
        {
            _lastSampleCount = samples;
            Array.Copy(inputBuffer, _processingBuffer, samples);

            // Mutación matemática in-place a través de la cadena de efectos
            Span<float> span = _processingBuffer.AsSpan(0, samples);
            _chain.ProcessBlock(span);
        }

        // 2. Entrega el audio procesado a los canales virtuales de la Maono (Output)
        public int Read(float[] buffer, int offset, int count)
        {
            // PARCHE DE MEMORIA: Si no hay señal aún, llenamos el buffer con ceros (silencio digital)
            // Esto evita que ASIO colapse intentando reproducir basura de memoria.
            if (_lastSampleCount == 0)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            // Ruteo Mono -> Estéreo
            if (WaveFormat.Channels == 2)
            {
                int inputIndex = 0;
                for (int i = 0; i < count; i += 2)
                {
                    if (inputIndex < _lastSampleCount)
                    {
                        float sample = _processingBuffer[inputIndex++];
                        buffer[offset + i] = sample;       // Canal L (Izquierdo)
                        buffer[offset + i + 1] = sample;   // Canal R (Derecho)
                    }
                    else
                    {
                        // Rellenamos el resto de las muestras con silencio 
                        // para evitar chasquidos (glitches) al final del bloque
                        buffer[offset + i] = 0f;
                        buffer[offset + i + 1] = 0f;
                    }
                }
            }
            else
            {
                // Ruteo Mono -> Mono
                int samplesToCopy = Math.Min(count, _lastSampleCount);
                Array.Copy(_processingBuffer, 0, buffer, offset, samplesToCopy);

                // Limpiamos el resto del buffer si el driver pide más de lo que tenemos
                if (count > samplesToCopy)
                {
                    Array.Clear(buffer, offset + samplesToCopy, count - samplesToCopy);
                }
            }

            return count; // Le devolvemos a ASIO exactamente la cantidad de muestras que pidió
        }
    }
}