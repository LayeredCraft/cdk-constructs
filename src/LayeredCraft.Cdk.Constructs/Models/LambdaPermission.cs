namespace LayeredCraft.Cdk.Constructs.Models;

public sealed record LambdaPermission
{
    public required string Principal { get; set; }
    public required string Action { get; set; }
    public string? EventSourceToken { get; set; }
    public string? SourceArn { get; set; }
}