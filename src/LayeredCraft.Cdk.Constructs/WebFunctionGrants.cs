using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// Shared implementation of the Web Function <c>GrantInvoke</c> rules (design §10.2, revision 4). The library
/// writes an identity policy only when it can verify the write happened; everything else uses the function's single
/// resource policy on an owned endpoint, and a grant that cannot be fully configured fails at synthesis.
/// </summary>
internal static class WebFunctionGrants
{
    internal const string InvokeAction = "lambda:InvokeWebFunctionEndpoint";

    internal static Grant GrantInvoke(
        WebFunctionAuthType authType,
        IGrantable grantee,
        string endpointName,
        string endpointArn,
        string? endpointAccount,
        WebFunctionInvokeGrantOptions? options,
        IConstruct endpointConstruct,
        IResourceWithPolicyV2? resourcePolicyOwner)
    {
        ArgumentNullException.ThrowIfNull(grantee);

        if (authType != WebFunctionAuthType.IamAuth)
        {
            throw new InvalidOperationException(
                $"The '{endpointName}' endpoint uses ApplicationManaged authentication, so invoke access cannot be " +
                "granted. Use an IamAuth endpoint.");
        }

        var principal = grantee.GrantPrincipal;

        RejectServicePrincipal(grantee, principal, endpointConstruct, endpointName);
        RejectWildcardPrincipal(grantee, principal, endpointConstruct, endpointName);
        RejectUnsupportedPrincipalWrapper(grantee, principal, endpointConstruct, endpointName);

        if ((options?.CallerPolicy ?? WebFunctionCallerPolicy.Automatic) == WebFunctionCallerPolicy.ExternallyManaged)
        {
            var resourceGrant = AddResourceStatement(resourcePolicyOwner, grantee, principal, endpointArn, endpointName);

            if (!IsAccountPrincipal(principal))
            {
                Annotations.Of(endpointConstruct).AddInfo(
                    $"Invocation by {Describe(grantee)} requires an identity policy in the caller's account allowing " +
                    $"'{InvokeAction}' on {DescribeEndpoint(endpointConstruct, endpointName)}.");
            }

            return resourceGrant;
        }

        var endpointAccountEstablished = endpointAccount is not null && !Token.IsUnresolved(endpointAccount);

        if (IsAccountPrincipal(principal))
        {
            throw new InvalidOperationException(
                $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be fully " +
                $"configured for {Describe(grantee)}. An account principal does not identify a caller, so the library " +
                "will not treat the grant as complete. Grant an IAM role, user, or group instead, or pass " +
                "CallerPolicy = ExternallyManaged and configure the caller's identity policy in its own account.");
        }

        // An imported endpoint owns no resource policy, so a grant can only be completed when the endpoint's
        // account is established; otherwise it fails for every grantee, owned callers included.
        if (resourcePolicyOwner is null && !endpointAccountEstablished)
        {
            throw new InvalidOperationException(
                $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be fully " +
                $"configured for {Describe(grantee)} because the endpoint's account cannot be established. Supply a " +
                "literal Account (or a literal endpoint ARN) so the library can write an identity policy, or grant " +
                "access outside this app. The endpoint remains usable as a CloudFront origin with " +
                "WebFunctionOriginPermissions.ExternallyManaged.");
        }

        if (IsOwnedMutableIdentity(principal))
        {
            var owned = (IConstruct)principal;

            // Ownership of both the caller and the endpoint in the same stack is enough to establish that the
            // identity policy can be written, even when the stack account is an unresolved token.
            if (resourcePolicyOwner is not null && ReferenceEquals(Stack.Of(owned), Stack.Of(endpointConstruct)))
                return WriteIdentityPolicy(grantee, endpointArn);

            // Different stacks are allowed only when both accounts are literal and equal; unresolved ownership is
            // rejected rather than assumed.
            if (endpointAccountEstablished && AreAccountsLiterallySame(Stack.Of(owned).Account, endpointAccount!))
                return WriteIdentityPolicy(grantee, endpointArn);

            // An imported endpoint owns no resource policy, so the only remaining reason here is that the caller's
            // account is not established. Say that, rather than implying a different-stack mismatch.
            if (resourcePolicyOwner is null)
            {
                throw new InvalidOperationException(
                    $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be fully " +
                    $"configured for {Describe(grantee)} because the caller's account cannot be established. The " +
                    "endpoint is imported (its resource policy is externally owned) and the caller's stack account is " +
                    "not established, so the library will not write an identity policy it cannot verify. Give the " +
                    "caller's stack an explicit environment account that matches the endpoint's, import the caller " +
                    "with a literal ARN in the endpoint's account, or grant with CallerPolicy = ExternallyManaged.");
            }

            throw new InvalidOperationException(
                $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be fully " +
                $"configured for {Describe(grantee)}. The caller is owned by a different stack whose account cannot be " +
                "established as the endpoint's account (matching literal accounts are required). Give both stacks " +
                "matching explicit environment accounts, or grant the caller from the endpoint's own stack.");
        }

        if (!endpointAccountEstablished)
        {
            throw new InvalidOperationException(
                $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be fully " +
                $"configured for {Describe(grantee)} because the endpoint's account cannot be established. Supply a " +
                "literal Account (or a literal endpoint ARN) so the library can write an identity policy, or grant " +
                "access outside this app. The endpoint remains usable as a CloudFront origin with " +
                "WebFunctionOriginPermissions.ExternallyManaged.");
        }

        if (IsEstablished(principal, endpointAccount!))
        {
            if (resourcePolicyOwner is null)
            {
                throw new InvalidOperationException(
                    $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be fully " +
                    $"configured for {Describe(grantee)} because the endpoint's resource policy is externally owned and " +
                    "no identity policy could be verified. Grant an IAM role, user, or group the library can write to, " +
                    "or configure the caller's access outside this app.");
            }

            // Established, but the identity write is not verified (an immutable same-account role): fall back to
            // the resource policy on an owned endpoint.
            return AddResourceStatement(resourcePolicyOwner, grantee, principal, endpointArn, endpointName);
        }

        throw new InvalidOperationException(
            $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be fully " +
            $"configured for {Describe(grantee)}. The caller's account cannot be established in this deployment, so " +
            "the library will not write a cross-account identity policy. Give the caller and the endpoint matching " +
            "explicit environment accounts (or import the caller with a literal ARN in the deployment account), or " +
            "pass CallerPolicy = ExternallyManaged and configure the caller's identity policy in its own account.");
    }

    internal static Grant AddResourceStatement(
        IResourceWithPolicyV2? resourcePolicyOwner,
        IGrantable grantee,
        IPrincipal principal,
        string endpointArn,
        string endpointName)
    {
        if (resourcePolicyOwner is null)
        {
            throw new InvalidOperationException(
                $"The resource policy of the '{endpointName}' endpoint is externally owned, so the library cannot " +
                "add an invocation statement for it. Configure the resource policy in its owning account.");
        }

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = [InvokeAction],
            Resources = [endpointArn],
            Principals = [principal],
        });

        var result = resourcePolicyOwner.AddToResourcePolicy(statement);
        if (!result.StatementAdded)
        {
            throw new InvalidOperationException(
                $"The '{endpointName}' endpoint's resource policy did not accept the invocation statement for " +
                $"{Describe(grantee)}.");
        }

        return Grant.AddStatementToResourcePolicy(new GrantPolicyWithResourceOptions
        {
            Grantee = grantee,
            Actions = [InvokeAction],
            ResourceArns = [endpointArn],
            Resource = resourcePolicyOwner,
            Statement = statement,
        });
    }

    private static void RejectServicePrincipal(
        IGrantable grantee,
        IPrincipal principal,
        IConstruct endpointConstruct,
        string endpointName)
    {
        if (!WebFunctionValidation.IsServicePrincipal(principal))
            return;

        var service = principal.PolicyFragment?.PrincipalJson.TryGetValue("Service", out var value) == true
            ? FlattenStrings(value)
            : [];

        var isCloudFront = service.Any(text =>
        {
            var resolved = Stack.Of(endpointConstruct).Resolve(text)?.ToString() ?? text;
            return resolved.Contains("cloudfront.amazonaws.com", StringComparison.Ordinal) ||
                   text.Contains("cloudfront.amazonaws.com", StringComparison.Ordinal);
        });

        var remedy = isCloudFront
            ? "Use GrantCloudFrontInvoke(distributionArn) to grant CloudFront invoke access."
            : "Add an explicit statement with endpoint.AddToResourcePolicy(...) that includes the scoping conditions " +
              "the service documents.";

        throw new InvalidOperationException(
            $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be granted to " +
            $"the service principal for {Describe(grantee)} through GrantInvoke. {remedy}");
    }

    private static void RejectWildcardPrincipal(
        IGrantable grantee,
        IPrincipal principal,
        IConstruct endpointConstruct,
        string endpointName)
    {
        if (!WebFunctionValidation.IsWildcardPrincipal(principal))
            return;

        throw new InvalidOperationException(
            $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be granted to " +
            $"the wildcard principal for {Describe(grantee)} through GrantInvoke. Add an explicit statement with " +
            "endpoint.AddToResourcePolicy(...) that names the callers and the scoping conditions.");
    }

    private static bool IsAccountPrincipal(IPrincipal principal)
    {
        if (principal is AccountPrincipal or AccountRootPrincipal)
            return true;

        var json = principal.PolicyFragment?.PrincipalJson;
        if (json is null)
            return false;

        return json.Values.Any(IsAccountRootOrId);
    }

    private static bool IsAccountRootOrId(object? value)
    {
        switch (value)
        {
            case null:
                return false;
            case string text:
                if (Token.IsUnresolved(text))
                    return false;

                return (text.StartsWith("arn:", StringComparison.Ordinal) &&
                        text.EndsWith(":root", StringComparison.Ordinal)) ||
                       IsAccountId(text);
            case System.Collections.IEnumerable items:
                foreach (var item in items)
                {
                    if (IsAccountRootOrId(item))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool IsAccountId(string text) => text.Length == 12 && text.All(char.IsAsciiDigit);

    private static IEnumerable<string> FlattenStrings(object? value)
    {
        switch (value)
        {
            case string text:
                yield return text;
                break;
            case System.Collections.IEnumerable items:
                foreach (var item in items)
                {
                    foreach (var text in FlattenStrings(item))
                        yield return text;
                }

                break;
        }
    }

    private static void RejectUnsupportedPrincipalWrapper(
        IGrantable grantee,
        IPrincipal principal,
        IConstruct endpointConstruct,
        string endpointName)
    {
        // PrincipalWithConditions (and any similar wrapper) can change what actually gets authorized, and jsii does
        // not expose a reliable way to see through it. Fail closed instead of writing a policy we cannot verify.
        if (principal is not PrincipalWithConditions)
            return;

        throw new InvalidOperationException(
            $"Invoke access to the {DescribeEndpoint(endpointConstruct, endpointName)} endpoint cannot be granted for " +
            $"{Describe(grantee)} through GrantInvoke because the principal carries conditions and the library cannot " +
            "verify exactly which authorization is written. Grant the underlying principal directly, or add an " +
            "explicit statement with endpoint.AddToResourcePolicy(...).");
    }

    private static bool IsOwnedMutableIdentity(IPrincipal principal)
    {
        // Only a concrete, owned, mutable CDK identity can be verified to accept an attached identity policy. We
        // classify by type up front; we never probe by attempting a write.
        if (principal is not (Role or User or Group or LazyRole))
            return false;

        return principal is IConstruct construct && Resource.IsOwnedResource(construct);
    }

    private static Grant WriteIdentityPolicy(IGrantable grantee, string endpointArn)
        => Grant.AddToPrincipal(new GrantOnPrincipalOptions
        {
            Grantee = grantee,
            Actions = [InvokeAction],
            ResourceArns = [endpointArn],
        });

    private static bool AreAccountsLiterallySame(string first, string second)
        => !Token.IsUnresolved(first)
           && !Token.IsUnresolved(second)
           && Token.CompareStrings(first, second) == TokenComparison.SAME;

    private static bool IsEstablished(IPrincipal principal, string endpointAccount)
    {
        var principalAccount = principal is IConstruct construct && Resource.IsOwnedResource(construct)
            ? Stack.Of(construct).Account
            : principal.PrincipalAccount;

        if (principalAccount is null || Token.IsUnresolved(principalAccount))
            return false;

        return Token.CompareStrings(principalAccount, endpointAccount) == TokenComparison.SAME;
    }

    private static string DescribeEndpoint(IConstruct endpointConstruct, string endpointName)
        => $"'{endpointConstruct.Node.Path}' ('{endpointName}')";

    private static string Describe(IGrantable grantee)
    {
        if (grantee is IConstruct granteeConstruct)
            return $"'{granteeConstruct.Node.Path}'";

        var principal = grantee.GrantPrincipal;

        if (principal is IConstruct principalConstruct)
            return $"'{principalConstruct.Node.Path}'";

        var json = principal.PolicyFragment?.PrincipalJson;
        if (json is { Count: > 0 })
        {
            var rendered = string.Join(", ", json.Select(entry => $"{entry.Key}={RenderValue(entry.Value)}"));
            if (!rendered.Contains("${Token[", StringComparison.Ordinal) &&
                !rendered.Contains("<unresolved>", StringComparison.Ordinal))
            {
                return $"'{rendered}'";
            }
        }

        return $"'{principal.GetType().Name}'";
    }

    private static string RenderValue(object? value) => value switch
    {
        null => "null",
        string text => Token.IsUnresolved(text) ? "<unresolved>" : text,
        IEnumerable<object> values => "[" + string.Join(", ", values.Select(RenderValue)) + "]",
        _ => value.ToString() ?? "<unknown>",
    };
}
