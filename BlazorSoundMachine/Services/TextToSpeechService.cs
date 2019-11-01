using Concentus.Oggfile;
using Concentus.Structs;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.TextToSpeech.V1;
using Grpc.Auth;
using Grpc.Core;
using ProjectCeilidh.PortAudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorSoundMachine.Services
{
    public class TextToSpeechService
    {
        readonly TextToSpeechClient ttsClient;

        public TextToSpeechService()
        {
            ttsClient = TextToSpeechClient.Create(
                new Channel(
                    "texttospeech.googleapis.com:443",
                    GoogleCredential
                        .FromFile("TTS-Robin-19301c00354e.json")
                        .CreateScoped(TextToSpeechClient.DefaultScopes)
                        .ToChannelCredentials()));
        }

        public async Task<Stream> TextToAudioStreamAsync(string text, string language = "fr-FR", SsmlVoiceGender voiceGender = SsmlVoiceGender.Male, CancellationToken token = default)
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
            using var opusStream = new MemoryStream();
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
                        byte[] bytes = BitConverter.GetBytes(packet[i]);
                        pcmStream.Write(bytes, 0, bytes.Length);
                    }
                }
            }

            pcmStream.Position = 0;
            return pcmStream;
        }
    }
}
