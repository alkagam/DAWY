using System;

namespace DawEngine.Core
{
    public class HardClipper : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;
        private float _threshold = 0.5f;
        private float _gain = 5.0f;

        public void UpdateParameter(string name, float value)
        {
            if (name == "Threshold") _threshold = value;
            else if (name == "Gain") _gain = value;
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float sample = buffer[i] * _gain;
                if (sample > _threshold) sample = _threshold;
                else if (sample < -_threshold) sample = -_threshold;
                buffer[i] = sample;
            }
        }
    }
}