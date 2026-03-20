using System;

namespace DawEngine.Core
{
    public class LowPassFilter : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        private float _sampleRate = 48000f;
        private float _cutoffFrequency = 4000f; // Suaviza la distorsión aguda
        private float _q = 0.707f;

        // Coeficientes Biquad
        private float b0, b1, b2, a1, a2;
        private float x1 = 0, x2 = 0, y1 = 0, y2 = 0;

        public LowPassFilter()
        {
            CalculateCoefficients();
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Cutoff") _cutoffFrequency = value;
            else if (name == "Q") _q = value;

            CalculateCoefficients();
        }

        private void CalculateCoefficients()
        {
            double w0 = 2 * Math.PI * _cutoffFrequency / _sampleRate;
            double alpha = Math.Sin(w0) / (2 * _q);
            double cosW0 = Math.Cos(w0);

            double a0 = 1 + alpha;
            b0 = (float)((1 - cosW0) / 2 / a0);
            b1 = (float)((1 - cosW0) / a0);
            b2 = (float)((1 - cosW0) / 2 / a0);
            a1 = (float)(-2 * cosW0 / a0);
            a2 = (float)((1 - alpha) / a0);
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float x0 = buffer[i];
                float y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;

                x2 = x1;
                x1 = x0;
                y2 = y1;
                y1 = y0;

                buffer[i] = y0;
            }
        }
    }
}