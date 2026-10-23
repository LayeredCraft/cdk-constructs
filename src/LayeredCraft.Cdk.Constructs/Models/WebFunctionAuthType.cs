namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// How a Web Function endpoint authenticates incoming requests.
/// </summary>
public enum WebFunctionAuthType
{
    /// <summary>
    /// Lambda authenticates SigV4/SigV4A callers and authorizes them with IAM (identity policy and the
    /// function's resource policy). Required for CloudFront OAC and <c>GrantInvoke</c>.
    /// </summary>
    IamAuth,

    /// <summary>
    /// Lambda performs no authentication; the endpoint is publicly reachable at its own domain, even behind
    /// CloudFront. The application must authenticate every request.
    /// </summary>
    ApplicationManaged,
}
