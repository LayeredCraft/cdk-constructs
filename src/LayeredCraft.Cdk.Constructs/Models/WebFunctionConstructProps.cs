using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Configuration for a Web Function construct.
/// </summary>
public interface IWebFunctionConstructProps
{
    /// <summary>The Web Function name. Must be 1–64 characters matching the Lambda name rules.</summary>
    string FunctionName { get; }

    /// <summary>The S3 artifact the revision runs from.</summary>
    WebFunctionCode Code { get; }

    /// <summary>The default endpoint configuration. Authentication must be chosen explicitly.</summary>
    IWebFunctionEndpointOptions Endpoint { get; }

    /// <summary>The runtime. Defaults to <see cref="WebFunctionRuntime.NodeJs24X"/>.</summary>
    WebFunctionRuntime Runtime { get; }

    /// <summary>An optional entry point, exported to the revision as <c>AWS_LAMBDA_NODEJS_ENTRYPOINT</c>.</summary>
    string? EntryPoint { get; }

    /// <summary>Environment variables for the revision.</summary>
    IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    /// <summary>The per-request timeout. Defaults to 30 seconds; must be 3–900 whole seconds.</summary>
    Duration Timeout { get; }

    /// <summary>The maximum concurrent environments per Region. AWS default 64; must be 1–128 when set.</summary>
    int? MaxConcurrencyPerEnvironment { get; }

    /// <summary>The revision description.</summary>
    string? Description { get; }

    /// <summary>An existing execution role. When null, one is created.</summary>
    IRole? Role { get; }

    /// <summary>Where the revision's logs go. Defaults to <see cref="WebFunctionLogs.Create"/>.</summary>
    WebFunctionLogs Logs { get; }

    /// <summary>The application log level, or null to use the service default.</summary>
    ApplicationLogLevel? ApplicationLogLevel { get; }

    /// <summary>The system log level, or null to use the service default.</summary>
    SystemLogLevel? SystemLogLevel { get; }
}

/// <summary>
/// Configuration for a Web Function construct.
/// </summary>
public sealed record WebFunctionConstructProps : IWebFunctionConstructProps
{
    /// <inheritdoc />
    public required string FunctionName { get; init; }

    /// <inheritdoc />
    public required WebFunctionCode Code { get; init; }

    /// <inheritdoc />
    public required IWebFunctionEndpointOptions Endpoint { get; init; }

    /// <inheritdoc />
    public WebFunctionRuntime Runtime { get; init; } = WebFunctionRuntime.NodeJs24X;

    /// <inheritdoc />
    public string? EntryPoint { get; init; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();

    /// <inheritdoc />
    public Duration Timeout { get; init; } = Duration.Seconds(30);

    /// <inheritdoc />
    public int? MaxConcurrencyPerEnvironment { get; init; }

    /// <inheritdoc />
    public string? Description { get; init; }

    /// <inheritdoc />
    public IRole? Role { get; init; }

    /// <inheritdoc />
    public WebFunctionLogs Logs { get; init; } = WebFunctionLogs.Create();

    /// <inheritdoc />
    public ApplicationLogLevel? ApplicationLogLevel { get; init; }

    /// <inheritdoc />
    public SystemLogLevel? SystemLogLevel { get; init; }
}
