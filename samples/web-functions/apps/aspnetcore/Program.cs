using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// The Web Function runtime hands the process the address it must listen on. Outside
// Lambda the variable is absent and Kestrel keeps its default binding.
var httpEndpoint = Environment.GetEnvironmentVariable("AWS_LAMBDA_HTTP_ENDPOINT");
if (!string.IsNullOrEmpty(httpEndpoint))
{
    builder.WebHost.UseUrls($"http://{httpEndpoint}");
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var buildId = BuildId.Resolve();
var startedAt = DateTimeOffset.UtcNow;

var app = builder.Build();

// A trivial liveness route. Every route is only reachable through the IAM-protected
// endpoint, so this doubles as the "protected route" the sample recipe calls for.
app.MapGet("/health", () => Results.Json(new { status = "Ok", backend = "aspnetcore", buildId }));

// The dynamically generated response the sample exists to demonstrate: values computed
// per request rather than baked in at build time.
app.MapGet("/status", () => Results.Json(new
{
    status = "Ok",
    backend = "aspnetcore",
    buildId,
    requestTimeUtc = DateTimeOffset.UtcNow,
    uptimeSeconds = Math.Round((DateTimeOffset.UtcNow - startedAt).TotalSeconds, 3),
}));

await app.StartAsync();
await LambdaRuntime.NotifyReadyAsync(app.Logger);
await app.WaitForShutdownAsync();

internal static class BuildId
{
    public static string Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("BUILD_ID");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        // The artifact build writes build-id.txt next to the apphost.
        var fromFile = Path.Combine(AppContext.BaseDirectory, "build-id.txt");
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
