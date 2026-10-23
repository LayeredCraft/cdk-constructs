using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// Creates an <c>AWS::Lambda::WebFunction</c>, its first revision, execution role, logging, and default endpoint.
/// Additional endpoints, resource-policy ownership, and invoke grants are added by later releases.
/// </summary>
public sealed class WebFunctionConstruct : Construct, IGrantable
{
    private readonly CfnWebFunction _function;
    private readonly CfnWebFunctionRevision _revision;

    /// <summary>Creates a Web Function.</summary>
    /// <param name="scope">The construct scope.</param>
    /// <param name="id">The construct id.</param>
    /// <param name="props">The Web Function configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="props"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a literal value is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a numeric literal is out of range.</exception>
    public WebFunctionConstruct(Construct scope, string id, IWebFunctionConstructProps props)
        : base(scope, id)
    {
        ArgumentNullException.ThrowIfNull(props);

        WebFunctionValidation.ValidateFunctionName(props.FunctionName);
        WebFunctionValidation.ValidateTimeout(props.Timeout);
        WebFunctionValidation.ValidateMaxConcurrencyPerEnvironment(props.MaxConcurrencyPerEnvironment);
        WebFunctionValidation.ValidateEnvironmentVariables(props.EnvironmentVariables);
        WebFunctionValidation.ValidateEntryPoint(props.EntryPoint, props.EnvironmentVariables);

        FunctionName = props.FunctionName;

        _function = new CfnWebFunction(this, "Function", new CfnWebFunctionProps
        {
            FunctionName = props.FunctionName,
        });
        Node.DefaultChild = _function;

        Role = props.Role ?? CreateExecutionRole();

        var (logGroup, logGroupName, logGroupCreated) = ResolveLogs(props);

        var codeConfig = props.Code.Bind(this);

        _revision = new CfnWebFunctionRevision(this, "Revision", new CfnWebFunctionRevisionProps
        {
            FunctionName = props.FunctionName,
            BuildConfig = new CfnWebFunctionRevision.BuildConfigProperty
            {
                CodeConfig = new CfnWebFunctionRevision.CodeConfigProperty
                {
                    S3Object = new CfnWebFunctionRevision.S3ObjectProperty
                    {
                        Bucket = codeConfig.Bucket,
                        Key = codeConfig.Key,
                        VersionId = codeConfig.VersionId,
                    },
                },
                RuntimeConfig = new CfnWebFunctionRevision.RuntimeConfigProperty
                {
                    Runtime = props.Runtime.Name,
                },
            },
            ServiceConfig = new CfnWebFunctionRevision.ServiceConfigProperty
            {
                ExecutionRoleArn = Role.RoleArn,
                EnvironmentVariables = BuildEnvironmentVariables(props),
                TimeoutSeconds = props.Timeout.ToSeconds(),
                MaxConcurrencyPerEnvironment = props.MaxConcurrencyPerEnvironment,
                TelemetryConfig = BuildTelemetryConfig(props, logGroupName),
            },
            Description = props.Description,
        });

        _revision.AddResourceDependency(_function, "The revision belongs to the Web Function.");
        _revision.Node.AddDependency(Role);
        if (logGroupCreated && logGroup is not null)
            _revision.Node.AddDependency(logGroup);
        if (codeConfig.PolicyDependable is not null)
            _revision.Node.AddDependency(codeConfig.PolicyDependable);

        LogGroup = logGroup;

        DefaultEndpoint = new WebFunctionEndpointConstruct(this, "DefaultEndpoint", this, props.Endpoint);
        DefaultEndpoint.CfnEndpoint.AddResourceDependency(_revision, "The endpoint serves the revision.");
    }

    /// <summary>The Web Function name.</summary>
    public string FunctionName { get; }

    /// <summary>The Web Function ARN.</summary>
    public string FunctionArn => _function.AttrFunctionArn;

    /// <summary>The current revision id.</summary>
    public string RevisionId => _revision.AttrRevisionId;

    /// <summary>The current revision ARN.</summary>
    public string RevisionArn => _revision.AttrRevisionArn;

    /// <summary>The execution role, created or supplied.</summary>
    public IRole Role { get; }

    /// <inheritdoc />
    public IPrincipal GrantPrincipal => Role.GrantPrincipal;

    /// <summary>The log group, or null when logs use the service default.</summary>
    public ILogGroup? LogGroup { get; }

    /// <summary>The default endpoint.</summary>
    public WebFunctionEndpointConstruct DefaultEndpoint { get; }

    private IRole CreateExecutionRole()
    {
        var role = new Role(this, "ExecutionRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
        });
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole"));
        return role;
    }

    private (ILogGroup? LogGroup, string? LogGroupName, bool Created) ResolveLogs(IWebFunctionConstructProps props)
    {
        switch (props.Logs)
        {
            case WebFunctionLogsCreate create:
            {
                var group = new LogGroup(this, "LogGroup", new LogGroupProps
                {
                    LogGroupName = $"/aws/lambda/web/{props.FunctionName}",
                    Retention = create.Retention,
                    RemovalPolicy = create.RemovalPolicy,
                });
                return (group, group.LogGroupName, true);
            }
            case WebFunctionLogsUse use:
            {
                if (ShouldGrantWrite(use.LogGroup))
                    use.LogGroup.GrantWrite(Role);
                return (use.LogGroup, use.LogGroup.LogGroupName, false);
            }
            default:
                return (null, null, false);
        }
    }

    private static bool ShouldGrantWrite(ILogGroup logGroup)
    {
        var name = logGroup.LogGroupName;
        if (Token.IsUnresolved(name))
            return true;

        return !name.StartsWith("/aws/lambda/", StringComparison.Ordinal);
    }

    private static Dictionary<string, string>? BuildEnvironmentVariables(IWebFunctionConstructProps props)
    {
        var variables = new Dictionary<string, string>();

        if (props.EnvironmentVariables is not null)
        {
            foreach (var entry in props.EnvironmentVariables)
                variables[entry.Key] = entry.Value;
        }

        if (!string.IsNullOrEmpty(props.EntryPoint))
            variables[WebFunctionValidation.NodeJsEntryPointEnvironmentVariable] = props.EntryPoint;

        return variables.Count == 0 ? null : variables;
    }

    private static CfnWebFunctionRevision.TelemetryConfigProperty? BuildTelemetryConfig(
        IWebFunctionConstructProps props,
        string? logGroupName)
    {
        var hasLevel = props.ApplicationLogLevel.HasValue || props.SystemLogLevel.HasValue;
        if (logGroupName is null && !hasLevel)
            return null;

        return new CfnWebFunctionRevision.TelemetryConfigProperty
        {
            LoggingConfig = new CfnWebFunctionRevision.LoggingConfigProperty
            {
                LogGroup = logGroupName,
                ApplicationLogLevel = props.ApplicationLogLevel?.ToString(),
                SystemLogLevel = props.SystemLogLevel?.ToString(),
            },
        };
    }
}
