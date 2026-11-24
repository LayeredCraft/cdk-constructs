using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Compono;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Profiles;

/// <summary>
/// Composes deterministic <see cref="WebFunctionConstructProps"/> test data. The default code artifact
/// is an imported (externally managed) bucket so synthesis never requires AWS credentials or an owned
/// bucket resource. A real imported bucket is used (rather than a test double) so the props marshal
/// through jsii when the construct is synthesized.
/// </summary>
public sealed class WebFunctionConstructPropsProfile : ICompositionProfile
{
    public void Configure(CompositionBuilder builder) =>
        builder.Register(() =>
        {
            var app = new App();
            var fixtureStack = new Stack(app, "WebFunctionFixtureStack");
            var artifacts = Bucket.FromBucketName(fixtureStack, "Artifacts", "test-web-function-artifacts");

            return new WebFunctionConstructProps
            {
                FunctionName = "test-web-function",
                Code = WebFunctionCode.FromBucket(
                    artifacts,
                    "artifacts/app.zip",
                    new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
                Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
                Runtime = WebFunctionRuntime.NodeJs24X,
                EntryPoint = null,
                EnvironmentVariables = new Dictionary<string, string> { { "TEST_VAR", "test-value" } },
                Timeout = Duration.Seconds(30),
                MaxConcurrencyPerEnvironment = null,
                Description = null,
                Role = null,
                Logs = WebFunctionLogs.Create(),
            };
        });
}
