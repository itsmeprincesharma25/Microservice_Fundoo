using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

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
        <tr><td><code>/api/auth/*</code>, <code>/api/users/*</code></td><td>Fundoo.User.Api (<code>http://localhost:5249</code>)</td><td><a href="http://localhost:5249/swagger" target="_blank">User Swagger</a></td></tr>
        <tr><td><code>/api/notes/*</code>, <code>/api/labels/*</code></td><td>Fundoo.Notes.Api (<code>http://localhost:5023</code>)</td><td><a href="http://localhost:5023/swagger" target="_blank">Notes Swagger</a></td></tr>
        <tr><td><code>/api/notifications/*</code></td><td>Fundoo.Notification.Api (<code>http://localhost:5003</code>)</td><td><a href="http://localhost:5003/swagger" target="_blank">Notification Swagger</a></td></tr>
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

app.MapReverseProxy();

app.UseWhen(
    context => context.GetEndpoint() == null,
    branch => branch.UseOcelot().GetAwaiter().GetResult());

app.Run();
