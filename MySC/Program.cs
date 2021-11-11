using MySC;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>().AddLogging(builder => {
            builder.AddEventLog(configure => {
                configure.SourceName = nameof(MySC);
            });
        });
    })
    .Build();

await host.RunAsync();
