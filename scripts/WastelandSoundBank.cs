using System;
using Godot;
using Cue = Star.WastelandAudio.Cue;

namespace Star;

/// <summary>一次性生成短促的无线电信号、机械声和可无缝循环的沙尘声。</summary>
internal static class WastelandSoundBank
{
    private const int SampleRate = 22050;

    internal static AudioStreamWav Wind(bool storm)
    {
        var random = new Random(storm ? 7081 : 3011);
        float rumble = 0f;
        float air = 0f;
        float dust = 0f;
        return Synthesize(storm ? 14f : 11f, t =>
        {
            float noise = 2f * random.NextSingle() - 1f;
            rumble += (noise - rumble) * 0.012f;
            air += (noise - air) * 0.095f;
            dust += (noise - dust) * 0.37f;
            float gusts = 0.58f + 0.22f * Sine(t, 0.17f) + 0.13f * Sine(t, 0.37f)
                + 0.07f * Sine(t, 0.91f);
            float grit = dust - air;
            float body = air - rumble;

            return storm
                ? gusts * (rumble * 3.4f + body * 2.15f + grit * 0.88f)
                : gusts * (rumble * 1.3f + body * 1.45f + grit * 0.42f);
        }, loop: true);
    }

    internal static AudioStreamWav Machinery()
    {
        var random = new Random(902);
        float hiss = 0f;
        return Synthesize(9f, t =>
        {
            hiss += (2f * random.NextSingle() - 1f - hiss) * 0.15f;
            float load = 0.72f + 0.13f * Sine(t, 0.47f) + 0.1f * Sine(t, 0.13f);
            float fan = (0.55f + 0.45f * Sine(t, 13f)) * hiss * 0.24f;
            return load * (0.09f * Sine(t, 50f) + 0.075f * Sine(t, 100f)
                + 0.042f * Sine(t, 154f) + 0.03f * Sine(t, 226f) + fan);
        }, loop: true);
    }

    internal static AudioStreamWav[] Cues()
    {
        var clips = new AudioStreamWav[(int)Cue.Defeat + 1];
        clips[(int)Cue.Ui] = Synthesize(0.08f, t => 0.42f * Tone(t, 720f, 0.08f, -150f));
        clips[(int)Cue.Select] = Synthesize(0.12f, t => 0.6f * Tone(t, 840f, 0.12f, 200f));
        clips[(int)Cue.Order] = Synthesize(0.24f, t => 0.52f * Tone(t, 480f, 0.11f)
            + 0.63f * Tone(t - 0.085f, 670f, 0.15f));
        clips[(int)Cue.Denied] = Synthesize(0.23f, t => 0.57f * Tone(t, 255f, 0.12f, -200f)
            + 0.48f * Tone(t - 0.09f, 185f, 0.14f, -95f));
        clips[(int)Cue.Notice] = Synthesize(0.3f, t => 0.53f * Tone(t, 512f, 0.12f)
            + 0.52f * Tone(t - 0.12f, 610f, 0.18f, -75f));
        clips[(int)Cue.Decision] = Synthesize(0.62f, t => 0.58f * Tone(t, 340f, 0.23f, 390f)
            + 0.61f * Tone(t - 0.26f, 340f, 0.35f, 390f));
        clips[(int)Cue.Complete] = Synthesize(0.56f, t => 0.47f * Tone(t, 440f, 0.24f)
            + 0.45f * Tone(t - 0.095f, 554.37f, 0.29f)
            + 0.47f * Tone(t - 0.20f, 659.26f, 0.36f));
        clips[(int)Cue.StormWarning] = Synthesize(1.05f, t => 0.7f * Tone(t, 195f, 0.31f, 550f)
            + 0.7f * Tone(t - 0.34f, 195f, 0.3f, 550f)
            + 0.67f * Tone(t - 0.68f, 195f, 0.37f, 550f));
        clips[(int)Cue.StormOnset] = Synthesize(0.61f, t => 0.62f * Tone(t, 140f, 0.59f, -136f)
            + 0.30f * Tone(t - 0.025f, 226f, 0.55f, -135f));
        clips[(int)Cue.StormClear] = Synthesize(0.43f, t => 0.55f * Tone(t, 415.30f, 0.22f)
            + 0.47f * Tone(t - 0.115f, 523.25f, 0.31f));
        clips[(int)Cue.Victory] = Synthesize(1.73f, t => 0.31f * Tone(t, 196f, 1.55f)
            + 0.22f * Tone(t - 0.16f, 293.66f, 1.44f)
            + 0.21f * Tone(t - 0.28f, 392f, 1.4f)
            + 0.19f * Tone(t - 0.39f, 493.88f, 1.34f));
        clips[(int)Cue.Defeat] = Synthesize(1.58f, t => 0.35f * Tone(t, 146.83f, 1.35f, -34f)
            + 0.25f * Tone(t - 0.12f, 138.59f, 1.35f, -31f)
            + 0.22f * Tone(t - 0.28f, 97.99f, 1.29f, -24f));
        return clips;
    }

    internal static AudioStreamWav Weapon(bool heavy, bool air)
    {
        if (air)
        {
            return Synthesize(0.31f, t => 0.56f * Tone(t, 280f, 0.3f, -510f)
                + 0.42f * Tone(t, 580f, 0.24f, -1250f));
        }

        var random = new Random(heavy ? 1338 : 9078);
        float lowPass = 0f;
        return Synthesize(heavy ? 0.67f : 0.29f, t =>
        {
            float noise = 2f * random.NextSingle() - 1f;
            lowPass += (noise - lowPass) * 0.2f;
            if (heavy)
            {
                float attack = Mathf.Min(t / 0.004f, 1f);
                float blast = 0.56f * lowPass * MathF.Exp(-t * 12f);
                float crack = 0.40f * noise * MathF.Exp(-t * 45f);
                float recoil = 0.65f * Sine(t, 89f - 33f * t) * MathF.Exp(-t * 8.5f);
                return (blast + crack + recoil) * attack * (1f - t / 0.67f);
            }

            float burst = Pop(t, 0.09f) + 0.81f * Pop(t - 0.075f, 0.09f)
                + 0.63f * Pop(t - 0.16f, 0.10f);
            return burst * (0.47f * noise + 0.74f * lowPass + 0.17f * Sine(t, 147f));
        });
    }

    private static float Pop(float time, float length)
    {
        if (time < 0f || time >= length)
        {
            return 0f;
        }

        return Mathf.Min(time / 0.003f, 1f) * MathF.Pow(1f - time / length, 3f);
    }

    private static float Tone(float time, float frequency, float length, float glide = 0f)
    {
        if (time < 0f || time >= length)
        {
            return 0f;
        }

        float phase = Mathf.Tau * (frequency + glide * time * 0.5f) * time;
        float envelope = Mathf.Min(time / 0.015f, 1f) * MathF.Pow(1f - time / length, 1.8f);
        return envelope * (0.78f * MathF.Sin(phase) + 0.16f * MathF.Sin(2f * phase)
            + 0.06f * MathF.Sin(3f * phase));
    }

    private static float Sine(float time, float frequency) => MathF.Sin(Mathf.Tau * frequency * time);

    private static AudioStreamWav Synthesize(float seconds, Func<float, float> sample, bool loop = false)
    {
        int frames = (int)(seconds * SampleRate);
        int overlap = loop ? (int)(0.48f * SampleRate) : 0;
        var pcm = new byte[frames * 2];
        var head = new float[overlap];
        for (int i = 0; i < frames; i++)
        {
            float value = sample(i / (float)SampleRate);
            if (i < overlap)
            {
                head[i] = value;
            }
            else
            {
                WritePcm(pcm, i, value);
            }
        }

        // 从自然延伸的尾声过渡到开头，循环接缝保留噪声滤波的连续性。
        for (int i = 0; i < overlap; i++)
        {
            float tail = sample((frames + i) / (float)SampleRate);
            float progress = Mathf.SmoothStep(0f, 1f, i / (float)(overlap - 1));
            WritePcm(pcm, i, Mathf.Lerp(tail, head[i], progress));
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Data = pcm,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopEnd = loop ? frames : 0,
        };
    }

    private static void WritePcm(byte[] pcm, int frame, float sample)
    {
        int value = (int)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue);
        pcm[frame * 2] = (byte)(value & 0xff);
        pcm[frame * 2 + 1] = (byte)((value >> 8) & 0xff);
    }
}
