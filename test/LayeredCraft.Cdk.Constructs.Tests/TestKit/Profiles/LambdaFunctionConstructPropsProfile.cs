using Amazon.CDK.AWS.IAM;
using Compono;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Profiles;

/// <summary>
/// Selects the optional pieces present in the composed <see cref="LambdaFunctionConstructProps"/>.
/// </summary>
[Flags]
public enum LambdaFunctionConstructPropsOptions
{
    None = 0,
    OtelLayer = 1 << 0,
    Permissions = 1 << 1,
    FunctionUrl = 1 << 2,
}

/// <summary>Profile configuration for <see cref="LambdaFunctionConstructPropsProfile"/>.</summary>
public sealed record LambdaFunctionConstructPropsConfig(LambdaFunctionConstructPropsOptions Options);

/// <summary>
/// Composes the deterministic, realistic <see cref="LambdaFunctionConstructProps"/> test data the
/// Lambda construct tests rely on.
/// </summary>
public sealed class LambdaFunctionConstructPropsProfile(LambdaFunctionConstructPropsConfig config) : ICompositionProfile
{
    public void Configure(CompositionBuilder builder) =>
        builder.Register(() => new LambdaFunctionConstructProps
        {
            FunctionName = "test-function",
            FunctionSuffix = "disney-trivia",
            AssetPath = "./TestAssets/test-lambda.zip",
            RoleName = "test-function-us-east-1-lambdaRole",
            PolicyName = "test-function-lambda",
            PolicyStatements =
            [
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions = ["dynamodb:Query", "dynamodb:GetItem", "dynamodb:PutItem"],
                    Resources = ["arn:aws:dynamodb:us-east-1:123456789012:table/test-table"],
                    Effect = Effect.ALLOW,
                }),
            ],
            EnvironmentVariables = new Dictionary<string, string> { { "TEST_VAR", "test-value" } },
            IncludeOtelLayer = config.Options.HasFlag(LambdaFunctionConstructPropsOptions.OtelLayer),
            OtelLayerVersion = "0-117-0",
            Architecture = "amd64",
            Permissions = config.Options.HasFlag(LambdaFunctionConstructPropsOptions.Permissions)
                ?
                [
                    new LambdaPermission
                    {
                        Principal = "alexa-appkit.amazon.com",
                        Action = "lambda:InvokeFunction",
                        EventSourceToken = "amzn1.ask.skill.test-skill-id",
                    },
                ]
                : [],
            EnableSnapStart = false,
            GenerateUrl = config.Options.HasFlag(LambdaFunctionConstructPropsOptions.FunctionUrl),
        });
}
