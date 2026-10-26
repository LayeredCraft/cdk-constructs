using System.Text;
using Acme.Blazor.Components;

var builder = WebApplication.CreateBuilder(args);

var httpEndpoint = Environment.GetEnvironmentVariable("AWS_LAMBDA_HTTP_ENDPOINT");
if (!string.IsNullOrEmpty(httpEndpoint))
{
    builder.WebHost.UseUrls($"http://{httpEndpoint}");

    // Static assets and Razor components resolve relative to the content root. The execve
    // shim starts the app from /var/task, so pin the content root to the app base directory
    // rather than the process working directory.
    builder.WebHost.UseContentRoot(AppContext.BaseDirectory);
}

// Static SSR only. Interactive Server / Auto render modes open a SignalR circuit over a
// WebSocket, which Web Functions do not support, so they are deliberately not registered.
builder.Services.AddRazorComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();

await app.StartAsync();
await LambdaRuntime.NotifyReadyAsync(app.Logger);
await app.WaitForShutdownAsync();

internal static class BuildId
{
    // The artifact build writes build-id.txt next to the apphost and the smoke test reads the value
    // back off the rendered page, so the deployed revision is observable from the outside.
    public static string Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("BUILD_ID");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var fromFile = Path.Join(AppContext.BaseDirectory, "build-id.txt");
        if (File.Exists(fromFile))
        {
            var value = File.ReadAllText(fromFile).Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return "dev";
    }
}

internal static class LambdaRuntime
{
    // The execve shim replaces the Node runtime, which also replaces the Lambda runtime
    // interface client that normally POSTs /ready once the port is up. The server has to
    // send that signal itself, or the first invocation fails with Runtime.InitTimeout.
    public static async Task NotifyReadyAsync(ILogger logger)
    {
        var runtimeApi = Environment.GetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API");
        if (string.IsNullOrEmpty(runtimeApi))
        {
            return;
        }

        using var client = new HttpClient { BaseAddress = new Uri($"http://{runtimeApi}") };

        // The runtime API is always up inside Lambda; retry briefly so a local smoke test
        // that starts its mock slightly later still observes the handshake.
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var response = await client.PostAsync("/2025-09-01/ready", content);
                response.EnsureSuccessStatusCode();
                logger.LogInformation("Reported readiness to the Lambda runtime API at {RuntimeApi}.", runtimeApi);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastFailure = ex;
                await Task.Delay(200);
            }
        }

        throw new InvalidOperationException($"Failed to report readiness to the Lambda runtime API at {runtimeApi}.", lastFailure);
    }
}
