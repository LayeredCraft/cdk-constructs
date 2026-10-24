using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WebFunctionSandbox;

/// <summary>
/// The disjoint stacks this sandbox app can synthesize. Selecting <see cref="App"/> also builds the
/// artifact and external stacks it references across stacks.
/// </summary>
[Flags]
internal enum SandboxComponents
{
    None = 0,
    Artifacts = 1,
    External = 2,
    App = 4,
    All = Artifacts | External | App,
}

/// <summary>
/// Raised when required sandbox configuration is missing or invalid. The message is intended to be read by
/// an operator running the sandbox scripts.
/// </summary>
internal sealed class SandboxConfigurationException(string message) : Exception(message);

/// <summary>
/// Fully explicit sandbox configuration. Nothing is inferred from the ambient AWS environment or from an
/// account's default region: an operator (or a script) must supply the account, region, sandbox id, and the
/// artifact identity to deploy.
/// </summary>
internal sealed record SandboxConfiguration
{
    private static readonly Regex AccountPattern = new("^[0-9]{12}$", RegexOptions.CultureInvariant);
    private static readonly Regex RegionPattern = new("^[a-z]{2}-[a-z]+-[0-9]$", RegexOptions.CultureInvariant);
    private static readonly Regex SandboxIdPattern = new("^[a-z][a-z0-9-]{1,14}[a-z0-9]$", RegexOptions.CultureInvariant);

    public required string Account { get; init; }

    public required string Region { get; init; }

    public required string SandboxId { get; init; }

    public required SandboxComponents Components { get; init; }

    public required string ArtifactsObjectPrefix { get; init; }

    public required ArtifactReference NextJs { get; init; }

    public required ArtifactReference AspNetCore { get; init; }

    public required ArtifactReference Blazor { get; init; }

    public bool SecondDistribution { get; init; }

    public string? CrossAccountCallerRoleArn { get; init; }

    public bool BuildsArtifacts => Components.HasFlag(SandboxComponents.Artifacts);

    public bool BuildsExternal => Components.HasFlag(SandboxComponents.External);

    public bool BuildsApp => Components.HasFlag(SandboxComponents.App);

    /// <summary>
    /// True when one or more workload artifacts are pinned to the synthesis-only placeholder version. This only
    /// happens for support-stack provisioning and teardown synthesis, where the app stack's object versions are
    /// irrelevant; it must never be used to deploy the app stack.
    /// </summary>
    public bool HasPlaceholderVersions =>
        NextJs.ObjectVersion == ArtifactReference.PlaceholderVersion
        || AspNetCore.ObjectVersion == ArtifactReference.PlaceholderVersion
        || Blazor.ObjectVersion == ArtifactReference.PlaceholderVersion;

    /// <summary>
    /// Reads and validates the sandbox configuration from environment variables. Every required value must be
    /// present and well formed; the aggregate failure names all missing variables at once.
    /// </summary>
    public static SandboxConfiguration FromEnvironment()
    {
        var errors = new List<string>();

        var account = RequireValue("SANDBOX_ACCOUNT", errors);
        var region = RequireValue("SANDBOX_REGION", errors);
        var sandboxId = RequireValue("SANDBOX_ID", errors);

        var components = ParseComponents(OptionalValue("SANDBOX_COMPONENTS") ?? "all", errors);

        if (account is not null && !AccountPattern.IsMatch(account))
            errors.Add("SANDBOX_ACCOUNT must be a 12-digit AWS account id.");

        if (region is not null && !RegionPattern.IsMatch(region))
            errors.Add("SANDBOX_REGION must look like 'us-east-1'.");

        if (sandboxId is not null && !SandboxIdPattern.IsMatch(sandboxId))
            errors.Add("SANDBOX_ID must be 3-16 lowercase alphanumeric/hyphen characters and start and end with an alphanumeric character.");

        var buildsApp = components.HasFlag(SandboxComponents.App);

        if (errors.Count > 0)
            throw new SandboxConfigurationException("Invalid sandbox configuration:\n  - " + string.Join("\n  - ", errors));

        var sandboxIdValue = sandboxId!;

        // The artifact object prefix is shared by the upload scripts, the CDK app, and the external bucket policy,
        // so it is resolved in exactly one place and reused everywhere (mirrors object_prefix in common.sh).
        var artifactsPrefix = OptionalValue("SANDBOX_ARTIFACTS_PREFIX") ?? sandboxIdValue;
        var allowPlaceholder = IsTruthy(OptionalValue("SANDBOX_ALLOW_PLACEHOLDER_VERSIONS"));

        var nextJs = ArtifactReference.Create("nextjs", artifactsPrefix, buildsApp, allowPlaceholder, errors);
        var aspNetCore = ArtifactReference.Create("aspnetcore", artifactsPrefix, buildsApp, allowPlaceholder, errors);
        var blazor = ArtifactReference.Create("blazor", artifactsPrefix, buildsApp, allowPlaceholder, errors);

        if (errors.Count > 0)
            throw new SandboxConfigurationException("Invalid sandbox artifact configuration:\n  - " + string.Join("\n  - ", errors));

        return new SandboxConfiguration
        {
            Account = account!,
            Region = region!,
            SandboxId = sandboxIdValue,
            Components = components,
            ArtifactsObjectPrefix = artifactsPrefix,
            NextJs = nextJs,
            AspNetCore = aspNetCore,
            Blazor = blazor,
            SecondDistribution = IsTruthy(OptionalValue("SANDBOX_SECOND_DISTRIBUTION")),
            CrossAccountCallerRoleArn = OptionalValue("SANDBOX_CROSS_ACCOUNT_CALLER_ROLE_ARN"),
        };
    }

    private static SandboxComponents ParseComponents(string raw, List<string> errors)
    {
        var result = SandboxComponents.None;
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "all":
                    result |= SandboxComponents.All;
                    break;
                case "artifacts":
                    result |= SandboxComponents.Artifacts;
                    break;
                case "external":
                    result |= SandboxComponents.External;
                    break;
                case "app":
                    result |= SandboxComponents.App;
                    break;
                default:
                    errors.Add($"SANDBOX_COMPONENTS contains an unknown component '{token}' (expected artifacts, external, app, or all).");
                    break;
            }
        }

        // The app stack consumes the artifact bucket as an owned cross-stack reference, so both support stacks
        // are always part of the app's graph.
        if (result.HasFlag(SandboxComponents.App))
            result |= SandboxComponents.Artifacts | SandboxComponents.External;

        return result;
    }

    private static string RequireValue(string name, List<string> errors)
    {
        var value = OptionalValue(name);
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{name} is required.");
        return value ?? string.Empty;
    }

    private static string? OptionalValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsTruthy(string? value)
        => value is not null && value.Equals("1", StringComparison.Ordinal) || value is not null && value.Equals("true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The immutable S3 identity (object key and optional version id) of one workload's deploy artifact.
/// </summary>
internal sealed record ArtifactReference
{
    public required string Name { get; init; }

    public required string ObjectKey { get; init; }

    public string? ObjectVersion { get; init; }

    /// <summary>
    /// Sentinel object version used only for synthesis that does not deploy the app stack (support-stack
    /// provisioning and teardown). It can never match a real S3 version id, and the app-stack deploy path never
    /// enables it, so a placeholder can never reach a real deployment.
    /// </summary>
    public const string PlaceholderVersion = "__SANDBOX_PLACEHOLDER__";

    public static ArtifactReference Create(string workload, string artifactPrefix, bool requireVersion, bool allowPlaceholder, List<string> errors)
    {
        var upper = workload.ToUpperInvariant();
        var key = Environment.GetEnvironmentVariable($"SANDBOX_{upper}_OBJECT_KEY");
        if (string.IsNullOrWhiteSpace(key))
            key = $"{artifactPrefix}/{workload}.zip";

        var version = Environment.GetEnvironmentVariable($"SANDBOX_{upper}_OBJECT_VERSION");
        version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();

        if (requireVersion && version is null)
        {
            if (allowPlaceholder)
                version = PlaceholderVersion;
            else
                errors.Add($"SANDBOX_{upper}_OBJECT_VERSION is required when the app stack is synthesized (upload the artifact and pass its version id).");
        }

        return new ArtifactReference
        {
            Name = workload,
            ObjectKey = key,
            ObjectVersion = version,
        };
    }
}