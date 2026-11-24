namespace LayeredCraft.Cdk.Constructs.Models;

public sealed record LambdaPermission
{
    public required string Principal { get; set; }
    public required string Action { get; set; }
    public string? EventSourceToken { get; set; }
    public string? SourceArn { get; set; }

    /// <summary>
    /// The targets this permission is applied to. Defaults to <see cref="LambdaPermissionTargets.Alias"/>.
    /// </summary>
    public LambdaPermissionTargets Targets { get; set; } = LambdaPermissionTargets.Alias;
}