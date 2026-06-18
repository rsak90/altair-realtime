using SasJobRunner.Filters;
using SasJobRunner.Hubs;
using SasJobRunner.Services;
using Serilog;

// Configure Serilog for file logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/sasjobrunner-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting SAS Job Runner application");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog for logging
    builder.Host.UseSerilog();

    // ── Session ──────────────────────────────────────────────────────────────────
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromHours(8);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
    builder.Services.AddHttpContextAccessor();

// ── Typed HttpClient ──────────────────────────────────────────────────────────
builder.Services.AddHttpClient<ISlcHubClient, SlcHubClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["SlcHub:BaseUrl"] ?? "https://localhost:5001"));

// ── TokenManager HttpClient ───────────────────────────────────────────────────
builder.Services.AddHttpClient<ITokenManager, TokenManager>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["SlcHub:BaseUrl"] ?? "https://localhost:5001"));

// ── Scoped services ───────────────────────────────────────────────────────────
builder.Services.AddScoped<ISessionJobOrchestrator, SessionJobOrchestrator>();
builder.Services.AddScoped<IDatasetReaderService, DatasetReaderService>();
builder.Services.AddScoped<PreambleBuilder>();
builder.Services.AddScoped<LogParserService>();

// ── Singleton stores ──────────────────────────────────────────────────────────
builder.Services.AddSingleton<IMacroVarStore, MacroVarStore>();
builder.Services.AddSingleton<IMacroProgramStore, MacroProgramStore>();
builder.Services.AddSingleton<IProgramHistoryStore, ProgramHistoryStore>();

// ── SignalR ───────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ── MVC with global bearer filter ────────────────────────────────────────────
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add<BearerTokenRequiredFilter>());

var app = builder.Build();

// ── Configuration validation ──────────────────────────────────────────────────
var requiredKeys = new[]
{
    "SlcHub:ServiceAccount:Username",
    "SlcHub:ServiceAccount:Password",
    "SlcHub:UserId",
    "SlcHub:BaseUrl",
    "SlcHub:Namespace",
    "SlcHub:ExecutionProfile"
};

foreach (var key in requiredKeys)
{
    var value = app.Configuration[key];
    if (string.IsNullOrEmpty(value))
    {
        throw new InvalidOperationException(
            $"Required configuration key '{key}' is missing or empty. " +
            "Please ensure all required SlcHub configuration keys are set in appsettings.json.");
    }
}

// ── HTTP pipeline ─────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSession();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapHub<LogStreamingHub>("/hubs/log");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

    Log.Information("SAS Job Runner application started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make Program accessible for testing
public partial class Program { }
