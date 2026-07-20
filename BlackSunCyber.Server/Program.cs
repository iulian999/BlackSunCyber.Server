using BlackSunCyber.Server.BackgroundWork;
using BlackSunCyber.Server.Hubs;
using BlackSunCyber.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------
// Rulare ca Windows Service: dacă executabilul e pornit ca serviciu
// (instalat cu `sc.exe` sau prin installer), folosește acest host.
// Dacă e rulat normal (debug din Visual Studio), funcționează la fel
// ca o aplicație de consolă obișnuită — nu trebuie cod separat.
// ------------------------------------------------------------
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "BlackSunCyber Server";
});

// ------------------------------------------------------------
// Validăm din start că appsettings.json are cheile Supabase completate,
// ca să primim o eroare clară la pornire, nu un crash confuz mai târziu.
// ------------------------------------------------------------
_ = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Lipsește Supabase:Url din appsettings.json");
_ = builder.Configuration["Supabase:SecretKey"]
    ?? throw new InvalidOperationException("Lipsește Supabase:SecretKey din appsettings.json");

builder.Services.AddHttpClient();
builder.Services.AddSingleton<SupabaseRestClient>();
builder.Services.AddScoped<StationService>();
builder.Services.AddHostedService<CountdownBackgroundService>();

builder.Services.AddSignalR();
builder.Services.AddControllers();

// CORS permisiv pe rețeaua locală — telefoanele clienților/admin se conectează
// din IP-uri diferite în Wi-Fi-ul clubului.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();   // servește automat index.html
app.UseStaticFiles();    // servește tot ce e în wwwroot/ (site-ul admin + client)

app.MapControllers();
app.MapHub<StationHub>("/hub/station");

app.Run();