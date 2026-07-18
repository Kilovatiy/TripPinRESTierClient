using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Application;
using Client.Core.Abstractions;
using Client.Infrastructure;
using Client.Presentation.Logging;
using Client.Presentation.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PanoramicData.OData.Client;
using Serilog;

var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

if (!File.Exists(appSettingsPath))
{
    Console.Error.WriteLine($"Configuration file not found: {appSettingsPath}");
    return 1;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(appSettingsPath, optional: false)
    .Build();

var options = configuration.GetSection("Client").Get<ClientOptions>();
if (options is null || string.IsNullOrWhiteSpace(options.ServiceUrl))
{
    Console.Error.WriteLine("Configuration error: 'Client:ServiceUrl' is missing in appsettings.json.");
    return 1;
}

if (options.PageSize <= 0)
{
    Console.Error.WriteLine("Configuration error: 'Client:PageSize' must be a positive integer.");
    return 1;
}

// Personal fields are masked wherever a Person is destructured (e.g. "{@Person}"
// in LoggingClientGateway), so the full Information-level trail in logs/ never
// contains real names or email addresses - only the console's Warning+ output
// and the rolling file are affected. See PersonDestructuringPolicy for what's
// masked and why it's structured this way instead of a by-name mask list.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Destructure.With<PersonDestructuringPolicy>()
    .CreateLogger();

try
{
    var services = new ServiceCollection();

    services.AddSingleton(options);
    services.AddLogging(builder => builder.AddSerilog(dispose: false));

    // A single ODataClient for the app's lifetime: it manages HttpClient
    // internally, and reusing one instance avoids the classic socket-exhaustion
    // mistake of new-ing HttpClient per request.
    services.AddSingleton(_ => new ODataClient(new ODataClientOptions
    {
        BaseUrl = options.ServiceUrl,
        JsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        }
    }));

    // Gateway wrapped in a logging decorator (cross-cutting concern kept out
    // of the data-access code; composition happens here, in one place).
    services.AddSingleton<ClientGateway>();
    services.AddSingleton<IClientGateway>(sp => new LoggingClientGateway(
        sp.GetRequiredService<ClientGateway>(),
        sp.GetRequiredService<ILogger<LoggingClientGateway>>()));

    services.AddSingleton<IClientService, ClientService>();
    services.AddSingleton<ConsoleMenu>();

    await using var provider = services.BuildServiceProvider();

    // Ctrl+C -> graceful cancellation instead of a hard kill.
    var cts = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    Log.Information("People Explorer starting (service: {ServiceUrl}, page size: {PageSize})", options.ServiceUrl, options.PageSize);

    try
    {
        await provider.GetRequiredService<ConsoleMenu>().RunAsync(cts.Token);
        return 0;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine("Cancelled. Bye!");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
