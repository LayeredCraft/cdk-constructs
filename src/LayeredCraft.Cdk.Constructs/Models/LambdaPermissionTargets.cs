using System;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Specifies which Lambda resources a <see cref="LambdaPermission"/> grants invocation on.
/// </summary>
[Flags]
public enum LambdaPermissionTargets
{
    /// <summary>No target. Not valid for a permission.</summary>
    None = 0,

    /// <summary>The unqualified function.</summary>
    Function = 1 << 0,

    /// <summary>The published version pinned by this construct.</summary>
    Version = 1 << 1,

    /// <summary>The alias (the default, matching least-privilege deployments).</summary>
    Alias = 1 << 2,
}