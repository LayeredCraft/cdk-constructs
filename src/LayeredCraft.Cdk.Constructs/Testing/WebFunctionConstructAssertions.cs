using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK.Assertions;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Fluent assertion helpers for synthesized Web Function CloudFormation templates.
/// Assertions match by function and endpoint <em>names</em> so they keep working as
/// logical IDs change, and they distinguish multiple endpoints on the same function.
/// </summary>
public static class WebFunctionConstructAssertions
{
    private const string WebFunctionType = "AWS::Lambda::WebFunction";
    private const string RevisionType = "AWS::Lambda::WebFunctionRevision";
    private const string EndpointType = "AWS::Lambda::WebFunctionEndpoint";
    private const string ResourcePolicyType = "AWS::Lambda::ResourcePolicy";
    private const string OriginAccessControlType = "AWS::CloudFront::OriginAccessControl";
    private const string BucketPolicyType = "AWS::S3::BucketPolicy";
    private const string LogGroupType = "AWS::Logs::LogGroup";
    private const string InvokeAction = "lambda:InvokeWebFunctionEndpoint";

    /// <summary>Asserts the template contains a Web Function with the given function name.</summary>
    public static void ShouldHaveWebFunction(this Template template, string functionName)
    {
        ArgumentNullException.ThrowIfNull(template);

        template.HasResourceProperties(WebFunctionType, Match.ObjectLike(new Dictionary<string, object>
        {
            ["FunctionName"] = functionName,
        }));
    }

    /// <summary>Asserts the template contains a revision for the function using the given runtime.</summary>
    public static void ShouldHaveRevision(this Template template, string functionName, string runtime = "nodejs24.x")
    {
        ArgumentNullException.ThrowIfNull(template);

        template.HasResourceProperties(RevisionType, Match.ObjectLike(new Dictionary<string, object>
        {
            ["FunctionName"] = functionName,
            ["BuildConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["RuntimeConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Runtime"] = runtime,
                }),
            }),
        }));
    }

    /// <summary>Asserts the function revision points at the given S3 object, optionally with a version.</summary>
    public static void ShouldHaveRevisionCode(this Template template, string bucketName, string key, string? versionId = null)
    {
        ArgumentNullException.ThrowIfNull(template);

        var s3Object = new Dictionary<string, object>
        {
            ["Bucket"] = bucketName,
            ["Key"] = key,
        };

        if (versionId is not null)
            s3Object["VersionId"] = versionId;

        template.HasResourceProperties(RevisionType, Match.ObjectLike(new Dictionary<string, object>
        {
            ["BuildConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["CodeConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["S3Object"] = Match.ObjectLike(s3Object),
                }),
            }),
        }));
    }

    /// <summary>Asserts the function exposes an endpoint with the given name, auth type and endpoint type.</summary>
    public static void ShouldHaveEndpoint(
        this Template template,
        string functionName,
        string endpointName,
        WebFunctionAuthType authType,
        WebFunctionEndpointType type = WebFunctionEndpointType.HomeRegion)
    {
        ArgumentNullException.ThrowIfNull(template);

        template.HasResourceProperties(EndpointType, Match.ObjectLike(new Dictionary<string, object>
        {
            ["FunctionName"] = functionName,
            ["EndpointName"] = endpointName,
            ["AuthType"] = authType.ToString(),
            ["EndpointType"] = type.ToString(),
        }));
    }

    /// <summary>
    /// Asserts the scaling and throttling configuration of an endpoint. A <c>null</c> value asserts the
    /// corresponding block is absent, which is how a <c>null</c> option is surfaced in the template.
    /// </summary>
    public static void ShouldHaveEndpointScaling(this Template template, string endpointName, int? maxEnvironments, int? rateLimit)
    {
        ArgumentNullException.ThrowIfNull(template);

        var matches = FindResourcesByProperty(template, EndpointType, "EndpointName", endpointName);
        if (matches.Count == 0)
            throw new InvalidOperationException($"No Web Function endpoint named '{endpointName}' was found.");

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Multiple Web Function endpoints named '{endpointName}' were found, so the scaling assertion is " +
                "ambiguous. Endpoint names are only unique per function; assert against a distinct endpoint name.");

        var properties = Properties(matches[0].Value);

        if (maxEnvironments is null)
        {
            if (properties.ContainsKey("ScalingConfig"))
                throw new InvalidOperationException($"The '{endpointName}' endpoint was not expected to declare ScalingConfig.");
        }
        else if (!properties.TryGetValue("ScalingConfig", out var scalingValue)
                 || scalingValue is not IDictionary<string, object> scaling
                 || !scaling.TryGetValue("MaxEnvironments", out var max)
                 || !ValueEquals(max, maxEnvironments.Value))
        {
            throw new InvalidOperationException($"The '{endpointName}' endpoint was expected to declare ScalingConfig.MaxEnvironments = {maxEnvironments}.");
        }

        if (rateLimit is null)
        {
            if (properties.ContainsKey("ThrottleConfig"))
                throw new InvalidOperationException($"The '{endpointName}' endpoint was not expected to declare ThrottleConfig.");
        }
        else if (!properties.TryGetValue("ThrottleConfig", out var throttleValue)
                 || throttleValue is not IDictionary<string, object> throttle
                 || !throttle.TryGetValue("RateLimit", out var rate)
                 || !ValueEquals(rate, rateLimit.Value))
        {
            throw new InvalidOperationException($"The '{endpointName}' endpoint was expected to declare ThrottleConfig.RateLimit = {rateLimit}.");
        }
    }

    /// <summary>
    /// Asserts an identity-based invoke grant (an IAM policy statement) exists for the endpoint, such as the
    /// grant produced by <c>GrantInvoke</c> for a same-account principal.
    /// </summary>
    public static void ShouldHaveInvokeGrant(this Template template, string functionName, string endpointName)
    {
        ArgumentNullException.ThrowIfNull(template);

        var endpointLogicalId = EndpointLogicalId(template, functionName, endpointName);

        foreach (var policy in template.FindResources("AWS::IAM::Policy").Values)
        {
            foreach (var statement in StatementsOf(policy))
            {
                if (StatementHasEffect(statement, "Allow")
                    && StatementHasAction(statement, InvokeAction)
                    && StatementTargetsEndpoint(statement, endpointLogicalId))
                    return;
            }
        }

        throw new InvalidOperationException($"No identity invoke grant for the '{endpointName}' endpoint was found.");
    }

    /// <summary>Asserts the function has exactly one resource policy, attached to that function.</summary>
    public static void ShouldHaveSingleResourcePolicy(this Template template, string functionName)
    {
        ArgumentNullException.ThrowIfNull(template);

        var policies = template.FindResources(ResourcePolicyType);
        if (policies.Count != 1)
            throw new InvalidOperationException($"Expected exactly one {ResourcePolicyType} but found {policies.Count}.");

        var functionLogicalId = FunctionLogicalId(template, functionName);
        var properties = Properties(policies.Values.Single());

        if (!properties.TryGetValue("ResourceArn", out var resourceArn) || !GetAttMatches(resourceArn, functionLogicalId, "FunctionArn"))
            throw new InvalidOperationException($"The resource policy is not attached to the '{functionName}' function.");
    }

    /// <summary>Asserts the template contains no resource policy at all.</summary>
    public static void ShouldHaveNoResourcePolicy(this Template template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var count = template.FindResources(ResourcePolicyType).Count;
        if (count != 0)
            throw new InvalidOperationException($"Expected no {ResourcePolicyType} but found {count}.");
    }

    /// <summary>Asserts a resource-policy statement allows CloudFront to invoke the endpoint (with an aws:SourceArn condition).</summary>
    public static void ShouldAllowCloudFront(this Template template, string functionName, string endpointName)
    {
        ArgumentNullException.ThrowIfNull(template);

        var endpointLogicalId = EndpointLogicalId(template, functionName, endpointName);

        foreach (var statement in ResourcePolicyStatements(template))
        {
            if (StatementHasEffect(statement, "Allow")
                && StatementHasAction(statement, InvokeAction)
                && StatementHasServicePrincipal(statement, "cloudfront.amazonaws.com")
                && StatementTargetsEndpoint(statement, endpointLogicalId)
                && StatementHasStringEquals(statement, "aws:SourceArn"))
                return;
        }

        throw new InvalidOperationException($"No CloudFront invoke statement for the '{endpointName}' endpoint was found.");
    }

    /// <summary>Asserts a resource-policy statement allows the given IAM principal to invoke the endpoint.</summary>
    public static void ShouldAllowResourcePolicyPrincipal(this Template template, string functionName, string endpointName, string principalArn)
    {
        ArgumentNullException.ThrowIfNull(template);

        var endpointLogicalId = EndpointLogicalId(template, functionName, endpointName);

        foreach (var statement in ResourcePolicyStatements(template))
        {
            if (StatementHasEffect(statement, "Allow")
                && StatementHasAction(statement, InvokeAction)
                && StatementHasAwsPrincipal(statement, principalArn)
                && StatementTargetsEndpoint(statement, endpointLogicalId))
                return;
        }

        throw new InvalidOperationException($"No resource-policy statement for principal '{principalArn}' on the '{endpointName}' endpoint was found.");
    }

    /// <summary>Asserts the template contains a Lambda function URL origin access control.</summary>
    public static void ShouldHaveWebFunctionOac(this Template template, string signingProtocol = "sigv4")
    {
        ArgumentNullException.ThrowIfNull(template);

        template.HasResourceProperties(OriginAccessControlType, Match.ObjectLike(new Dictionary<string, object>
        {
            ["OriginAccessControlConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["OriginAccessControlOriginType"] = "lambda",
                ["SigningBehavior"] = "always-amz-auth",
                ["SigningProtocol"] = signingProtocol,
            }),
        }));
    }

    /// <summary>
    /// Asserts the function's code bucket grants the Lambda service read access to the artifact. The statement must
    /// be scoped to this function through the <c>aws:SourceArn</c> condition
    /// (<c>web-function/{functionName}/revision/*</c>), so a policy for another function never satisfies it.
    /// </summary>
    public static void ShouldHaveCodeBucketReadStatement(this Template template, string functionName)
    {
        ArgumentNullException.ThrowIfNull(template);

        _ = FunctionLogicalId(template, functionName);

        var expectedSourceArn = $"web-function/{functionName}/revision/*";

        foreach (var policy in template.FindResources(BucketPolicyType).Values)
        {
            foreach (var statement in StatementsOf(policy))
            {
                if (StatementHasEffect(statement, "Allow")
                    && StatementHasAction(statement, "s3:GetObject")
                    && StatementHasServicePrincipal(statement, "lambda.amazonaws.com")
                    && StatementHasStringEquals(statement, "aws:SourceAccount")
                    && StatementSourceArnContains(statement, expectedSourceArn))
                    return;
            }
        }

        throw new InvalidOperationException(
            $"No code-bucket read statement scoped to the '{functionName}' function was found.");
    }

    /// <summary>
    /// Asserts the template contains a log group with the given literal name and retention. Token names cannot be
    /// asserted this way because they are not literal in the synthesized template.
    /// </summary>
    public static void ShouldHaveWebFunctionLogGroup(this Template template, string logGroupName, int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(template);

        var matches = FindResourcesByProperty(template, LogGroupType, "LogGroupName", logGroupName);
        if (matches.Count == 0)
            throw new InvalidOperationException($"No log group named '{logGroupName}' was found.");

        var properties = Properties(matches[0].Value);
        if (!properties.TryGetValue("RetentionInDays", out var retention) || !ValueEquals(retention, retentionDays))
            throw new InvalidOperationException($"The log group '{logGroupName}' was expected to declare RetentionInDays = {retentionDays}.");
    }

    /// <summary>
    /// Asserts the template contains the companion log group for a centralized log group, named
    /// <c>{centralizedLogGroupName}.local</c>, with the given retention. Token names cannot be asserted this way.
    /// </summary>
    public static void ShouldHaveCompanionLogGroup(this Template template, string centralizedLogGroupName, int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(template);

        var companionName = $"{centralizedLogGroupName}.local";
        var matches = FindResourcesByProperty(template, LogGroupType, "LogGroupName", companionName);
        if (matches.Count == 0)
            throw new InvalidOperationException($"No companion log group named '{companionName}' was found.");

        var properties = Properties(matches[0].Value);
        if (!properties.TryGetValue("RetentionInDays", out var retention) || !ValueEquals(retention, retentionDays))
            throw new InvalidOperationException($"The companion log group '{companionName}' was expected to declare RetentionInDays = {retentionDays}.");
    }

    /// <summary>
    /// Asserts the function's revision depends on each named log group. The dependency is verified through the
    /// resolved logical IDs, so the assertion never hard-codes generated identifiers.
    /// </summary>
    public static void ShouldHaveRevisionDependingOnLogGroups(this Template template, string functionName, params string[] logGroupNames)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(logGroupNames);

        var revisions = FindResourcesByProperty(template, RevisionType, "FunctionName", functionName);
        if (revisions.Count == 0)
            throw new InvalidOperationException($"No revision for Web Function '{functionName}' was found.");

        var dependsOn = DependsOnIds(revisions[0].Value);

        foreach (var logGroupName in logGroupNames)
        {
            var groups = FindResourcesByProperty(template, LogGroupType, "LogGroupName", logGroupName);
            if (groups.Count == 0)
                throw new InvalidOperationException($"No log group named '{logGroupName}' was found.");

            if (!dependsOn.Contains(groups[0].Key, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"The revision for '{functionName}' was expected to depend on the log group '{logGroupName}', but it does not.");
        }
    }

    private static IReadOnlyList<string> DependsOnIds(IDictionary<string, object> resource)
    {
        if (!resource.TryGetValue("DependsOn", out var value))
            return [];

        return value switch
        {
            string single => [single],
            IEnumerable<object> list => list.OfType<string>().ToList(),
            _ => [],
        };
    }

    private static string FunctionLogicalId(Template template, string functionName)
    {
        var matches = FindResourcesByProperty(template, WebFunctionType, "FunctionName", functionName);
        if (matches.Count == 0)
            throw new InvalidOperationException($"No Web Function named '{functionName}' was found.");

        return matches[0].Key;
    }

    private static string EndpointLogicalId(Template template, string functionName, string endpointName)
    {
        _ = FunctionLogicalId(template, functionName);

        var matches = new List<KeyValuePair<string, IDictionary<string, object>>>();
        foreach (var resource in template.FindResources(EndpointType))
        {
            var properties = Properties(resource.Value);
            if (properties.TryGetValue("EndpointName", out var name) && Equals(name, endpointName)
                && properties.TryGetValue("FunctionName", out var function) && Equals(function, functionName))
                matches.Add(resource);
        }

        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"No Web Function endpoint named '{endpointName}' was found on function '{functionName}'.");

        return matches[0].Key;
    }

    private static List<KeyValuePair<string, IDictionary<string, object>>> FindResourcesByProperty(
        Template template,
        string resourceType,
        string propertyName,
        object expectedValue)
    {
        var matches = new List<KeyValuePair<string, IDictionary<string, object>>>();
        foreach (var resource in template.FindResources(resourceType))
        {
            var properties = Properties(resource.Value);
            if (properties.TryGetValue(propertyName, out var value) && Equals(value, expectedValue))
                matches.Add(resource);
        }

        return matches;
    }

    private static IEnumerable<IDictionary<string, object>> ResourcePolicyStatements(Template template)
    {
        foreach (var policy in template.FindResources(ResourcePolicyType).Values)
        {
            foreach (var statement in StatementsOf(policy))
                yield return statement;
        }
    }

    private static IEnumerable<IDictionary<string, object>> StatementsOf(IDictionary<string, object> resource)
    {
        var properties = Properties(resource);
        if (!properties.TryGetValue("PolicyDocument", out var document) || document is not IDictionary<string, object> policyDocument)
            yield break;

        if (!policyDocument.TryGetValue("Statement", out var statements))
            yield break;

        switch (statements)
        {
            case IEnumerable<object> list:
                foreach (var statement in list.OfType<IDictionary<string, object>>())
                    yield return statement;
                break;
            case IDictionary<string, object> single:
                yield return single;
                break;
        }
    }

    private static bool StatementHasAction(IDictionary<string, object> statement, string action)
    {
        if (!statement.TryGetValue("Action", out var value))
            return false;

        return value switch
        {
            string single => string.Equals(single, action, StringComparison.Ordinal),
            IEnumerable<object> list => list.OfType<string>().Contains(action, StringComparer.Ordinal),
            _ => false,
        };
    }

    private static bool StatementHasServicePrincipal(IDictionary<string, object> statement, string service)
    {
        if (!statement.TryGetValue("Principal", out var principal) || principal is not IDictionary<string, object> principalMap)
            return false;

        return principalMap.TryGetValue("Service", out var value) && Equals(value, service);
    }

    private static bool StatementHasAwsPrincipal(IDictionary<string, object> statement, string principalArn)
    {
        if (!statement.TryGetValue("Principal", out var principal) || principal is not IDictionary<string, object> principalMap)
            return false;

        return principalMap.TryGetValue("AWS", out var value) && Equals(value, principalArn);
    }

    private static bool StatementHasStringEquals(IDictionary<string, object> statement, string key)
    {
        if (!statement.TryGetValue("Condition", out var condition) || condition is not IDictionary<string, object> conditionMap)
            return false;

        return conditionMap.TryGetValue("StringEquals", out var equals) && equals is IDictionary<string, object> equalsMap && equalsMap.ContainsKey(key);
    }

    private static bool StatementSourceArnContains(IDictionary<string, object> statement, string substring)
    {
        if (!statement.TryGetValue("Condition", out var condition) || condition is not IDictionary<string, object> conditionMap)
            return false;

        if (!conditionMap.TryGetValue("ArnLike", out var arnLike) || arnLike is not IDictionary<string, object> arnLikeMap)
            return false;

        if (!arnLikeMap.TryGetValue("aws:SourceArn", out var sourceArn))
            return false;

        return FlattenArn(sourceArn).Contains(substring, StringComparison.Ordinal);
    }

    // Flattens an intrinsic such as Fn::Join into its literal string parts. Unresolved references
    // (for example Ref AWS::Partition) contribute nothing, which is enough to locate the function name.
    private static string FlattenArn(object? value)
    {
        switch (value)
        {
            case string text:
                return text;
            case IDictionary<string, object> map when map.TryGetValue("Fn::Join", out var join)
                                                       && join is IEnumerable<object> joinParts:
            {
                var parts = joinParts.ToList();
                return parts.Count == 2 && parts[1] is IEnumerable<object> segments
                    ? string.Concat(segments.Select(FlattenArn))
                    : string.Empty;
            }
            default:
                return string.Empty;
        }
    }

    private static bool StatementHasEffect(IDictionary<string, object> statement, string effect) =>
        statement.TryGetValue("Effect", out var value) && Equals(value, effect);

    private static bool StatementTargetsEndpoint(IDictionary<string, object> statement, string endpointLogicalId) =>
        statement.TryGetValue("Resource", out var resource) && ResourceTargetsEndpoint(resource, endpointLogicalId);

    // The resource may be a single endpoint ARN or an array of them: IAM policy minimization
    // (@aws-cdk/aws-iam:minimizePolicies) merges equivalent statements into one with an array Resource.
    private static bool ResourceTargetsEndpoint(object? resource, string endpointLogicalId) =>
        resource switch
        {
            IEnumerable<object> list => list.Any(item => GetAttMatches(item, endpointLogicalId, "EndpointArn")),
            _ => GetAttMatches(resource, endpointLogicalId, "EndpointArn"),
        };

    private static bool GetAttMatches(object? value, string logicalId, string attribute) =>
        value is IDictionary<string, object> map
        && map.TryGetValue("Fn::GetAtt", out var getAtt)
        && getAtt is IEnumerable<object> parts
        && parts.OfType<string>().SequenceEqual([logicalId, attribute]);

    private static IDictionary<string, object> Properties(IDictionary<string, object> resource) =>
        resource.TryGetValue("Properties", out var properties) && properties is IDictionary<string, object> map
            ? map
            : new Dictionary<string, object>();

    private static bool ValueEquals(object? value, int expected)
    {
        try
        {
            return Convert.ToInt64(value) == expected;
        }
        catch (Exception) when (value is not null)
        {
            return false;
        }
    }
}
