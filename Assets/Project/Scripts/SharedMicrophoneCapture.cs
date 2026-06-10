using System;
using System.Collections.Generic;
using Photon.Voice;
using UnityEngine;

public sealed class SharedMicrophoneCapture : MonoBehaviour
{
    private const int DefaultRequestedFrequency = 24000;
    private const int DefaultClipLengthSeconds = 5;
    private const int DefaultRingBufferSeconds = 30;
    private const float FailedStartRetryDelaySeconds = 3f;
    private const float ErrorLogIntervalSeconds = 5f;

    private static SharedMicrophoneCapture instance;

    private readonly object syncRoot = new();
    private readonly List<float> segmentSamples = new();

    private AudioClip microphoneClip;
    private string deviceName;
    private string error;
    private float[] ringBuffer = Array.Empty<float>();

    private int channels;
    private int sampleRate;
    private int lastMicPositionFrames;
    private long totalSamplesWritten;
    private bool isStarted;
    private bool isQuitting;
    private bool segmentActive;
    private bool segmentReachedLimit;
    private int segmentMaxSamples;
    private float nextStartAttemptTime;
    private float lastErrorLogTime = -999f;
    private string lastLoggedError;

    public static SharedMicrophoneCapture Instance
    {
        get
        {
            if (instance != null)
                return instance;

            GameObject captureObject = new GameObject("[SharedMicrophoneCapture]");
            DontDestroyOnLoad(captureObject);
            instance = captureObject.AddComponent<SharedMicrophoneCapture>();
            return instance;
        }
    }

    public bool IsStarted
    {
        get
        {
            lock (syncRoot)
            {
                return isStarted;
            }
        }
    }

    public int SampleRate
    {
        get
        {
            lock (syncRoot)
            {
                return sampleRate;
            }
        }
    }

    public int Channels
    {
        get
        {
            lock (syncRoot)
            {
                return channels;
            }
        }
    }

    public string Error
    {
        get
        {
            lock (syncRoot)
            {
                return error;
            }
        }
    }

    public bool EnsureStarted(int requestedFrequency = DefaultRequestedFrequency, string preferredDevice = "")
    {
        lock (syncRoot)
        {
            if (isStarted)
                return true;

            if (Time.realtimeSinceStartup < nextStartAttemptTime)
                return false;
        }

        if (isQuitting)
            return false;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            SetError("Microphone permission has not been granted yet.");
            return false;
        }
#endif

        string[] devices = Microphone.devices;
        if (devices == null || devices.Length == 0)
        {
            ScheduleRetry();
            SetError("No microphone devices found.");
            return false;
        }

        int frequency = Mathf.Max(8000, requestedFrequency);
        string[] candidateDevices = ResolveDeviceCandidates(devices, preferredDevice);
        string selectedDevice = string.Empty;
        AudioClip clip = null;
        Exception startException = null;
        for (int i = 0; i < candidateDevices.Length; i++)
        {
            string candidate = candidateDevices[i];
            try
            {
                clip = Microphone.Start(candidate, true, DefaultClipLengthSeconds, frequency);
            }
            catch (Exception exception)
            {
                startException = exception;
                clip = null;
            }

            if (clip == null)
                continue;

            selectedDevice = candidate;
            break;
        }

        if (clip == null)
        {
            ScheduleRetry();
            string detail = startException != null ? $" Exception={startException.Message}" : string.Empty;
            SetError($"Microphone.Start failed for available devices: {string.Join(", ", candidateDevices)}.{detail}");
            return false;
        }

        int clipChannels = Mathf.Max(1, clip.channels);
        int clipFrequency = Mathf.Max(8000, clip.frequency);
        int capacity = Mathf.Max(clipFrequency * clipChannels, clipFrequency * clipChannels * DefaultRingBufferSeconds);

        lock (syncRoot)
        {
            deviceName = selectedDevice;
            microphoneClip = clip;
            channels = clipChannels;
            sampleRate = clipFrequency;
            ringBuffer = new float[capacity];
            totalSamplesWritten = 0;
            lastMicPositionFrames = Mathf.Max(0, Microphone.GetPosition(deviceName));
            error = null;
            isStarted = true;
        }

        Debug.Log($"[SharedMicrophoneCapture] Started device='{selectedDevice}', sampleRate={clipFrequency}, channels={clipChannels}");
        return true;
    }

    public IAudioDesc CreatePhotonReader(int requestedFrequency = DefaultRequestedFrequency)
    {
        return new SharedMicrophoneAudioReader(this, requestedFrequency);
    }

    public bool BeginSegment(int maxSeconds, int requestedFrequency = DefaultRequestedFrequency)
    {
        if (!EnsureStarted(requestedFrequency))
            return false;

        lock (syncRoot)
        {
            segmentSamples.Clear();
            segmentActive = true;
            segmentReachedLimit = false;
            segmentMaxSamples = Mathf.Max(1, maxSeconds) * sampleRate * channels;
        }

        Debug.Log("[SharedMicrophoneCapture] Segment recording started.");
        return true;
    }

    public SharedMicrophoneSegment EndSegment()
    {
        lock (syncRoot)
        {
            segmentActive = false;

            SharedMicrophoneSegment segment = new SharedMicrophoneSegment(
                segmentSamples.ToArray(),
                sampleRate,
                channels,
                segmentReachedLimit
            );

            segmentSamples.Clear();
            segmentReachedLimit = false;
            return segment;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        AudioClip clip;
        string selectedDevice;
        int previousPosition;

        lock (syncRoot)
        {
            if (!isStarted || microphoneClip == null)
                return;

            clip = microphoneClip;
            selectedDevice = deviceName;
            previousPosition = lastMicPositionFrames;
        }

        int currentPosition = Microphone.GetPosition(selectedDevice);
        if (currentPosition < 0 || currentPosition == previousPosition)
            return;

        if (currentPosition > previousPosition)
        {
            ReadAndAppendMicFrames(clip, previousPosition, currentPosition - previousPosition);
        }
        else
        {
            ReadAndAppendMicFrames(clip, previousPosition, clip.samples - previousPosition);

            if (currentPosition > 0)
                ReadAndAppendMicFrames(clip, 0, currentPosition);
        }

        lock (syncRoot)
        {
            lastMicPositionFrames = currentPosition;
        }
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
        StopCapture();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            StopCapture();
            instance = null;
        }
    }

    private void StopCapture()
    {
        string selectedDevice;

        lock (syncRoot)
        {
            if (!isStarted)
                return;

            selectedDevice = deviceName;
            isStarted = false;
            microphoneClip = null;
            ringBuffer = Array.Empty<float>();
            segmentActive = false;
            segmentSamples.Clear();
        }

        if (!string.IsNullOrEmpty(selectedDevice) && Microphone.IsRecording(selectedDevice))
            Microphone.End(selectedDevice);

        Debug.Log("[SharedMicrophoneCapture] Stopped.");
    }

    private void ReadAndAppendMicFrames(AudioClip clip, int offsetFrames, int frameCount)
    {
        if (clip == null || frameCount <= 0)
            return;

        int localChannels;
        lock (syncRoot)
        {
            localChannels = channels;
        }

        float[] samples = new float[frameCount * localChannels];
        if (!clip.GetData(samples, offsetFrames))
        {
            Debug.LogWarning($"[SharedMicrophoneCapture] AudioClip.GetData failed. offsetFrames={offsetFrames}, frameCount={frameCount}");
            return;
        }

        AppendSamples(samples);
    }

    private void AppendSamples(float[] samples)
    {
        if (samples == null || samples.Length == 0)
            return;

        lock (syncRoot)
        {
            WriteSamplesToRingBuffer(samples);
            AppendSamplesToSegment(samples);
        }
    }

    private void WriteSamplesToRingBuffer(float[] samples)
    {
        if (ringBuffer.Length == 0)
            return;

        int sourceOffset = 0;
        int samplesToStore = samples.Length;

        if (samplesToStore > ringBuffer.Length)
        {
            sourceOffset = samplesToStore - ringBuffer.Length;
            samplesToStore = ringBuffer.Length;
        }

        long storeStart = totalSamplesWritten + sourceOffset;
        int writeIndex = (int)(storeStart % ringBuffer.Length);
        int firstCopyCount = Math.Min(samplesToStore, ringBuffer.Length - writeIndex);

        Array.Copy(samples, sourceOffset, ringBuffer, writeIndex, firstCopyCount);

        int remaining = samplesToStore - firstCopyCount;
        if (remaining > 0)
            Array.Copy(samples, sourceOffset + firstCopyCount, ringBuffer, 0, remaining);

        totalSamplesWritten += samples.Length;
    }

    private void AppendSamplesToSegment(float[] samples)
    {
        if (!segmentActive || segmentMaxSamples <= 0)
            return;

        int remainingCapacity = segmentMaxSamples - segmentSamples.Count;
        if (remainingCapacity <= 0)
        {
            segmentActive = false;
            segmentReachedLimit = true;
            return;
        }

        int count = Math.Min(samples.Length, remainingCapacity);
        for (int i = 0; i < count; i++)
            segmentSamples.Add(samples[i]);

        if (count < samples.Length || segmentSamples.Count >= segmentMaxSamples)
        {
            segmentActive = false;
            segmentReachedLimit = true;
            Debug.Log("[SharedMicrophoneCapture] Segment reached its configured maximum length.");
        }
    }

    private bool ReadForReader(ref long readSampleCursor, float[] buffer)
    {
        if (buffer == null || buffer.Length == 0)
            return false;

        lock (syncRoot)
        {
            if (!isStarted || ringBuffer.Length == 0)
                return false;

            long oldestAvailableSample = Math.Max(0, totalSamplesWritten - ringBuffer.Length);
            if (readSampleCursor < oldestAvailableSample)
                readSampleCursor = oldestAvailableSample;

            if (totalSamplesWritten - readSampleCursor < buffer.Length)
                return false;

            int readIndex = (int)(readSampleCursor % ringBuffer.Length);
            int firstCopyCount = Math.Min(buffer.Length, ringBuffer.Length - readIndex);

            Array.Copy(ringBuffer, readIndex, buffer, 0, firstCopyCount);

            int remaining = buffer.Length - firstCopyCount;
            if (remaining > 0)
                Array.Copy(ringBuffer, 0, buffer, firstCopyCount, remaining);

            readSampleCursor += buffer.Length;
            return true;
        }
    }

    private long GetCurrentSamplePosition()
    {
        lock (syncRoot)
        {
            return totalSamplesWritten;
        }
    }

    private void ScheduleRetry()
    {
        lock (syncRoot)
        {
            nextStartAttemptTime = Time.realtimeSinceStartup + FailedStartRetryDelaySeconds;
        }
    }

    private void SetError(string message)
    {
        lock (syncRoot)
        {
            error = message;
        }

        if (!ShouldLogError(message))
            return;

        Debug.LogWarning($"[SharedMicrophoneCapture] {message}");
    }

    private bool ShouldLogError(string message)
    {
        float now = Time.realtimeSinceStartup;
        if (string.Equals(lastLoggedError, message, StringComparison.Ordinal) &&
            now - lastErrorLogTime < ErrorLogIntervalSeconds)
            return false;

        lastLoggedError = message;
        lastErrorLogTime = now;
        return true;
    }

    private static string[] ResolveDeviceCandidates(string[] devices, string preferredDevice)
    {
        List<string> candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredDevice))
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (string.Equals(devices[i], preferredDevice, StringComparison.Ordinal))
                {
                    candidates.Add(preferredDevice);
                    break;
                }
            }

            if (candidates.Count == 0)
                Debug.LogWarning($"[SharedMicrophoneCapture] Preferred microphone '{preferredDevice}' was not found. Trying available microphones.");
        }

        for (int i = 0; i < devices.Length; i++)
        {
            string device = devices[i];
            if (!candidates.Contains(device))
                candidates.Add(device);
        }

        return candidates.ToArray();
    }

    private sealed class SharedMicrophoneAudioReader : IAudioReader<float>
    {
        private readonly SharedMicrophoneCapture capture;
        private readonly int channels;
        private readonly int sampleRate;
        private long readSampleCursor;
        private bool disposed;
        private string error;

        public SharedMicrophoneAudioReader(SharedMicrophoneCapture capture, int requestedFrequency)
        {
            this.capture = capture;

            if (!capture.EnsureStarted(requestedFrequency))
            {
                error = capture.Error ?? "Shared microphone capture failed to start.";
                return;
            }

            channels = capture.Channels;
            sampleRate = capture.SampleRate;
            readSampleCursor = capture.GetCurrentSamplePosition();
        }

        public int SamplingRate => error == null ? sampleRate : 0;
        public int Channels => error == null ? channels : 0;
        public string Error => error;

        public bool Read(float[] buffer)
        {
            if (disposed || error != null)
                return false;

            return capture.ReadForReader(ref readSampleCursor, buffer);
        }

        public void Dispose()
        {
            disposed = true;
        }
    }
}

public readonly struct SharedMicrophoneSegment
{
    public SharedMicrophoneSegment(float[] samples, int sampleRate, int channels, bool reachedLimit)
    {
        Samples = samples ?? Array.Empty<float>();
        SampleRate = sampleRate;
        Channels = channels;
        ReachedLimit = reachedLimit;
    }

    public float[] Samples { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public bool ReachedLimit { get; }
    public bool HasSamples => Samples.Length > 0 && SampleRate > 0 && Channels > 0;
    public float DurationSeconds => HasSamples ? Samples.Length / (float)(SampleRate * Channels) : 0f;
}
