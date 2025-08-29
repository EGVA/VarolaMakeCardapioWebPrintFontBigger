using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "RAW Print Processor Service";
            })
            .ConfigureServices(services =>
            {
                services.AddHostedService<PrintMonitorService>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                logging.AddEventLog(settings =>
                {
                    settings.SourceName = "RAW Print Processor";
                    settings.LogName = "Application";
                });
                logging.AddConsole();
            });
}