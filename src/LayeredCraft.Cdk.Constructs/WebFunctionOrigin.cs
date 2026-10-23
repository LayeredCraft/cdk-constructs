using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// Creates CloudFront origins for Web Function endpoints, mirroring CDK's Function URL origin helpers. Domain,
/// protocol (HTTPS only), and the origin access control id are owned by the factory and are deliberately not
/// expressible on <see cref="WebFunctionOriginProps"/>.
/// </summary>
public static class WebFunctionOrigin
{
    /// <summary>
    /// Creates an HTTPS origin for an <see cref="WebFunctionAuthType.IamAuth"/> endpoint, signed by an origin
    /// access control (origin type lambda, <c>always-amz-auth</c>; <c>sigv4</c> for
    /// <see cref="WebFunctionEndpointType.HomeRegion"/> and <see cref="WebFunctionEndpointType.PerRegion"/>,
    /// <c>sigv4a</c> for <see cref="WebFunctionEndpointType.MultiRegion"/>).
    /// </summary>
    /// <param name="endpoint">The endpoint the distribution proxies to.</param>
    /// <param name="props">Optional origin settings.</param>
    /// <returns>The origin, bound to the distribution that uses it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="endpoint"/> does not use IAM authentication.</exception>
    public static IOrigin WithOriginAccessControl(IWebFunctionEndpoint endpoint, WebFunctionOriginWithOacProps? props = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        props ??= new WebFunctionOriginWithOacProps();

        if (endpoint.AuthType != WebFunctionAuthType.IamAuth)
        {
            throw new InvalidOperationException(
                "OAC cannot restrict an ApplicationManaged endpoint; it stays publicly reachable. Use IamAuth, or " +
                "WebFunctionOrigin.WithoutOriginAccessControl to state that the endpoint is public.");
        }

        ValidateOriginConfiguration(endpoint.DomainName, props);

        return new WebFunctionOacOrigin(endpoint, props);
    }

    /// <summary>
    /// Creates an HTTPS origin for an <see cref="WebFunctionAuthType.ApplicationManaged"/> endpoint. The endpoint
    /// remains directly reachable on the internet; CloudFront adds no access control.
    /// </summary>
    /// <param name="endpoint">The endpoint the distribution proxies to.</param>
    /// <param name="props">Optional origin settings.</param>
    /// <returns>The origin.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="endpoint"/> uses IAM authentication.</exception>
    public static IOrigin WithoutOriginAccessControl(IWebFunctionEndpoint endpoint, WebFunctionOriginProps? props = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        props ??= new WebFunctionOriginProps();

        if (endpoint.AuthType != WebFunctionAuthType.ApplicationManaged)
        {
            throw new InvalidOperationException(
                "An IamAuth endpoint must be reached through an origin access control, because an unsigned request " +
                "is rejected. Use WebFunctionOrigin.WithOriginAccessControl, or an ApplicationManaged endpoint with " +
                "WebFunctionOrigin.WithoutOriginAccessControl.");
        }

        return new HttpOrigin(endpoint.DomainName, BuildHttpOriginProps(props, originAccessControlId: null));
    }

    /// <summary>
    /// Maps the library-owned origin settings onto <see cref="HttpOriginProps"/>, forcing HTTPS and leaving the
    /// origin access control id to the caller.
    /// </summary>
    internal static HttpOriginProps BuildHttpOriginProps(WebFunctionOriginProps props, string? originAccessControlId)
    {
        ArgumentNullException.ThrowIfNull(props);

        return new HttpOriginProps
        {
            OriginId = props.OriginId,
            OriginPath = props.OriginPath,
            CustomHeaders = props.CustomHeaders is null
                ? null
                : new Dictionary<string, string>(props.CustomHeaders),
            ConnectionAttempts = props.ConnectionAttempts,
            ConnectionTimeout = props.ConnectionTimeout,
            ReadTimeout = props.ReadTimeout,
            KeepaliveTimeout = props.KeepaliveTimeout,
            ResponseCompletionTimeout = props.ResponseCompletionTimeout,
            OriginShieldEnabled = props.OriginShieldEnabled,
            OriginShieldRegion = props.OriginShieldRegion,
            ProtocolPolicy = OriginProtocolPolicy.HTTPS_ONLY,
            OriginAccessControlId = originAccessControlId,
        };
    }

    /// <summary>
    /// Runs CDK's own <see cref="HttpOrigin"/> validation (connection attempts, timeouts, ports, and SSL protocols)
    /// at factory time. This fails before the distribution binds the origin, so a rejected configuration never
    /// leaves an origin access control or an invocation-policy statement behind.
    /// </summary>
    private static void ValidateOriginConfiguration(string domainName, WebFunctionOriginProps props)
        => _ = new HttpOrigin(domainName, BuildHttpOriginProps(props, originAccessControlId: null));
}
