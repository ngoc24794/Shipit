using Shipit.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddShipit();

var app = builder.Build();
await app.PrepareDatabaseAsync();
app.MapShipit();
app.Run();

// Required so WebApplicationFactory<Program> can see the entry point from test projects.
public partial class Program;
