using Shipit.Core;

var builder = WebApplication.CreateBuilder(args);

// Env vars are part of the default configuration; ShipitOptions reads them by their exact names.
var options = ShipitOptions.FromLookup(name => builder.Configuration[name]);
builder.Services.AddSingleton(options);

var app = builder.Build();

app.MapGet("/healthz", () => Results.Text("ok"));

app.Run();

// Required so WebApplicationFactory<Program> can see the entry point from test projects.
public partial class Program;
