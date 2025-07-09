using Amazon.CDK.AWS.IAM;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Fluent builder for creating LambdaFunctionConstructProps instances in tests.
/// Provides sensible defaults and allows customization of specific properties.
/// </summary>
public class LambdaFunctionConstructPropsBuilder
{
    private string _functionName = "test-function";
    private string _functionSuffix = "test";
    private string _assetPath = "./test-lambda.zip";
    private string _roleName = "test-role";
    private string _policyName = "test-policy";
    private double _memorySize = 1024;
    private double _timeoutInSeconds = 6;
    private readonly List<PolicyStatement> _policyStatements = new();
    private readonly Dictionary<string, string> _environmentVariables = new();
    private bool _includeOtelLayer = true;
    private readonly List<LambdaPermission> _permissions = new();
    private bool _enableSnapStart = false;
    private bool _generateUrl = false;

    /// <summary>
    /// Sets the function name.
    /// </summary>
    /// <param name="functionName">The function name</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithFunctionName(string functionName)
    {
        _functionName = functionName;
        return this;
    }

    /// <summary>
    /// Sets the function suffix.
    /// </summary>
    /// <param name="functionSuffix">The function suffix</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithFunctionSuffix(string functionSuffix)
    {
        _functionSuffix = functionSuffix;
        return this;
    }

    /// <summary>
    /// Sets the asset path for the Lambda deployment package.
    /// </summary>
    /// <param name="assetPath">The asset path</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithAssetPath(string assetPath)
    {
        _assetPath = assetPath;
        return this;
    }

    /// <summary>
    /// Sets the IAM role name.
    /// </summary>
    /// <param name="roleName">The role name</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithRoleName(string roleName)
    {
        _roleName = roleName;
        return this;
    }

    /// <summary>
    /// Sets the IAM policy name.
    /// </summary>
    /// <param name="policyName">The policy name</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithPolicyName(string policyName)
    {
        _policyName = policyName;
        return this;
    }

    /// <summary>
    /// Enables or disables the OpenTelemetry layer.
    /// </summary>
    /// <param name="enabled">Whether to include the OTEL layer</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithOtelEnabled(bool enabled = true)
    {
        _includeOtelLayer = enabled;
        return this;
    }

    /// <summary>
    /// Adds DynamoDB access permissions for the specified table.
    /// </summary>
    /// <param name="tableName">The DynamoDB table name</param>
    /// <param name="region">AWS region (default: us-east-1)</param>
    /// <param name="account">AWS account ID (default: 123456789012)</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithDynamoDbAccess(string tableName, 
        string region = "us-east-1", string account = "123456789012")
    {
        _policyStatements.Add(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:Query", "dynamodb:UpdateItem", "dynamodb:DeleteItem"],
            Resources = [$"arn:aws:dynamodb:{region}:{account}:table/{tableName}"],
            Effect = Effect.ALLOW
        }));
        return this;
    }

    /// <summary>
    /// Adds S3 access permissions for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The S3 bucket name</param>
    /// <param name="actions">S3 actions to allow (default: common read/write actions)</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithS3Access(string bucketName, 
        string[]? actions = null)
    {
        actions ??= ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"];
        
        _policyStatements.Add(new PolicyStatement(new PolicyStatementProps
        {
            Actions = actions,
            Resources = [$"arn:aws:s3:::{bucketName}/*"],
            Effect = Effect.ALLOW
        }));
        return this;
    }

    /// <summary>
    /// Adds API Gateway invoke permission.
    /// </summary>
    /// <param name="apiArn">The API Gateway execution ARN</param>
    /// <param name="eventSourceToken">Optional event source token</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithApiGatewayPermission(string apiArn, 
        string? eventSourceToken = null)
    {
        _permissions.Add(new LambdaPermission
        {
            Principal = "apigateway.amazonaws.com",
            Action = "lambda:InvokeFunction",
            SourceArn = apiArn,
            EventSourceToken = eventSourceToken
        });
        return this;
    }

    /// <summary>
    /// Adds Alexa Skills Kit invoke permission.
    /// </summary>
    /// <param name="skillId">The Alexa skill ID</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithAlexaPermission(string skillId)
    {
        _permissions.Add(new LambdaPermission
        {
            Principal = "alexa-appkit.amazon.com",
            Action = "lambda:InvokeFunction",
            EventSourceToken = skillId
        });
        return this;
    }

    /// <summary>
    /// Adds a custom policy statement.
    /// </summary>
    /// <param name="policyStatement">The policy statement to add</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithCustomPolicy(PolicyStatement policyStatement)
    {
        _policyStatements.Add(policyStatement);
        return this;
    }

    /// <summary>
    /// Adds a custom Lambda permission.
    /// </summary>
    /// <param name="permission">The Lambda permission to add</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithCustomPermission(LambdaPermission permission)
    {
        _permissions.Add(permission);
        return this;
    }

    /// <summary>
    /// Adds an environment variable.
    /// </summary>
    /// <param name="key">The environment variable key</param>
    /// <param name="value">The environment variable value</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithEnvironmentVariable(string key, string value)
    {
        _environmentVariables[key] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple environment variables.
    /// </summary>
    /// <param name="environmentVariables">Dictionary of environment variables</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithEnvironmentVariables(IDictionary<string, string> environmentVariables)
    {
        foreach (var kvp in environmentVariables)
        {
            _environmentVariables[kvp.Key] = kvp.Value;
        }
        return this;
    }

    /// <summary>
    /// Enables or disables Lambda SnapStart.
    /// </summary>
    /// <param name="enabled">Whether to enable SnapStart</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithSnapStart(bool enabled = true)
    {
        _enableSnapStart = enabled;
        return this;
    }

    /// <summary>
    /// Sets the memory size for the Lambda function.
    /// </summary>
    /// <param name="memorySize">The memory size in MB</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithMemorySize(double memorySize)
    {
        _memorySize = memorySize;
        return this;
    }

    /// <summary>
    /// Sets the timeout for the Lambda function.
    /// </summary>
    /// <param name="timeoutInSeconds">The timeout in seconds</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithTimeoutInSeconds(double timeoutInSeconds)
    {
        _timeoutInSeconds = timeoutInSeconds;
        return this;
    }

    /// <summary>
    /// Enables or disables function URL generation for direct HTTP access.
    /// </summary>
    /// <param name="generateUrl">Whether to generate a function URL</param>
    /// <returns>The builder instance for method chaining</returns>
    public LambdaFunctionConstructPropsBuilder WithGenerateUrl(bool generateUrl = true)
    {
        _generateUrl = generateUrl;
        return this;
    }

    /// <summary>
    /// Builds the LambdaFunctionConstructProps instance.
    /// </summary>
    /// <returns>The configured LambdaFunctionConstructProps</returns>
    public LambdaFunctionConstructProps Build()
    {
        return new LambdaFunctionConstructProps
        {
            FunctionName = _functionName,
            FunctionSuffix = _functionSuffix,
            AssetPath = _assetPath,
            RoleName = _roleName,
            PolicyName = _policyName,
            MemorySize = _memorySize,
            TimeoutInSeconds = _timeoutInSeconds,
            PolicyStatements = [.. _policyStatements],
            EnvironmentVariables = _environmentVariables,
            IncludeOtelLayer = _includeOtelLayer,
            Permissions = _permissions,
            EnableSnapStart = _enableSnapStart,
            GenerateUrl = _generateUrl
        };
    }
}