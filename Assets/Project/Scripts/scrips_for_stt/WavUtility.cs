using System;
using System.IO;
using UnityEngine;

public static class WavUtility
{
    public static byte[] FromAudioClip(AudioClip clip)
    {
        using MemoryStream stream = new MemoryStream();

        int channels = clip.channels;
        int sampleRate = clip.frequency;
        int samples = clip.samples;

        float[] floatData = new float[samples * channels];
        clip.GetData(floatData, 0);

        short[] intData = new short[floatData.Length];
        byte[] bytesData = new byte[floatData.Length * 2];

        const float rescaleFactor = 32767f;

        for (int i = 0; i < floatData.Length; i++)
        {
            intData[i] = (short)(floatData[i] * rescaleFactor);
            byte[] byteArr = BitConverter.GetBytes(intData[i]);
            byteArr.CopyTo(bytesData, i * 2);
        }

        WriteHeader(stream, clip, bytesData.Length);
        stream.Write(bytesData, 0, bytesData.Length);

        return stream.ToArray();
    }

    private static void WriteHeader(Stream stream, AudioClip clip, int dataLength)
    {
        int channels = clip.channels;
        int sampleRate = clip.frequency;
        int byteRate = sampleRate * channels * 2;

        stream.Position = 0;

        stream.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
        stream.Write(BitConverter.GetBytes(dataLength + 36), 0, 4);
        stream.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));
        stream.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
        stream.Write(BitConverter.GetBytes(16), 0, 4);
        stream.Write(BitConverter.GetBytes((ushort)1), 0, 2);
        stream.Write(BitConverter.GetBytes((ushort)channels), 0, 2);
        stream.Write(BitConverter.GetBytes(sampleRate), 0, 4);
        stream.Write(BitConverter.GetBytes(byteRate), 0, 4);
        stream.Write(BitConverter.GetBytes((ushort)(channels * 2)), 0, 2);
        stream.Write(BitConverter.GetBytes((ushort)16), 0, 2);
        stream.Write(System.Text.Encoding.UTF8.GetBytes("data"));
        stream.Write(BitConverter.GetBytes(dataLength), 0, 4);
    }
}