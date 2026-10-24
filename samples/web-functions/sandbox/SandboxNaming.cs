using System;
using Amazon.CDK;

namespace WebFunctionSandbox;

/// <summary>
/// Deterministic, sandbox-scoped names. Every name is derived from the operator-supplied sandbox id, so a
/// destroy command can prove a resource belongs to this sandbox before acting on it.
/// </summary>
internal sealed class SandboxNaming(SandboxConfiguration configuration)
{
    /// <summary>
    /// Fixed namespace placed in every stack name so a sandbox stack is recognisable as belonging to this tool and
    /// cannot be confused with an unrelated stack that happens to share the sandbox id.
    /// </summary>
    public const string Namespace = "lccdk-wf-sandbox";

    /// <summary>Stack tag key whose value is the owning sandbox id.</summary>
    public const string OwnershipTagKey = "lc-webfunction-sandbox-id";

    /// <summary>Stack tag key whose value identifies the owning tool.</summary>
    public const string OwnerTagKey = "lc-webfunction-sandbox-owner";

    /// <summary>Value written to <see cref="OwnerTagKey"/> for every sandbox stack.</summary>
    public const string Owner = "LayeredCraft.Cdk.Constructs";

    public string Prefix { get; } = configuration.SandboxId;

    public string ArtifactsStackName => $"{Namespace}-{Prefix}-artifacts";

    public string ExternalStackName => $"{Namespace}-{Prefix}-external";

    public string AppStackName => $"{Namespace}-{Prefix}-app";

    // Bucket names are globally unique: they embed the account and Region so two sandboxes in the same account
    // (or the same sandbox id in two accounts) never collide.
    public string ArtifactsBucketName => $"{Prefix}-artifacts-{configuration.Account}-{configuration.Region}";

    public string ExternalBucketName => $"{Prefix}-external-{configuration.Account}-{configuration.Region}";

    public string NextJsFunctionName => $"{Prefix}-nextjs";

    public string AspNetCoreFunctionName => $"{Prefix}-aspnetcore";

    public string BlazorFunctionName => $"{Prefix}-blazor";

    // Two imported caller roles: the sandbox imports one with Mutable = false and one with the default (mutable),
    // covering both resource-policy-only authorization sub-cases in the acceptance matrix (§19.1 3.3).
    public string ImportedCallerImmutableRoleName => $"{Prefix}-imported-caller-immutable";

    public string ImportedCallerMutableRoleName => $"{Prefix}-imported-caller-mutable";

    public string ImportedCallerImmutableRoleArn =>
        $"arn:aws:iam::{configuration.Account}:role/{ImportedCallerImmutableRoleName}";

    public string ImportedCallerMutableRoleArn =>
        $"arn:aws:iam::{configuration.Account}:role/{ImportedCallerMutableRoleName}";

    /// <summary>
    /// Applies the sandbox ownership tags to a stack. The shell scripts read these tags back to prove a
    /// pre-existing stack belongs to this sandbox before mutating or deleting it.
    /// </summary>
    public void ApplyTags(Stack stack)
    {
        Tags.Of(stack).Add(OwnershipTagKey, Prefix);
        Tags.Of(stack).Add(OwnerTagKey, Owner);
    }

    /// <summary>True when a literal resource name belongs to this sandbox.</summary>
    public bool OwnsName(string? value)
        => value is not null && value.StartsWith(Prefix + "-", StringComparison.Ordinal);
}