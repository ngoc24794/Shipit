var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")) &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
    !args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase)))
{
    builder.WebHost.UseUrls("http://localhost:8080");
}

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    app = "hello-shipit",
    version = Environment.GetEnvironmentVariable("APP_VERSION") is { Length: > 0 } v ? v : "dev",
    time = DateTimeOffset.UtcNow.ToString("o")
}));

app.MapGet("/healthz", () => Results.Text("ok"));

app.Run();
