using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Constructs;

public class LambdaFunctionConstruct : Construct
{
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
                    Resources = [$"arn:aws:logs:{region}:{account}:log-group:/aws/lambda/{props.FunctionName}*:*"],
                    Effect = Effect.ALLOW
                }),
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions =
                    [
                        "logs:PutLogEvents"
                    ],
                    Resources =
                        [$"arn:aws:logs:{region}:{account}:log-group:/aws/lambda/{props.FunctionName}*:*:*"],
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

        var lambda = new Function(this, $"{id}-function", new FunctionProps
        {
            FunctionName = $"{props.FunctionName}-{props.FunctionSuffix}",
            Runtime = Runtime.PROVIDED_AL2023,
            Handler = "bootstrap",
            Code = Code.FromAsset(props.AssetPath),
            Role = role,
            MemorySize = 1024,
            Timeout = Duration.Seconds(6),
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
            lambda.AddLayers(LayerVersion.FromLayerVersionArn(this, "OTELLambdaLayer",
                $"arn:aws:lambda:{region}:901920570463:layer:aws-otel-collector-amd64-ver-0-102-1:1"));
        }

        // ✅ Create a new version on every deployment
        var currentVersion = lambda.CurrentVersion;

        var alias = new Alias(this, $"{id}-alias", new AliasProps
        {
            AliasName = "live",
            Version = currentVersion,
        });
        AddPermissionsToAllTargets(
            baseId: $"{id}-permission",
            function: lambda,
            version: lambda.CurrentVersion,
            alias: alias,
            permissions: props.Permissions
        );
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