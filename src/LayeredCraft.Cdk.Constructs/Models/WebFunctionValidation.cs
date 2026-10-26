using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;

[assembly: InternalsVisibleTo("LayeredCraft.Cdk.Constructs.Tests")]

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Value-level validation for Web Function configuration. Literal values are validated; unresolved
/// CloudFormation tokens are skipped rather than rejected. The constructs that own the runtime entry points
/// call these helpers so the rules are defined in one place.
/// </summary>
internal static class WebFunctionValidation
{
    internal const int MaxRegionCount = 50;

    internal const string HttpEndpointEnvironmentVariable = "AWS_LAMBDA_HTTP_ENDPOINT";

    internal const string NodeJsEntryPointEnvironmentVariable = "AWS_LAMBDA_NODEJS_ENTRYPOINT";

    private static readonly Regex FunctionNamePattern = new(
        "^[A-Za-z0-9](?:[A-Za-z0-9_-]*[A-Za-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EndpointNamePattern = new(
        "^[A-Za-z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EnvironmentVariableKeyPattern = new(
        "^[A-Za-z][A-Za-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CloudFrontDistributionArnPattern = new(
        "^arn:[a-z0-9-]+:cloudfront::[0-9]{12}:distribution/[A-Za-z0-9]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WebFunctionEndpointArnPattern = new(
        "^arn:[a-z0-9-]+:lambda:[a-z0-9-]+:[0-9]{12}:web-function/[^/]+/endpoint/[^/]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AccountPattern = new(
        "^[0-9]{12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReservedEndpointChildIds = new(StringComparer.Ordinal)
    {
        "Function",
        "Revision",
        "DefaultEndpoint",
        "ExecutionRole",
        "LogGroup",
        "LocalLogGroup",
        "ResourcePolicy",
    };

    /// <summary>Validates a Web Function name: 1–64 characters matching the Lambda name rules.</summary>
    internal static void ValidateFunctionName(string? functionName, string parameterName = "FunctionName")
    {
        if (functionName is null || Token.IsUnresolved(functionName))
            return;

        if (functionName.Length is 0 or > 64 || !FunctionNamePattern.IsMatch(functionName))
        {
            throw new ArgumentException(
                $"Function name '{functionName}' is invalid. It must be 1-64 characters long, start and end " +
                "with a letter or digit, and contain only letters, digits, hyphens, and underscores.",
                parameterName);
        }
    }

    /// <summary>Validates an endpoint name: 1–64 characters from <c>[A-Za-z0-9_-]</c>.</summary>
    internal static void ValidateEndpointName(string? endpointName, string parameterName = "EndpointName")
    {
        if (endpointName is null || Token.IsUnresolved(endpointName))
            return;

        if (endpointName.Length is 0 or > 64 || !EndpointNamePattern.IsMatch(endpointName))
        {
            throw new ArgumentException(
                $"Endpoint name '{endpointName}' is invalid. It must be 1-64 characters long and contain only " +
                "letters, digits, hyphens, and underscores.",
                parameterName);
        }
    }

    /// <summary>Validates a CloudFront distribution ARN: literal values are checked, tokens are skipped.</summary>
    internal static void ValidateCloudFrontDistributionArn(
        string? distributionArn,
        string parameterName = "distributionArn")
    {
        if (distributionArn is null || Token.IsUnresolved(distributionArn))
            return;

        if (!CloudFrontDistributionArnPattern.IsMatch(distributionArn))
        {
            throw new ArgumentException(
                $"'{distributionArn}' is not a CloudFront distribution ARN.",
                parameterName);
        }
    }

    /// <summary>Validates a timeout: 3–900 whole seconds.</summary>
    internal static void ValidateTimeout(Duration? timeout, string parameterName = "Timeout")
    {
        if (timeout is null || timeout.IsUnresolved())
            return;

        var milliseconds = timeout.ToMilliseconds();
        if ((decimal)milliseconds % 1000 != 0)
            throw new ArgumentOutOfRangeException(parameterName, timeout, "Timeout must be a whole number of seconds.");

        var seconds = milliseconds / 1000;
        if (seconds is < 3 or > 900)
            throw new ArgumentOutOfRangeException(parameterName, timeout, "Timeout must be between 3 and 900 seconds.");
    }

    /// <summary>Validates the maximum concurrency per environment: 1–128.</summary>
    internal static void ValidateMaxConcurrencyPerEnvironment(
        int? maxConcurrencyPerEnvironment,
        string parameterName = "MaxConcurrencyPerEnvironment")
    {
        if (maxConcurrencyPerEnvironment is null)
            return;

        if (maxConcurrencyPerEnvironment is < 1 or > 128)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                maxConcurrencyPerEnvironment,
                "MaxConcurrencyPerEnvironment must be between 1 and 128.");
        }
    }

    /// <summary>Validates the maximum number of environments: 2–10000.</summary>
    internal static void ValidateMaxEnvironments(int? maxEnvironments, string parameterName = "MaxEnvironments")
    {
        if (maxEnvironments is null)
            return;

        if (maxEnvironments is < 2 or > 10000)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                maxEnvironments,
                "MaxEnvironments must be between 2 and 10000.");
        }
    }

    /// <summary>
    /// Validates a request rate limit: 0, a multiple of 100 from 100 to 1000, or a multiple of 1000 from 2000
    /// to 10000.
    /// </summary>
    internal static void ValidateRateLimit(int? rateLimit, string parameterName = "RateLimit")
    {
        if (rateLimit is null)
            return;

        var value = rateLimit.Value;
        var isValid = value == 0
                      || (value is >= 100 and <= 1000 && value % 100 == 0)
                      || (value is >= 2000 and <= 10000 && value % 1000 == 0);

        if (!isValid)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                rateLimit,
                "RateLimit must be 0, a multiple of 100 between 100 and 1000, or a multiple of 1000 between " +
                "2000 and 10000.");
        }
    }

    /// <summary>
    /// Validates the Regions for an endpoint type: at most 50, no duplicates, none for <see cref="WebFunctionEndpointType.HomeRegion"/>
    /// beyond the home Region, and at least one non-home Region for multi- and per-Region endpoints. When any Region is an
    /// unresolved token the home-Region-only check is deferred to deployment, because the token may resolve to a non-home Region.
    /// </summary>
    internal static void ValidateRegions(
        WebFunctionEndpointType endpointType,
        IReadOnlyList<string>? regions,
        string? homeRegion = null,
        string parameterName = "Regions")
    {
        var isMultiRegion = endpointType is WebFunctionEndpointType.MultiRegion or WebFunctionEndpointType.PerRegion;

        if (regions is null || regions.Count == 0)
        {
            if (isMultiRegion)
                throw new ArgumentException($"{endpointType} endpoints require at least one Region.", parameterName);
            return;
        }

        if (regions.Count > MaxRegionCount)
        {
            throw new ArgumentException(
                $"Regions must contain at most {MaxRegionCount} entries. Got {regions.Count}.",
                parameterName);
        }

        var resolved = regions.Where(region => !Token.IsUnresolved(region)).ToList();

        if (resolved.Distinct(StringComparer.Ordinal).Count() != resolved.Count)
            throw new ArgumentException("Regions must not contain duplicates.", parameterName);

        if (isMultiRegion)
        {
            if (homeRegion is not null
                && !Token.IsUnresolved(homeRegion)
                && resolved.Count == regions.Count
                && resolved.All(region => string.Equals(region, homeRegion, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    $"{endpointType} endpoints require at least one Region other than the function's home Region.",
                    parameterName);
            }

            return;
        }

        if (homeRegion is not null && !Token.IsUnresolved(homeRegion))
        {
            if (resolved.Any(region => !string.Equals(region, homeRegion, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "HomeRegion endpoints may only list the function's home Region.",
                    parameterName);
            }

            return;
        }

        if (resolved.Count > 1)
        {
            throw new ArgumentException(
                "HomeRegion endpoints may list at most the function's home Region.",
                parameterName);
        }
    }

    /// <summary>Validates an environment variable name: <c>^[A-Za-z][A-Za-z0-9_]*$</c>, not reserved.</summary>
    internal static void ValidateEnvironmentVariableKey(string? key, string parameterName = "key")
    {
        if (key is null || Token.IsUnresolved(key))
            return;

        if (string.Equals(key, HttpEndpointEnvironmentVariable, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Environment variable '{HttpEndpointEnvironmentVariable}' is reserved by Lambda and cannot be set.",
                parameterName);
        }

        if (!EnvironmentVariableKeyPattern.IsMatch(key))
        {
            throw new ArgumentException(
                $"Environment variable name '{key}' is invalid. It must start with a letter and contain only " +
                "letters, digits, and underscores.",
                parameterName);
        }
    }

    /// <summary>Validates every key in an environment variable dictionary.</summary>
    internal static void ValidateEnvironmentVariables(
        IReadOnlyDictionary<string, string>? environmentVariables,
        string parameterName = "EnvironmentVariables")
    {
        if (environmentVariables is null)
            return;

        foreach (var key in environmentVariables.Keys)
            ValidateEnvironmentVariableKey(key, parameterName);
    }

    /// <summary>
    /// Validates an entry point: relative, no leading <c>/</c>, no <c>..</c> segment, and not combined with the
    /// <c>AWS_LAMBDA_NODEJS_ENTRYPOINT</c> environment variable.
    /// </summary>
    internal static void ValidateEntryPoint(
        string? entryPoint,
        IReadOnlyDictionary<string, string>? environmentVariables,
        string parameterName = "EntryPoint")
    {
        var hasEntryPoint = !string.IsNullOrEmpty(entryPoint);
        var hasNodeJsEntryPointVariable = environmentVariables is not null
                                          && environmentVariables.ContainsKey(NodeJsEntryPointEnvironmentVariable);

        if (hasEntryPoint && hasNodeJsEntryPointVariable)
        {
            throw new ArgumentException(
                $"EntryPoint and the {NodeJsEntryPointEnvironmentVariable} environment variable cannot both be set.",
                parameterName);
        }

        if (string.IsNullOrEmpty(entryPoint) || Token.IsUnresolved(entryPoint))
            return;

        if (entryPoint.StartsWith('/'))
            throw new ArgumentException("EntryPoint must be a relative path and must not start with '/'.", parameterName);

        if (entryPoint.Split('/').Any(segment => segment == ".."))
            throw new ArgumentException("EntryPoint must not contain '..'.", parameterName);
    }

    /// <summary>
    /// Validates a Web Function endpoint ARN: literal values must match
    /// <c>arn:{partition}:lambda:{region}:{account}:web-function/{function}/endpoint/{endpoint}</c>; tokens are skipped.
    /// </summary>
    internal static void ValidateEndpointArn(string? endpointArn, string parameterName = "EndpointArn")
    {
        if (endpointArn is null || Token.IsUnresolved(endpointArn))
            return;

        if (!WebFunctionEndpointArnPattern.IsMatch(endpointArn))
        {
            throw new ArgumentException(
                $"'{endpointArn}' is not a Lambda Web Function endpoint ARN.",
                parameterName);
        }
    }

    /// <summary>Validates an imported endpoint domain name: null fails, empty fails, tokens are skipped.</summary>
    internal static void ValidateEndpointDomainName(string? domainName, string parameterName = "DomainName")
    {
        if (domainName is null)
            throw new ArgumentNullException(parameterName, "DomainName is required.");

        if (Token.IsUnresolved(domainName))
            return;

        if (domainName.Length == 0)
            throw new ArgumentException("DomainName must not be empty.", parameterName);
    }

    /// <summary>Validates an account id: a literal value must be 12 digits; tokens are skipped.</summary>
    internal static void ValidateAccount(string? account, string parameterName = "Account")
    {
        if (account is null || Token.IsUnresolved(account))
            return;

        if (!AccountPattern.IsMatch(account))
        {
            throw new ArgumentException(
                $"Account '{account}' is invalid. It must be a 12-digit account id.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates that a supplied account and a literal endpoint ARN agree when both are literal.
    /// </summary>
    internal static void ValidateImportedAccountConsistency(string? account, string? endpointArn)
    {
        if (account is null || Token.IsUnresolved(account))
            return;

        if (endpointArn is null || Token.IsUnresolved(endpointArn))
            return;

        var parts = endpointArn.Split(':');
        if (parts.Length >= 6 && !string.Equals(parts[4], account, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Account '{account}' does not match the account in endpoint ARN '{endpointArn}'.",
                "Account");
        }
    }

    /// <summary>
    /// Validates that an added endpoint id does not collide with a reserved child id of the Web Function construct.
    /// </summary>
    internal static void ValidateEndpointChildId(string? id, string parameterName = "id")
    {
        if (id is null)
            return;

        if (ReservedEndpointChildIds.Contains(id))
        {
            throw new ArgumentException(
                $"Endpoint id '{id}' is reserved by the Web Function construct. Choose a different id.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates a statement added to a Web Function resource policy. Deny statements are unrestricted. An Allow
    /// statement that names a wildcard or service principal is only accepted when it carries conditions; the
    /// consumer is responsible for ensuring those conditions meaningfully restrict access. Allow statements that
    /// use <c>NotPrincipal</c> are rejected.
    /// </summary>
    internal static void ValidateResourcePolicyStatement(PolicyStatement statement, string parameterName = "statement")
    {
        ArgumentNullException.ThrowIfNull(statement);

        if (statement.Effect == Effect.DENY)
            return;

        if (statement.NotPrincipals is { Length: > 0 })
        {
            throw new ArgumentException(
                "A resource-policy Allow statement must not use NotPrincipal. Name the principals explicitly.",
                parameterName);
        }

        if (statement.Conditions is IDictionary<string, object> conditions && conditions.Count > 0)
            return;

        foreach (var principal in statement.Principals ?? [])
        {
            if (IsServicePrincipal(principal) || IsWildcardPrincipal(principal))
            {
                throw new ArgumentException(
                    "A resource-policy Allow statement that names a wildcard or service principal must include " +
                    "conditions that restrict access. Add conditions (for CloudFront, use " +
                    "GrantCloudFrontInvoke).",
                    parameterName);
            }
        }
    }

    /// <summary>Whether the principal's rendered policy fragment is a service principal.</summary>
    internal static bool IsServicePrincipal(IPrincipal? principal)
    {
        var json = principal?.PolicyFragment?.PrincipalJson;
        if (json is null)
            return false;

        return json.Keys.Any(key => string.Equals(key, "Service", StringComparison.Ordinal));
    }

    /// <summary>Whether the principal's rendered policy fragment is a wildcard principal.</summary>
    internal static bool IsWildcardPrincipal(IPrincipal? principal)
    {
        var json = principal?.PolicyFragment?.PrincipalJson;
        if (json is null)
            return false;

        return json.Values.Any(IsWildcardValue);
    }

    private static bool IsWildcardValue(object? value)
    {
        switch (value)
        {
            case null:
                return false;
            case string text:
                return text == "*";
            case System.Collections.IEnumerable items:
                foreach (var item in items)
                {
                    if (IsWildcardValue(item))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }
}
