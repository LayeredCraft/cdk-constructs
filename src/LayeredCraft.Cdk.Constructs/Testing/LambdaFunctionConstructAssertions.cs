using Amazon.CDK.Assertions;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Extension methods for asserting LambdaFunctionConstruct resources in CDK templates.
/// These helpers simplify common testing scenarios for consumers of the library.
/// </summary>
public static class LambdaFunctionConstructAssertions
{
    /// <summary>
    /// Asserts that the template contains a Lambda function with the specified name.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="functionName">The expected function name</param>
    public static void ShouldHaveLambdaFunction(this Template template, string functionName)
    {
        template.HasResourceProperties("AWS::Lambda::Function", Match.ObjectLike(new Dictionary<string, object>
        {
            { "FunctionName", functionName }
        }));
    }

    /// <summary>
    /// Asserts that the template contains CloudWatch Logs permissions for the specified function.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="functionName">The function name to check permissions for</param>
    /// <param name="region">AWS region (default: us-east-1)</param>
    /// <param name="account">AWS account ID (default: 123456789012)</param>
    public static void ShouldHaveCloudWatchLogsPermissions(this Template template, string functionName, 
        string region = "us-east-1", string account = "123456789012")
    {
        template.HasResourceProperties("AWS::IAM::Policy", Match.ObjectLike(new Dictionary<string, object>
        {
            { "PolicyDocument", Match.ObjectLike(new Dictionary<string, object>
            {
                { "Statement", Match.ArrayWith([
                    // CloudWatch Logs permissions
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        { "Effect", "Allow" },
                        { "Action", Match.ArrayWith([
                            "logs:CreateLogStream",
                            "logs:CreateLogGroup", 
                            "logs:TagResource"
                        ]) },
                        { "Resource", $"arn:aws:logs:{region}:{account}:log-group:/aws/lambda/{functionName}*:*" }
                    }),
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        { "Effect", "Allow" },
                        { "Action", "logs:PutLogEvents" },
                        { "Resource", $"arn:aws:logs:{region}:{account}:log-group:/aws/lambda/{functionName}*:*:*" }
                    })
                ]) }
            }) }
        }));
    }

    /// <summary>
    /// Asserts that the template contains a Lambda function with OpenTelemetry layer and tracing enabled.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    public static void ShouldHaveOtelLayer(this Template template)
    {
        template.HasResourceProperties("AWS::Lambda::Function", Match.ObjectLike(new Dictionary<string, object>
        {
            { "TracingConfig", new Dictionary<string, object> { { "Mode", "Active" } } },
            { "Layers", new object[] { Match.StringLikeRegexp(".*aws-otel-collector.*") } }
        }));
    }

    /// <summary>
    /// Asserts that the template contains a Lambda function without OpenTelemetry layer or tracing.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    public static void ShouldNotHaveOtelLayer(this Template template)
    {
        // Verify TracingConfig is not present
        template.HasResourceProperties("AWS::Lambda::Function", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            { "TracingConfig", Match.AnyValue() }
        })));

        // Verify Layers property is not present
        template.HasResourceProperties("AWS::Lambda::Function", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            { "Layers", Match.AnyValue() }
        })));
    }

    /// <summary>
    /// Asserts that the template contains a Lambda function with the specified environment variables.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="environmentVariables">The expected environment variables</param>
    public static void ShouldHaveEnvironmentVariables(this Template template, IDictionary<string, string> environmentVariables)
    {
        template.HasResourceProperties("AWS::Lambda::Function", Match.ObjectLike(new Dictionary<string, object>
        {
            { "Environment", Match.ObjectLike(new Dictionary<string, object>
            {
                { "Variables", Match.ObjectLike(environmentVariables.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)) }
            }) }
        }));
    }

    /// <summary>
    /// Asserts that the template contains Lambda permissions for the specified count (function + version + alias).
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="permissionCount">Number of permission objects (will be multiplied by 3 for function, version, alias)</param>
    public static void ShouldHaveLambdaPermissions(this Template template, int permissionCount)
    {
        var expectedPermissionCount = permissionCount * 3; // function + version + alias
        template.ResourceCountIs("AWS::Lambda::Permission", expectedPermissionCount);
    }

    /// <summary>
    /// Asserts that the template contains a Lambda version and alias.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="aliasName">The expected alias name (default: live)</param>
    public static void ShouldHaveVersionAndAlias(this Template template, string aliasName = "live")
    {
        template.ResourceCountIs("AWS::Lambda::Version", 1);
        template.HasResourceProperties("AWS::Lambda::Alias", Match.ObjectLike(new Dictionary<string, object>
        {
            { "Name", aliasName }
        }));
    }

    /// <summary>
    /// Asserts that the template contains a CloudWatch Logs log group with the specified retention.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="functionName">The function name for the log group</param>
    /// <param name="retentionDays">Expected retention in days (default: 14)</param>
    public static void ShouldHaveLogGroup(this Template template, string functionName, int retentionDays = 14)
    {
        template.HasResourceProperties("AWS::Logs::LogGroup", Match.ObjectLike(new Dictionary<string, object>
        {
            { "LogGroupName", $"/aws/lambda/{functionName}" },
            { "RetentionInDays", retentionDays }
        }));
    }
}