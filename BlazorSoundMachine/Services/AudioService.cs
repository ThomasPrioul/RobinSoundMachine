using ProjectCeilidh.PortAudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorSoundMachine.Services
{
    public class AudioService
    {
        readonly PortAudioSampleFormat outputFormat = new PortAudioSampleFormat(PortAudioSampleFormat.PortAudioNumberFormat.Signed, 2);
        readonly PortAudioHostApi api;
        readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

        public AudioService()
        {
            api = PortAudioHostApi.SupportedHostApis.First();
            Devices = api.Devices.Where(x => x.MaxOutputChannels > 0).ToArray();
            if (Devices.Length == 0) throw new InvalidOperationException("PC has no sound output device");
            OutputDevice = api.DefaultOutputDevice;
        }

        public PortAudioDevice[] Devices { get; }

        public PortAudioDevice OutputDevice { get; set; }

        public event EventHandler<bool> Playing;

        public async Task PlayAudioStream(Stream audioStream, CancellationToken token = default)
        {
            try
            {
                Playing?.Invoke(this, true);
                await semaphore.WaitAsync(token);
                using var pump = new PortAudioDevicePump(OutputDevice, 2, outputFormat, OutputDevice.DefaultLowOutputLatency, 48000, ReadCallback);
                using var handle = new ManualResetEventSlim(false);
                pump.StreamFinished += FinishedHandler;
                pump.Start();
                handle.Wait();
                pump.StreamFinished -= FinishedHandler;

                int ReadCallback(byte[] buffer, int offset, int count) => audioStream.Read(buffer, offset, count);
                void FinishedHandler(object sender, EventArgs eventArgs) => handle.Set();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (semaphore.CurrentCount == 0) semaphore.Release();
                Playing?.Invoke(this, false);
            }
        }
    }
}
