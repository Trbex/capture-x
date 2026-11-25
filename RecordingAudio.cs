using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace RealCaptureX;

internal sealed class RecordingAudio : IDisposable
{
    public const int Rate = 48000;
    public const int Channels = 2;
    public const int PacketFrames = 960;
    private readonly List<WasapiCapture> devices = [];
    private readonly List<BufferedWaveProvider> buffers = [];
    private readonly MixingSampleProvider mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(Rate, Channels))
    {
        ReadFully = true
    };
    private Exception? failed;

    public RecordingAudio(bool system, bool microphone)
    {
        try
        {
            if (system) Add(new WasapiLoopbackCapture());
            if (microphone) Add(new WasapiCapture());
            foreach (var device in devices) device.StartRecording();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void Add(WasapiCapture device)
    {
        devices.Add(device);
        var buffer = new BufferedWaveProvider(device.WaveFormat)
        {
            DiscardOnBufferOverflow = true, ReadFully = true, BufferDuration = TimeSpan.FromSeconds(1)
        };
        buffers.Add(buffer);
        device.DataAvailable += (_, args) => buffer.AddSamples(args.Buffer, 0, args.BytesRecorded);
        device.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null) Interlocked.CompareExchange(ref failed, args.Exception, null);
        };
        ISampleProvider provider = buffer.ToSampleProvider();
        if (provider.WaveFormat.Channels == 1) provider = new MonoToStereoSampleProvider(provider);
        if (provider.WaveFormat.Channels > 2)
        {
            var channels = new MultiplexingSampleProvider([provider], Channels);
            channels.ConnectInputToOutput(0, 0);
            channels.ConnectInputToOutput(1, 1);
            provider = channels;
        }
        if (provider.WaveFormat.SampleRate != Rate) provider = new WdlResamplingSampleProvider(provider, Rate);
        mixer.AddMixerInput(provider);
    }

    public void StartTimeline()
    {
        foreach (var buffer in buffers) buffer.ClearBuffer();
    }

    public IBuffer ReadPacket()
    {
        if (Volatile.Read(ref failed) is { } failure)
            throw new InvalidOperationException("The selected audio device stopped recording.", failure);
        var floatSamples = new float[PacketFrames * Channels];
        var pcm = new byte[floatSamples.Length * sizeof(short)];
        var samples = MemoryMarshal.Cast<byte, short>(pcm.AsSpan());
        mixer.Read(floatSamples, 0, floatSamples.Length);
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(Math.Clamp(floatSamples[i], -1, 1) * short.MaxValue);
        return pcm.AsBuffer();
    }

    public void Dispose()
    {
        foreach (var device in devices) device.Dispose();
    }
}
