using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Fluent builder for <see cref="WebFunctionConstructProps"/> used by tests. It mirrors the repository's
/// builder convention (see <c>CognitoUserPoolConstructPropsBuilder</c>, <c>StaticSiteConstructPropsBuilder</c>):
/// private fields, <c>With*</c> methods that return <c>this</c>, and a <see cref="Build"/> method that returns
/// the init-only record. The builder never selects an authentication mode for you.
/// </summary>
public class WebFunctionConstructPropsBuilder
{
    private const string DefaultArtifactBucketName = "test-web-function-artifacts";
    private const string ArtifactBucketConstructId = "WebFunctionTestArtifacts";

    private string _functionName = "test-web-function";
    private WebFunctionCode? _code;
    private IWebFunctionEndpointOptions? _endpoint;
    private readonly WebFunctionRuntime _runtime = WebFunctionRuntime.NodeJs24X;
    private string? _entryPoint;
    private readonly Dictionary<string, string> _environmentVariables = new();
    private Duration _timeout = Duration.Seconds(30);
    private int? _maxConcurrencyPerEnvironment;
    private string? _description;
    private IRole? _role;
    private WebFunctionLogs _logs = WebFunctionLogs.Create();

    /// <summary>Sets the function name (default: <c>"test-web-function"</c>).</summary>
    public WebFunctionConstructPropsBuilder WithFunctionName(string functionName)
    {
        _functionName = functionName;
        return this;
    }

    /// <summary>Sets the deployment code.</summary>
    public WebFunctionConstructPropsBuilder WithCode(WebFunctionCode code)
    {
        _code = code;
        return this;
    }

    /// <summary>
    /// Imports the bucket <c>"test-web-function-artifacts"</c> in <paramref name="scope"/> and uses
    /// <c>FromBucket(bucket, key, ExternallyManaged)</c>, so the template synthesizes without AWS credentials
    /// or an owned bucket resource. The imported bucket is reused when it already exists in the scope, so
    /// several Web Functions can be configured in the same stack.
    /// </summary>
    public WebFunctionConstructPropsBuilder WithTestArtifact(Construct scope, string key = "artifacts/app.zip")
    {
        ArgumentNullException.ThrowIfNull(scope);

        var bucket = scope.Node.TryFindChild(ArtifactBucketConstructId) as IBucket
            ?? Bucket.FromBucketName(scope, ArtifactBucketConstructId, DefaultArtifactBucketName);

        _code = WebFunctionCode.FromBucket(bucket, key, new WebFunctionCodeFromBucketOptions
        {
            BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
        });

        return this;
    }

    /// <summary>Uses an IAM-authenticated default endpoint.</summary>
    public WebFunctionConstructPropsBuilder WithIamAuthEndpoint() =>
        WithEndpoint(new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth });

    /// <summary>Uses a publicly reachable (application-managed) default endpoint.</summary>
    public WebFunctionConstructPropsBuilder WithApplicationManagedEndpoint() =>
        WithEndpoint(new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.ApplicationManaged });

    /// <summary>Sets the default endpoint options. Callers customize an options record with a <c>with</c> expression.</summary>
    public WebFunctionConstructPropsBuilder WithEndpoint(IWebFunctionEndpointOptions endpoint)
    {
        _endpoint = endpoint;
        return this;
    }

    /// <summary>Adds or replaces an environment variable.</summary>
    public WebFunctionConstructPropsBuilder WithEnvironmentVariable(string key, string value)
    {
        _environmentVariables[key] = value;
        return this;
    }

    /// <summary>Sets the Node.js entry point.</summary>
    public WebFunctionConstructPropsBuilder WithEntryPoint(string entryPoint)
    {
        _entryPoint = entryPoint;
        return this;
    }

    /// <summary>Sets the function timeout.</summary>
    public WebFunctionConstructPropsBuilder WithTimeout(Duration timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>Sets the maximum number of concurrent environments.</summary>
    public WebFunctionConstructPropsBuilder WithMaxConcurrencyPerEnvironment(int maxConcurrency)
    {
        _maxConcurrencyPerEnvironment = maxConcurrency;
        return this;
    }

    /// <summary>Sets the function description.</summary>
    public WebFunctionConstructPropsBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>Uses a supplied execution role instead of the created default role.</summary>
    public WebFunctionConstructPropsBuilder WithRole(IRole role)
    {
        _role = role;
        return this;
    }

    /// <summary>Sets the logging configuration.</summary>
    public WebFunctionConstructPropsBuilder WithLogs(WebFunctionLogs logs)
    {
        _logs = logs;
        return this;
    }

    /// <summary>
    /// Builds the props. Throws <see cref="InvalidOperationException"/> when code or the endpoint (and therefore
    /// the authentication mode) was never chosen: the builder never picks an auth mode. Performs no other
    /// validation; the construct validates the rest.
    /// </summary>
    public WebFunctionConstructProps Build()
    {
        if (_code is null)
            throw new InvalidOperationException("Web Function code was not configured. Call WithCode or WithTestArtifact.");

        if (_endpoint is null)
            throw new InvalidOperationException("A Web Function endpoint was not configured. Call WithEndpoint, WithIamAuthEndpoint, or WithApplicationManagedEndpoint.");

        return new WebFunctionConstructProps
        {
            FunctionName = _functionName,
            Code = _code,
            Endpoint = _endpoint,
            Runtime = _runtime,
            EntryPoint = _entryPoint,
            EnvironmentVariables = new Dictionary<string, string>(_environmentVariables),
            Timeout = _timeout,
            MaxConcurrencyPerEnvironment = _maxConcurrencyPerEnvironment,
            Description = _description,
            Role = _role,
            Logs = _logs,
        };
    }
}
