using System;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Constructs;
using LayeredCraft.Cdk.Constructs.Extensions;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

public class LambdaFunctionConstruct : Construct
{
    /// <summary>
    /// The domain of the function URL if GenerateUrl is enabled, null otherwise.
    /// </summary>
    public readonly string? LiveAliasFunctionUrlDomain;
    
    /// <summary>
    /// The underlying Lambda function for advanced configuration.
    /// </summary>
    public readonly Function LambdaFunction;

    /// <summary>
    /// The published Lambda version backing the alias.
    /// </summary>
    public readonly IVersion Version;

    /// <summary>
    /// The Lambda alias pointed at the published version.
    /// </summary>
    public readonly IAlias Alias;

    public LambdaFunctionConstruct(Construct scope, string id, ILambdaFunctionConstructProps props) : base(scope, id)
    {
        var region = Stack.Of(this).Region;
        ValidatePermissionTargets(props.Permissions);
        var code = ResolveCode(props);

        var policy = new Policy(this, $"{id}-policy", new PolicyProps
        {
            PolicyName = props.PolicyName
        });

        var role = new Role(this, $"{id}-role", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            RoleName = props.RoleName
        });

        role.AttachInlinePolicy(policy);

        var logGroup = new LogGroup(this, $"{id}-log-group", new LogGroupProps
        {
            LogGroupName = $"/aws/lambda/{props.FunctionName}-{props.FunctionSuffix}",
            Retention = RetentionDays.TWO_WEEKS,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        policy.AddStatements(new PolicyStatement(new PolicyStatementProps
        {
            Actions =
            [
                "logs:CreateLogStream",
                "logs:PutLogEvents"
            ],
            Resources = [logGroup.LogGroupArn],
            Effect = Effect.ALLOW
        }));

        policy.AddStatements(props.PolicyStatements);

        LambdaFunction = new Function(this, $"{id}-function", new FunctionProps
        {
            FunctionName = $"{props.FunctionName}-{props.FunctionSuffix}",
            Runtime = Runtime.PROVIDED_AL2023,
            Handler = "bootstrap",
            Code = code,
            Role = role,
            MemorySize = props.MemorySize,
            Timeout = Duration.Seconds(props.TimeoutInSeconds),
            Environment = props.EnvironmentVariables,
            LogGroup = logGroup,
            Tracing = props.IncludeOtelLayer ? Tracing.ACTIVE : Tracing.DISABLED,
            Architecture = ResolveArchitecture(props.Architecture),
            CurrentVersionOptions = new VersionOptions
            {
                RemovalPolicy = RemovalPolicy.RETAIN
            }
        });
        if (props.IncludeOtelLayer)
        {
            LambdaFunction.AddLayers(LayerVersion.FromLayerVersionArn(this, "OTELLambdaLayer",
                $"arn:aws:lambda:{region}:901920570463:layer:aws-otel-collector-{props.Architecture}-ver-{props.OtelLayerVersion}:1"));
        }

        // ✅ Create a new version on every deployment
        var currentVersion = LambdaFunction.CurrentVersion;
        Version = currentVersion;

        if (props.EnableSnapStart)
        {
            if (LambdaFunction.Runtime.Family != RuntimeFamily.JAVA)
            {
                throw new NotSupportedException("SnapStart is only supported for Java runtimes. Set EnableSnapStart = false or use a Java runtime.");
            }

            var cfnFunction = (CfnFunction)LambdaFunction.Node.DefaultChild!;
            cfnFunction.AddPropertyOverride("SnapStart", new Dictionary<string, object>
            {
                ["ApplyOn"] = "PublishedVersions"
            });
        }

        var alias = new Alias(this, $"{id}-alias", new AliasProps
        {
            AliasName = props.AliasName,
            Version = currentVersion,
        });
        Alias = alias;

        AddPermissionsToAllTargets(
            baseId: $"{id}-permission",
            function: LambdaFunction,
            version: Version,
            alias: Alias,
            permissions: props.Permissions
        );

        if (props.GenerateUrl)
        {
            var functionUrl = alias.AddFunctionUrl(new FunctionUrlOptions
            {
                AuthType = FunctionUrlAuthType.NONE
            });

            LiveAliasFunctionUrlDomain = Fn.Select(2, Fn.Split("/", functionUrl.Url));
            
            _ = new CfnOutput(this, $"{id}-url-output", new CfnOutputProps
            {
                ExportName = Stack.Of(this).CreateExportName(id, "url-output"),
                Value = functionUrl.Url
            });
        }
        else
        {
            LiveAliasFunctionUrlDomain = null;
        }
    }
    
    private void AddPermissionsToAllTargets(string baseId, IFunction function, IVersion version, IAlias? alias, List<LambdaPermission> permissions)
    {
        foreach (var (perm, index) in permissions.Select((p, i) => (p, i)))
        {
            var permission = new Permission
            {
                Principal = new ServicePrincipal(perm.Principal),
                Action = perm.Action,
                EventSourceToken = perm.EventSourceToken,
                SourceArn = perm.SourceArn
            };

            if (perm.Targets.HasFlag(LambdaPermissionTargets.Function))
            {
                function.AddPermission($"{baseId}-fn-{index}", permission);
            }

            if (perm.Targets.HasFlag(LambdaPermissionTargets.Version))
            {
                version.AddPermission($"{baseId}-ver-{index}", permission);
            }

            if (alias is not null && perm.Targets.HasFlag(LambdaPermissionTargets.Alias))
            {
                alias.AddPermission($"{baseId}-alias-{index}", permission);
            }
        }
    }

    private static Code ResolveCode(ILambdaFunctionConstructProps props)
    {
        var assetPath = props.AssetPath;
        var code = props.Code;

        if (assetPath is not null && code is not null)
        {
            throw new ArgumentException(
                "Exactly one of AssetPath or Code must be set; both were provided.",
                nameof(props));
        }

        if (code is not null)
        {
            return code;
        }

        if (!string.IsNullOrWhiteSpace(assetPath))
        {
            return Code.FromAsset(assetPath);
        }

        throw new ArgumentException(
            "Exactly one of AssetPath or Code must be set; neither was provided.",
            nameof(props));
    }

    private static void ValidatePermissionTargets(List<LambdaPermission> permissions)
    {
        foreach (var (permission, index) in permissions.Select((p, i) => (p, i)))
        {
            if (permission.Targets == LambdaPermissionTargets.None)
            {
                throw new ArgumentException(
                    $"Permission at index {index} has no targets. Set LambdaPermission.Targets to one or more of Function, Version, or Alias.",
                    nameof(permissions));
            }
        }
    }

    private static Architecture ResolveArchitecture(string architecture)
    {
        return string.Equals(architecture, "arm64", StringComparison.OrdinalIgnoreCase)
            ? Architecture.ARM_64
            : Architecture.X86_64;
    }
}
