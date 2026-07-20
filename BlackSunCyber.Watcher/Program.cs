using BlackSunCyber.Watcher;

var builder = Host.CreateApplicationBuilder(args);

// Rulare ca Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "BlackSunCyber Watcher";
});

// Incarcare configuratie din watchersettings.json
builder.Configuration.AddJsonFile("watchersettings.json", optional: false, reloadOnChange: true);
builder.Services.Configure<WatcherSettings>(builder.Configuration);

// Inregistrare serviciu background
builder.Services.AddHttpClient();
builder.Services.AddHostedService<WatcherService>();

var host = builder.Build();
host.Run();
