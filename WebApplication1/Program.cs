using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddSwaggerForOcelot(builder.Configuration);

var app = builder.Build();

// SwaggerForOcelot UI must be registered BEFORE routing/endpoint middleware
app.UseSwaggerForOcelotUI(opt =>
{
    opt.PathToSwaggerGenerator = "/swagger/docs";
});

app.UseRouting();

app.MapGet("/", () => Results.Content("""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>Fundoo API Gateway</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; max-width: 780px; margin: 48px auto; padding: 0 20px; color: #1f2937; background: #f9fafb; }
    h1 { margin-bottom: 4px; }
    .sub { color: #6b7280; margin-bottom: 24px; }
    .card { background: #fff; border: 1px solid #e5e7eb; border-radius: 10px; padding: 20px; margin-bottom: 16px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { text-align: left; padding: 8px 6px; border-bottom: 1px solid #f3f4f6; font-size: 14px; }
    code { background: #f3f4f6; padding: 2px 6px; border-radius: 4px; }
    a { color: #2563eb; text-decoration: none; }
    a:hover { text-decoration: underline; }
  </style>
</head>
<body>
  <h1>Fundoo API Gateway</h1>
  <p class="sub">Ocelot &amp; YARP Reverse Proxy is running. Check <a href="/health">/health</a>.</p>
  <div class="card">
    <h3>Gateway Routes</h3>
    <table>
      <thead><tr><th>Gateway Path</th><th>Downstream Service</th><th>Swagger UI</th></tr></thead>
      <tbody>
        <tr><td><code>/api/auth/*</code>, <code>/api/users/*</code></td><td>Fundoo.User.Api</td><td><a href="/swagger" target="_blank">Swagger UI</a></td></tr>
        <tr><td><code>/api/v1/notes/*</code>, <code>/api/v2/notes/*</code>, <code>/api/labels/*</code></td><td>Fundoo.Notes.Api</td><td><a href="/swagger" target="_blank">Swagger UI</a></td></tr>
        <tr><td><code>/api/notifications/*</code></td><td>Fundoo.Notification.Api</td><td><a href="/swagger" target="_blank">Swagger UI</a></td></tr>
      </tbody>
    </table>
  </div>
</body>
</html>
""", "text/html"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "Fundoo API Gateway (Ocelot & YARP Enabled)"
}));

// YARP handles API routes (matched by route table in appsettings.json)
app.MapReverseProxy();

// Ocelot handles anything not matched by YARP routes or Swagger middleware
app.UseWhen(
    context => context.GetEndpoint() == null,
    branch => branch.UseOcelot().GetAwaiter().GetResult());

app.Run();
