using Metmuseum.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
});

services.AddHttpClient("metMuseum", c =>
{
    c.BaseAddress = new Uri("https://collectionapi.metmuseum.org/public/collection/v1/");
    c.Timeout = TimeSpan.FromSeconds(60);
});
services.AddSingleton<IMetMuseumService, MetMuseumService>();
services.AddSingleton<ImportService>();

var provider = services.BuildServiceProvider();
var logger = provider.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting Met Museum import...");

var importService = provider.GetRequiredService<ImportService>();

using var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    logger.LogWarning("Cancellation requested...");
    cancellationTokenSource.Cancel();
    e.Cancel = true;
};

const int parallelism = 5;
try
{
    await importService.RunAsync(parallelism, cancellationTokenSource.Token);
    logger.LogInformation("Import finished.");
}
catch (OperationCanceledException)
{
    logger.LogWarning("Import cancelled by user.");
}
catch (Exception ex)
{
    logger.LogError(ex, "Fatal error during import.");
}