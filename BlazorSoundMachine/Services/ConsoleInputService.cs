using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorSoundMachine.Services
{
    public class ConsoleInputService : BackgroundService
    {
        readonly AudioService audio;
        readonly TextToSpeechService textToSpeech;

        public ConsoleInputService(AudioService audio, TextToSpeechService textToSpeech)
        {
            this.audio = audio;
            this.textToSpeech = textToSpeech;
        }

        static bool ReadLine(StreamReader input, CancellationToken cancellationToken, out string? line)
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

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.Run(async () =>
            {
                using var input = new StreamReader(Console.OpenStandardInput(), Encoding.Unicode);
            
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Console.Out.WriteAsync("->");

                    try
                    {
                        // Handle user input
                        if (!ReadLine(input, stoppingToken, out string? line) || line is null) return;

                        using var audioStream = await textToSpeech.TextToAudioStreamAsync(line, token: stoppingToken);
                        await audio.PlayAudioStream(audioStream, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception error)
                    {
                        await Console.Error.WriteLineAsync(error.ToString());
                    }
                }
            });
        }
    }
}
