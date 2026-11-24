using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Compono;
using LayeredCraft.Cdk.Constructs.Models;
using ApplicationLogLevel = Amazon.CDK.AWS.Lambda.ApplicationLogLevel;
using SystemLogLevel = Amazon.CDK.AWS.Lambda.SystemLogLevel;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Profiles;

/// <summary>
/// Supplies deterministic <see cref="WebFunctionConstructProps"/> test data. The default code artifact is
/// an imported (externally managed) bucket so synthesis never requires AWS credentials or an owned bucket
/// resource. A real imported bucket is used (rather than a test double) so the props marshal through jsii
/// when the construct is synthesized. The two incidental log levels the previous customization left unset
/// are generated from the composition context.
/// </summary>
public sealed class WebFunctionConstructPropsProfile : ICompositionProfile
{
    // Compono does not provide this because composition populates constructor parameters and required
    // members only; the behavior-defining members (runtime, environment, timeout, logs, ...) are optional
    // (non-required) properties, so a registration factory supplies the whole value.
    public void Configure(CompositionBuilder builder) =>
        builder.Register(context =>
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
                // Incidental values, generated deterministically from each enum's own valid values.
                ApplicationLogLevel = Pick<ApplicationLogLevel>(context),
                SystemLogLevel = Pick<SystemLogLevel>(context),
            };
        });

    private static TEnum Pick<TEnum>(ICompositionContext context)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        return values[(int)((uint)context.DeriveSeed() % (uint)values.Length)];
    }
}
