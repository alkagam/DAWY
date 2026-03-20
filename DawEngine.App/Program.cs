using System;
using System.Threading;
using NAudio.Wave;
using DawEngine.Core;

namespace DawEngine.App
{
    class Program
    {
        private static float[]? _audioBuffer;
        private static EffectChain? _chain;
        private static DspEngine? _dspEngine;

        static void Main(string[] args)
        {
            Console.WriteLine("Preparando hilo STA para el motor de audio...");

            Thread audioThread = new Thread(RunAudioEngine);
#pragma warning disable CA1416
            audioThread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
            audioThread.Start();
            audioThread.Join();
        }

        private static void RunAudioEngine()
        {
            _chain = new EffectChain();
            _chain.AddProcessor(new HardClipper());

            // 1. Sincronizamos el DSP al reloj de hardware de la Maono (48kHz)
            _dspEngine = new DspEngine(48000, 2, _chain);

            var asioDrivers = AsioOut.GetDriverNames();
            if (asioDrivers.Length == 0)
            {
                Console.WriteLine("Error: No se encontraron drivers ASIO.");
                return;
            }

            // 2. Buscamos el driver nativo de Maono
            string driverName = asioDrivers[0];
            foreach (var driver in asioDrivers)
            {
                if (driver.ToLower().Contains("maono") || driver.ToLower().Contains("ps22"))
                {
                    driverName = driver;
                    break;
                }
            }

            Console.WriteLine($"Iniciando motor con driver: {driverName}");

            try
            {
                using var asioOut = new AsioOut(driverName);

                Console.WriteLine("\n--- ESCÁNER DE ENTRADAS ASIO ---");
                Console.WriteLine($"Total de entradas físicas detectadas: {asioOut.DriverInputChannelCount}");
                for (int i = 0; i < asioOut.DriverInputChannelCount; i++)
                {
                    Console.WriteLine($"[Input {i}]: {asioOut.AsioInputChannelName(i)}");
                }

                // --- NUEVO: ESCÁNER DE SALIDAS ASIO ---
                Console.WriteLine("\n--- ESCÁNER DE SALIDAS ASIO ---");
                Console.WriteLine($"Total de salidas físicas detectadas: {asioOut.DriverOutputChannelCount}");
                for (int i = 0; i < asioOut.DriverOutputChannelCount; i++)
                {
                    Console.WriteLine($"[Output {i}]: {asioOut.AsioOutputChannelName(i)}");
                }
                Console.WriteLine("--------------------------------\n");

                // 3. Capturamos la guitarra (Saltamos el Micrófono en el índice 0)
                asioOut.InputChannelOffset = 1;

                // Nota: Aún no ajustamos el OutputChannelOffset hasta ver qué nos dice el escáner

                // 4. Inicializamos Full-Duplex a 48kHz
                asioOut.InitRecordAndPlayback(_dspEngine.ToWaveProvider(), 1, 48000);

                asioOut.AudioAvailable += OnAudioAvailable;

                Console.WriteLine("¡CUIDADO CON EL VOLUMEN! Hard Clipper activado.");

                asioOut.Play();
                Console.WriteLine("Motor Full-Duplex en línea. Toca la guitarra...");
                Console.WriteLine("Presiona ENTER para detener y salir.");
                Console.ReadLine();

                asioOut.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error crítico en ASIO: {ex.Message}");
            }
        }

        private static void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
        {
            int requiredSize = e.SamplesPerBuffer * e.InputBuffers.Length;

            if (_audioBuffer == null || _audioBuffer.Length != requiredSize)
            {
                _audioBuffer = new float[requiredSize];
            }

            e.GetAsInterleavedSamples(_audioBuffer);

            // Medidor RMS para confirmar que entra audio
            double sumSquared = 0;
            for (int i = 0; i < e.SamplesPerBuffer; i++)
            {
                sumSquared += _audioBuffer[i] * _audioBuffer[i];
            }
            double rms = Math.Sqrt(sumSquared / e.SamplesPerBuffer);

            if (rms > 0.001)
            {
                Console.WriteLine($"[SEÑAL DETECTADA] Energía RMS: {rms:F4}");
            }

            _dspEngine?.ProcessInput(_audioBuffer, e.SamplesPerBuffer);
        }
    }
}