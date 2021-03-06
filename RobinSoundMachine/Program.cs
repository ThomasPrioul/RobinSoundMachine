﻿namespace RobinSoundMachine
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
    using Microsoft.Extensions.Hosting;
    using Microsoft.AspNetCore.Hosting;

    class Program
    {
        const long waveFileHeaderSize = 44;
        readonly TextToSpeechClient ttsClient;

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });

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

        static PortAudioHostApi PickBestApi(StreamReader consoleInput, IEnumerable<PortAudioHostApi> supportedHostApis, CancellationToken cancellationToken)
        {
            Console.WriteLine("Choisir l'API de sortie audio:");
            var audioApis = supportedHostApis.Reverse().ToArray();

            for (int i = 0; i < audioApis.Length; i++)
                Console.WriteLine($"[{i.ToString("D2")}] - {audioApis[i].Name} - {audioApis[i].HostApiType}");

            return audioApis[0];

            /*
            Console.Write("Taper le numéro choisi ->");
            if (!ReadLine(consoleInput, cancellationToken, out string line)) throw new OperationCanceledException();
            return audioApis[int.Parse(line)];
            */
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

        static PortAudioDevice GetOutputDevice(StreamReader consoleInput, IEnumerable<PortAudioDevice> devices, CancellationToken cancellationToken)
        {
            var outputDevices = devices.Where(x => x.MaxOutputChannels >= 2).ToArray();
            if (outputDevices.Length == 0) throw new InvalidOperationException("Aucun périphérique de sortie détecté");

            string plural = outputDevices.Length > 1 ? "s" : "";
            Console.WriteLine($"{outputDevices.Length} périphérique{plural} de sortie détecté{plural}");
            if (outputDevices.Length == 1) return outputDevices[0];

            Console.WriteLine("Choisir le périphérique de sortie:");

            for (int i = 0; i < outputDevices.Length; i++)
            {
                Console.WriteLine($"[{i.ToString("D2")}] - {outputDevices[i].Name}");
            }

            Console.Write("Taper le numéro choisi -> ");
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

                try
                {
                    using var input = new StreamReader(Console.OpenStandardInput(), Encoding.Unicode);
                    using var api = PickBestApi(input, PortAudioHostApi.SupportedHostApis, cts.Token);
                    using var device = GetOutputDevice(input, api.Devices, cts.Token);

                    Console.Write("Saisir le code de langue voulu (défaut : fr-FR): ");
                    if (!ReadLine(input, cts.Token, out string language)) return;
                    if (string.IsNullOrWhiteSpace(language))
                        language = "fr-FR";

                    Console.WriteLine("Choisir le type de voix (femme/homme):");
                    Console.WriteLine("[0] - Non spécifié");
                    Console.WriteLine("[1] - Homme");
                    Console.WriteLine("[2] - Femme");
                    Console.WriteLine("[2] - Neutre");

                    if (!ReadLine(input, cts.Token, out string voiceGenderStr) || !int.TryParse(voiceGenderStr, out int voiceGenderInt)) return;

                    var voiceGender = (SsmlVoiceGender)voiceGenderInt;

                    Console.WriteLine($"Prêt à parler sur {device.Name}");

                    while (!cts.IsCancellationRequested)
                    {
                        Console.Write("->");

                        // Handle user input
                        if (!ReadLine(input, cts.Token, out string line) || line is null) return;

                        Console.WriteLine("Récupération du son pour le texte : " + line);
                        Console.WriteLine("Son récupéré, lecture...");

                        using var audioStream = await TextToAudioStreamAsync(line, language, voiceGender, cts.Token);
                        using var pump = new PortAudioDevicePump(
                            device,
                            2,
                            new PortAudioSampleFormat(PortAudioSampleFormat.PortAudioNumberFormat.Signed, 2),
                            device.DefaultLowOutputLatency,
                            48000,
                            (buffer, offset, count) => audioStream.Read(buffer, offset, count));

                        using var handle = new ManualResetEventSlim(false);
                        pump.StreamFinished += FinishedHandler;
                        pump.Start();
                        handle.Wait();
                        pump.StreamFinished -= FinishedHandler;

                        void FinishedHandler(object sender, EventArgs eventArgs) => handle.Set();
                        Console.WriteLine("Lecture terminée");
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }

            async Task<Stream> TextToAudioStreamAsync(string text, string language, SsmlVoiceGender voiceGender, CancellationToken token)
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
                        LanguageCode = language,
                        SsmlGender = voiceGender,
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