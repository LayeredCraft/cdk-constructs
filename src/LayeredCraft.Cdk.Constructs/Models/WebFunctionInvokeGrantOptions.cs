namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Options for <c>GrantInvoke</c> on a Web Function endpoint.
/// </summary>
public sealed record WebFunctionInvokeGrantOptions
{
    /// <summary>
    /// Who maintains the caller's identity policy. Defaults to
    /// <see cref="WebFunctionCallerPolicy.Automatic"/>.
    /// </summary>
    public WebFunctionCallerPolicy CallerPolicy { get; init; } = WebFunctionCallerPolicy.Automatic;
}
