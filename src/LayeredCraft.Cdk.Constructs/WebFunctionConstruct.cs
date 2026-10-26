using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.JSII.Runtime.Deputy;
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
    private readonly List<WebFunctionEndpointConstruct> _endpoints = [];
    private readonly HashSet<string> _endpointNames = new(StringComparer.Ordinal);
    private PolicyDocument? _resourcePolicy;
    private readonly List<PolicyStatement> _resourcePolicyStatements = [];
    private Amazon.CDK.AWS.Lambda.CfnResourcePolicy? _resourcePolicyResource;
    private readonly string _companionRegion;
    private string? _centralizedLogGroupName;
    private bool _grantsCompanionWrite;
    private bool _hasNonHomeRegionEndpoint;
    private bool _companionCreateGroupGranted;

    private const string UnmanagedCompanionWarningId =
        "@layeredcraft/cdk-constructs:webFunctionUnmanagedCompanionLogGroup";

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
        Logs = props.Logs;
        _companionRegion = Lazy.UncachedString(
            new CompanionRegionProducer(this),
            new LazyStringValueOptions { DisplayHint = "companionLogGroupRegion" });

        var (logGroup, logGroupName, localLogGroup) = ResolveLogs(props);

        EmitUnmanagedCompanionWarning(props);

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
        if (Role.Node.DefaultChild is CfnResource roleCfn)
            _revision.Node.AddDependency(roleCfn);
        if (logGroup is not null && ShouldDependOn(logGroup))
            _revision.Node.AddDependency(logGroup);
        if (localLogGroup is not null && ShouldDependOn(localLogGroup))
            _revision.Node.AddDependency(localLogGroup);
        if (codeConfig.PolicyDependable is not null)
            _revision.Node.AddDependency(codeConfig.PolicyDependable);

        LogGroup = logGroup;
        LocalLogGroup = localLogGroup;

        _endpointNames.Add(props.Endpoint.EndpointName);
        DefaultEndpoint = CreateEndpoint("DefaultEndpoint", props.Endpoint);

        Endpoints = _endpoints.AsReadOnly();
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

    /// <summary>
    /// The companion log group named <c>{LogGroup}.local</c> that Lambda writes to in the stack Region, or
    /// null when this construct does not manage it. It is created by <see cref="WebFunctionLogs.Create"/> and
    /// supplied by <see cref="WebFunctionLogs.Use"/> when a companion is passed; it is null for
    /// <see cref="WebFunctionLogs.Use"/> without a companion and for <see cref="WebFunctionLogs.ServiceDefault"/>.
    /// </summary>
    public ILogGroup? LocalLogGroup { get; }

    /// <summary>The logging configuration chosen for this Web Function.</summary>
    internal WebFunctionLogs Logs { get; }

    /// <summary>The default endpoint.</summary>
    public WebFunctionEndpointConstruct DefaultEndpoint { get; }

    /// <summary>All endpoints owned by this Web Function, starting with <see cref="DefaultEndpoint"/>.</summary>
    public IReadOnlyList<WebFunctionEndpointConstruct> Endpoints { get; }

    /// <summary>
    /// Adds an endpoint. Every endpoint routes 100% of traffic to the current revision.
    /// </summary>
    /// <param name="id">The construct id for the new endpoint.</param>
    /// <param name="options">The endpoint configuration. Its name must be unique within the function.</param>
    /// <returns>The created endpoint.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when an endpoint with the same name already exists.</exception>
    public WebFunctionEndpointConstruct AddEndpoint(string id, IWebFunctionEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(options);

        WebFunctionValidation.ValidateEndpointChildId(id);

        if (!_endpointNames.Add(options.EndpointName))
        {
            throw new InvalidOperationException(
                $"An endpoint named '{options.EndpointName}' already exists on Web Function '{FunctionName}'.");
        }

        return CreateEndpoint(id, options);
    }

    private WebFunctionEndpointConstruct CreateEndpoint(string id, IWebFunctionEndpointOptions options)
    {
        var endpoint = new WebFunctionEndpointConstruct(this, id, this, options);
        endpoint.CfnEndpoint.AddResourceDependency(_revision, "The endpoint serves the revision.");
        _endpoints.Add(endpoint);

        if (options.EndpointType is WebFunctionEndpointType.PerRegion or WebFunctionEndpointType.MultiRegion)
        {
            _hasNonHomeRegionEndpoint = true;

            if (_grantsCompanionWrite && _centralizedLogGroupName is not null)
                GrantCompanionCreateGroup(_centralizedLogGroupName);
        }

        return endpoint;
    }

    /// <summary>Adds a statement to the function's single resource policy, creating it on first use.</summary>
    internal Amazon.CDK.AWS.Lambda.CfnResourcePolicy AddResourcePolicyStatement(PolicyStatement statement)
    {
        WebFunctionValidation.ValidateResourcePolicyStatement(statement);

        var stored = statement.Copy(null);
        stored.Freeze();

        var candidate = new PolicyDocument(new PolicyDocumentProps
        {
            Statements = [.. _resourcePolicyStatements, stored],
        });

        var errors = candidate.ValidateForResourcePolicy(null);
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                $"The resource policy for Web Function '{Node.Path}' is invalid: {string.Join("; ", errors)}");
        }

        _resourcePolicyStatements.Add(stored);

        _resourcePolicy ??= new PolicyDocument();
        _resourcePolicy.AddStatements(stored);

        _resourcePolicyResource ??= new Amazon.CDK.AWS.Lambda.CfnResourcePolicy(
            this,
            "ResourcePolicy",
            new Amazon.CDK.AWS.Lambda.CfnResourcePolicyProps
            {
                ResourceArn = _function.AttrFunctionArn,
                PolicyDocument = _resourcePolicy,
            });

        return _resourcePolicyResource;
    }

    private IRole CreateExecutionRole()
    {
        var role = new Role(this, "ExecutionRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
        });
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole"));
        return role;
    }

    private (ILogGroup? LogGroup, string? LogGroupName, ILogGroup? LocalLogGroup) ResolveLogs(
        IWebFunctionConstructProps props)
    {
        switch (props.Logs)
        {
            case WebFunctionLogsCreate create:
            {
                var centralizedName = $"/aws/lambda/web/{props.FunctionName}";

                var group = new LogGroup(this, "LogGroup", new LogGroupProps
                {
                    LogGroupName = centralizedName,
                    Retention = create.Retention,
                    RemovalPolicy = create.RemovalPolicy,
                });

                var companion = new LogGroup(this, "LocalLogGroup", new LogGroupProps
                {
                    LogGroupName = $"{centralizedName}.local",
                    Retention = create.CompanionRetention,
                    RemovalPolicy = create.RemovalPolicy,
                });

                _centralizedLogGroupName = centralizedName;
                return (group, group.LogGroupName, companion);
            }
            case WebFunctionLogsUse use:
            {
                _centralizedLogGroupName = use.LogGroup.LogGroupName;

                if (ShouldGrantWrite(use.LogGroup))
                {
                    _grantsCompanionWrite = true;
                    use.LogGroup.GrantWrite(Role);
                    GrantCompanionWrite(use.LogGroup.LogGroupName);

                    if (use.Companion is null)
                        GrantCompanionCreateGroup(use.LogGroup.LogGroupName);
                }

                return (use.LogGroup, use.LogGroup.LogGroupName, use.Companion);
            }
            default:
                _centralizedLogGroupName = $"/aws/lambda/web/{props.FunctionName}";
                return (null, null, null);
        }
    }

    private bool ShouldDependOn(ILogGroup logGroup)
        => logGroup is Construct construct
           && construct.Node.DefaultChild is CfnResource
           && Stack.Of(construct) == Stack.Of(this);

    private void GrantCompanionWrite(string centralizedLogGroupName)
        => Grant.AddToPrincipal(new GrantOnPrincipalOptions
        {
            Grantee = Role,
            Actions = ["logs:CreateLogStream", "logs:PutLogEvents"],
            ResourceArns = [FormatCompanionArn(centralizedLogGroupName, ":log-stream:*")],
        });

    private void GrantCompanionCreateGroup(string centralizedLogGroupName)
    {
        if (_companionCreateGroupGranted)
            return;

        _companionCreateGroupGranted = true;

        Grant.AddToPrincipal(new GrantOnPrincipalOptions
        {
            Grantee = Role,
            Actions = ["logs:CreateLogGroup"],
            ResourceArns = [FormatCompanionArn(centralizedLogGroupName, string.Empty)],
        });
    }

    private string FormatCompanionArn(string centralizedLogGroupName, string suffix)
        => Stack.Of(this).FormatArn(new ArnComponents
        {
            Service = "logs",
            Region = _companionRegion,
            Resource = "log-group",
            ResourceName = $"{centralizedLogGroupName}.local{suffix}",
            ArnFormat = ArnFormat.COLON_RESOURCE_NAME,
        });

    private void EmitUnmanagedCompanionWarning(IWebFunctionConstructProps props)
    {
        if (props.Logs is not WebFunctionLogsUse { Companion: null })
            return;

        Annotations.Of(this).AddWarningV2(UnmanagedCompanionWarningId, BuildUnmanagedCompanionMessage());
    }

    private string BuildUnmanagedCompanionMessage()
    {
        if (Token.IsUnresolved(FunctionName)
            || _centralizedLogGroupName is null
            || Token.IsUnresolved(_centralizedLogGroupName))
        {
            return "This Web Function writes to a log group whose companion log group is not managed by this " +
                   "construct. Lambda creates it on first request with no expiry unless it already exists. Pass " +
                   "the companion to WebFunctionLogs.Use(logGroup, companion) or set its retention separately.";
        }

        return $"Web Function '{FunctionName}' writes to '{_centralizedLogGroupName}', but its companion log " +
               $"group '{_centralizedLogGroupName}.local' is not managed by this construct. Lambda creates it on " +
               "first request with no expiry unless it already exists. Pass the companion to " +
               "WebFunctionLogs.Use(logGroup, companion) or set its retention separately.";
    }

    internal string DescribeCompanionLogGroup()
        => _centralizedLogGroupName is not null && !Token.IsUnresolved(_centralizedLogGroupName)
            ? $"the companion log group '{_centralizedLogGroupName}.local'"
            : "a per-Region companion log group";

    private sealed class CompanionRegionProducer : DeputyBase, IStringProducer
    {
        private readonly WebFunctionConstruct _construct;

        internal CompanionRegionProducer(WebFunctionConstruct construct)
            : base(new DeputyProps(Array.Empty<object>()))
            => _construct = construct;

        public string Produce(IResolveContext context)
            => _construct._hasNonHomeRegionEndpoint ? "*" : Stack.Of(_construct).Region;
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
