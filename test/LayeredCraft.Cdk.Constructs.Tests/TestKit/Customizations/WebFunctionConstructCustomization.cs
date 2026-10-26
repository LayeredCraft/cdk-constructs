using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

/// <summary>
/// Configures AutoFixture to build deterministic <see cref="WebFunctionConstructProps"/> instances.
/// The default code artifact is an imported (externally managed) bucket so synthesis never requires
/// AWS credentials or an owned bucket resource. A real imported bucket is used (rather than a test
/// double) so the props marshal through jsii when the construct is synthesized.
/// </summary>
public class WebFunctionConstructCustomization : ICustomization
{
    public void Customize(IFixture fixture)
    {
        var app = new App();
        var fixtureStack = new Stack(app, "WebFunctionFixtureStack");
        var artifacts = Bucket.FromBucketName(fixtureStack, "Artifacts", "test-web-function-artifacts");

        fixture.Customize<WebFunctionConstructProps>(transform => transform
            .With(props => props.FunctionName, "test-web-function")
            .With(props => props.Code, WebFunctionCode.FromBucket(
                artifacts,
                "artifacts/app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }))
            .With(props => props.Endpoint, new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth })
            .With(props => props.Runtime, WebFunctionRuntime.NodeJs24X)
            .With(props => props.EntryPoint, (string?)null)
            .With(props => props.EnvironmentVariables, new Dictionary<string, string> { { "TEST_VAR", "test-value" } })
            .With(props => props.Timeout, Duration.Seconds(30))
            .With(props => props.MaxConcurrencyPerEnvironment, (int?)null)
            .With(props => props.Description, (string?)null)
            .With(props => props.Role, (IRole?)null)
            .With(props => props.Logs, WebFunctionLogs.Create()));
    }
}
