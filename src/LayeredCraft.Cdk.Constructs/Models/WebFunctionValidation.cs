using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Amazon.CDK;

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
        if (milliseconds % 1000 != 0)
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
    /// beyond the home Region, and at least one non-home Region for multi- and per-Region endpoints.
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

        if (!hasEntryPoint || entryPoint is null || Token.IsUnresolved(entryPoint))
            return;

        if (entryPoint.StartsWith('/'))
            throw new ArgumentException("EntryPoint must be a relative path and must not start with '/'.", parameterName);

        if (entryPoint.Split('/').Any(segment => segment == ".."))
            throw new ArgumentException("EntryPoint must not contain '..'.", parameterName);
    }
}
