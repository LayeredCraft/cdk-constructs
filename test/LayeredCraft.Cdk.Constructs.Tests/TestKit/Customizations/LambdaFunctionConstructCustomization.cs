using Amazon.CDK.AWS.IAM;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

public class LambdaFunctionConstructCustomization(bool includeOtelLayer = true, bool includePermissions = true, bool enableSnapStart = false)
    : ICustomization
{
    public void Customize(IFixture fixture)
    {
        fixture.Customize<LambdaPermission>(transform => transform
            .With(perm => perm.Principal, "alexa-appkit.amazon.com")
            .With(perm => perm.Action, "lambda:InvokeFunction")
            .With(perm => perm.EventSourceToken, "amzn1.ask.skill.test-skill-id"));

        fixture.Customize<LambdaFunctionConstructProps>(transform => transform
            .With(props => props.FunctionName, "test-function")
            .With(props => props.FunctionSuffix, "disney-trivia")
            .With(props => props.AssetPath, "./TestAssets/test-lambda.zip")
            .With(props => props.RoleName, "test-function-us-east-1-lambdaRole")
            .With(props => props.PolicyName, "test-function-lambda")
            .With(props => props.PolicyStatements, new PolicyStatement[]
            {
                new(new PolicyStatementProps
                {
                    Actions = ["dynamodb:Query", "dynamodb:GetItem", "dynamodb:PutItem"],
                    Resources = ["arn:aws:dynamodb:us-east-1:123456789012:table/test-table"],
                    Effect = Effect.ALLOW
                })
            })
            .With(props => props.EnvironmentVariables, new Dictionary<string, string>
            {
                { "TEST_VAR", "test-value" }
            })
            .With(props => props.IncludeOtelLayer, includeOtelLayer)
            .With(props => props.Permissions, includePermissions 
                ? [fixture.Create<LambdaPermission>()]
                : [])
            .With(props => props.EnableSnapStart, enableSnapStart));
    }
}