namespace RobinSoundMachine
{
    using Google.Cloud.TextToSpeech.V1;
    using System;
    using System.IO;
    using ProjectCeilidh.PortAudio;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Linq;
    using Google.Apis.Auth.OAuth2;
    using Grpc.Auth;
    using Grpc.Core;
    using Concentus.Structs;
    using Concentus.Oggfile;
    using System.Text;
    using System.Collections.Generic;

    class Program
    {
        const long waveFileHeaderSize = 44;
        readonly TextToSpeechClient ttsClient;

        public Program()
        {
            ttsClient = TextToSpeechClient.Create(
                new Channel(
                    "texttospeech.googleapis.com:443",
                    GoogleCredential
                        .FromFile("TTS-Robin-19301c00354e.json")
                        .CreateScoped(TextToSpeechClient.DefaultScopes)
                        .ToChannelCredentials()));
        }

        static async Task Main(string[] args)
        {
            try
            {
                await new Program().Run();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(error);
                Console.ReadLine();
            }
        }

        static PortAudioHostApi PickBestApi(IEnumerable<PortAudioHostApi> supportedHostApis)
        {
            if (supportedHostApis.FirstOrDefault(x => x.HostApiType == PortAudioHostApiType.Wasapi) is PortAudioHostApi wasapi) return wasapi;
            else if (supportedHostApis.FirstOrDefault(x => x.HostApiType == PortAudioHostApiType.DirectSound) is PortAudioHostApi directSound) return directSound;
            else if (supportedHostApis.FirstOrDefault(x => x.HostApiType == PortAudioHostApiType.Mme) is PortAudioHostApi mme) return mme;
            else return supportedHostApis.First();
        }

        static bool ReadLine(StreamReader input, CancellationToken cancellationToken, out string line)
        {
            line = null;

            try
            {
                var readOperation = input.ReadLineAsync();
                readOperation.Wait(cancellationToken);
                line = readOperation.Result;
                return true;
            }
            catch
            {
                return false;
            }
        }

        PortAudioDevice GetOutputDevice(StreamReader consoleInput, IEnumerable<PortAudioDevice> devices, CancellationToken cancellationToken)
        {
            var outputDevices = devices.Where(x => x.MaxOutputChannels >= 2).ToArray();
            if (outputDevices.Length == 0) throw new InvalidOperationException("Aucun périphérique de sortie détecté");

            var plural = (outputDevices.Length > 1 ? "s" : "");
            Console.WriteLine($"{outputDevices.Length} périphérique{plural} de sortie détecté{plural}");
            if (outputDevices.Length == 1) return outputDevices[0];

            Console.WriteLine("Choisir le périphérique de sortie:");

            for (int i = 0; i < outputDevices.Length; i++)
            {
                Console.WriteLine($"[{i.ToString("D2")}] - {outputDevices[i].Name}");
            }

            Console.WriteLine("Taper le numéro choisi ->");
            if (!ReadLine(consoleInput, cancellationToken, out string line)) throw new OperationCanceledException();
            return outputDevices[int.Parse(line)];
        }

        async Task Run()
        {
            using (var cts = new CancellationTokenSource())
            {
                Console.InputEncoding = Encoding.Unicode;
                Console.OutputEncoding = Encoding.Unicode;
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                using (var input = new StreamReader(Console.OpenStandardInput(), Encoding.Unicode))
                using (var api = PickBestApi(PortAudioHostApi.SupportedHostApis))
                using (var device = GetOutputDevice(input, api.Devices, cts.Token))
                {
                    if (device.MaxOutputChannels <= 0) return;

                    Console.WriteLine($"Prêt à parler sur {device.Name}");

                    while (!cts.IsCancellationRequested)
                    {
                        Console.Write("->");

                        // Handle user input
                        if (!ReadLine(input, cts.Token, out string line) || line is null) return;

                        Console.WriteLine("Récupération du son pour le texte : " + line);
                        using (var audioStream = await TextToAudioStreamAsync(line, cts.Token))
                        {
                            Console.WriteLine("Son récupéré, lecture...");

                            using (var pump = new PortAudioDevicePump(
                                device,
                                2,
                                new PortAudioSampleFormat(PortAudioSampleFormat.PortAudioNumberFormat.Signed, 2),
                                device.DefaultLowOutputLatency,
                                48000,
                                (buffer, offset, count) => audioStream.Read(buffer, offset, count)))
                            {
                                using (var handle = new ManualResetEventSlim(false))
                                {
                                    pump.StreamFinished += (sender, eventArgs) => handle.Set();
                                    pump.Start();
                                    handle.Wait();
                                }
                            }

                            Console.WriteLine("Lecture terminée");
                        }
                    }
                }
            }

            async Task<Stream> TextToAudioStreamAsync(string text, CancellationToken token)
            {
                var request = new SynthesizeSpeechRequest
                {
                    AudioConfig = new AudioConfig
                    {
                        AudioEncoding = AudioEncoding.OggOpus,
                    },
                    Input = new SynthesisInput
                    {
                        Text = text
                    },
                    Voice = new VoiceSelectionParams
                    {
                        LanguageCode = "fr-FR",
                        SsmlGender = SsmlVoiceGender.Female,
                    },
                };

                var response = await ttsClient.SynthesizeSpeechAsync(request, token);
                using (var opusStream = new MemoryStream())
                {
                    response.AudioContent.WriteTo(opusStream);
                    opusStream.Position = 0;

                    var opusDecoder = new OpusDecoder(48000, 2);
                    var oggIn = new OpusOggReadStream(opusDecoder, opusStream);

                    var pcmStream = new MemoryStream();
                    while (oggIn.HasNextPacket)
                    {
                        short[] packet = oggIn.DecodeNextPacket();
                        if (packet != null)
                        {
                            for (int i = 0; i < packet.Length; i++)
                            {
                                var bytes = BitConverter.GetBytes(packet[i]);
                                pcmStream.Write(bytes, 0, bytes.Length);
                            }
                        }
                    }

                    pcmStream.Position = 0;
                    return pcmStream;
                }
            }
        }
    }
}