using System;
using System.Text;
using UnityEngine;

public static class SharedMicrophoneEditModeValidation
{
    public static void Run()
    {
        byte[] wav = WavUtility.FromSamples(new[] { -1f, 0f, 0.5f, 1f, 2f, -2f }, 16000, 1);

        Expect(wav.Length == 56, $"Expected 56 WAV bytes, got {wav.Length}.");
        Expect(ReadAscii(wav, 0, 4) == "RIFF", "Missing RIFF header.");
        Expect(ReadAscii(wav, 8, 4) == "WAVE", "Missing WAVE header.");
        Expect(ReadAscii(wav, 12, 4) == "fmt ", "Missing fmt chunk.");
        Expect(ReadAscii(wav, 36, 4) == "data", "Missing data chunk.");
        Expect(BitConverter.ToInt32(wav, 24) == 16000, "Unexpected sample rate.");
        Expect(BitConverter.ToInt16(wav, 22) == 1, "Unexpected channel count.");
        Expect(BitConverter.ToInt32(wav, 40) == 12, "Unexpected data chunk length.");
        Expect(BitConverter.ToInt16(wav, 44) == -32767, "Expected first sample to be clamped to -1.");
        Expect(BitConverter.ToInt16(wav, 50) == 32767, "Expected fourth sample to be clamped to 1.");
        Expect(BitConverter.ToInt16(wav, 52) == 32767, "Expected high sample to be clamped to 1.");
        Expect(BitConverter.ToInt16(wav, 54) == -32767, "Expected low sample to be clamped to -1.");

        Debug.Log("[SharedMicrophoneEditModeValidation] WAV sample conversion passed.");
    }

    private static string ReadAscii(byte[] bytes, int index, int count)
    {
        return Encoding.ASCII.GetString(bytes, index, count);
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
