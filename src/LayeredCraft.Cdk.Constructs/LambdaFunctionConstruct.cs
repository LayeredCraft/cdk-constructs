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
    public LambdaFunctionConstruct(Construct scope, string id, ILambdaFunctionConstructProps props) : base(scope, id)
    {
        var region = Stack.Of(this).Region;
        var account = Stack.Of(this).Account;
        var policy = new Policy(this, $"{id}-policy", new PolicyProps
        {
            PolicyName = props.PolicyName,
            Statements =
            [
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions =
                    [
                        "logs:CreateLogStream",
                        "logs:CreateLogGroup",
                        "logs:TagResource"
                    ],
                    Resources = [$"arn:aws:logs:{region}:{account}:log-group:/aws/lambda/{props.FunctionName}-{props.FunctionSuffix}*:*"],
                    Effect = Effect.ALLOW
                }),
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions =
                    [
                        "logs:PutLogEvents"
                    ],
                    Resources =
                        [$"arn:aws:logs:{region}:{account}:log-group:/aws/lambda/{props.FunctionName}-{props.FunctionSuffix}*:*:*"],
                    Effect = Effect.ALLOW
                })
            ]
        });

        policy.AddStatements(props.PolicyStatements);

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

        LambdaFunction = new Function(this, $"{id}-function", new FunctionProps
        {
            FunctionName = $"{props.FunctionName}-{props.FunctionSuffix}",
            Runtime = Runtime.PROVIDED_AL2023,
            Handler = "bootstrap",
            Code = Code.FromAsset(props.AssetPath),
            Role = role,
            MemorySize = props.MemorySize,
            Timeout = Duration.Seconds(props.TimeoutInSeconds),
            Environment = props.EnvironmentVariables,
            LogGroup = logGroup,
            Tracing = props.IncludeOtelLayer ? Tracing.ACTIVE : Tracing.DISABLED,
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

        if (props.EnableSnapStart)
        {
            var cfnFunction = (CfnFunction)LambdaFunction.Node.DefaultChild!;
            cfnFunction.AddPropertyOverride("SnapStart", new Dictionary<string, object>
            {
                ["ApplyOn"] = "PublishedVersions"
            });
        }

        var alias = new Alias(this, $"{id}-alias", new AliasProps
        {
            AliasName = "live",
            Version = currentVersion,
        });
        
        AddPermissionsToAllTargets(
            baseId: $"{id}-permission",
            function: LambdaFunction,
            version: LambdaFunction.CurrentVersion,
            alias: alias,
            permissions: props.Permissions
        );

        if (props.GenerateUrl)
        {
            var functionUrl = alias.AddFunctionUrl(new FunctionUrlProps
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

            function.AddPermission($"{baseId}-fn-{index}", permission);
            version.AddPermission($"{baseId}-ver-{index}", permission);
            alias?.AddPermission($"{baseId}-alias-{index}", permission);
        }
    }
}