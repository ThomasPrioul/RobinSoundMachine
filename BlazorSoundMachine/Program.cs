using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlazorSoundMachine
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            if (!ConsoleInfo.LaunchedFromConsole)
                ConsoleInfo.HideConsole();

            using var cts = new CancellationTokenSource();
            Console.InputEncoding = Encoding.Unicode;
            Console.OutputEncoding = Encoding.Unicode;
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            using var host = CreateHostBuilder(args).Build();
            try
            {
                await host.StartAsync(cts.Token);
                await Task.Run(() => cts.Token.WaitHandle.WaitOne());
                await host.StopAsync();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.ToString());
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
