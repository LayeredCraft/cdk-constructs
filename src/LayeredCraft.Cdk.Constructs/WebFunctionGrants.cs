using System;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// Shared implementation of the Web Function <c>GrantInvoke</c> rules (design §10.2, revision 3). The library
/// writes an identity policy only for a principal whose account it can establish; every other case uses the
/// function's single resource policy, and a grant that cannot be fully configured fails at synthesis.
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

        if ((options?.CallerPolicy ?? WebFunctionCallerPolicy.Automatic) == WebFunctionCallerPolicy.ExternallyManaged)
        {
            var resourceGrant = AddResourceStatement(
                resourcePolicyOwner, grantee, principal, endpointArn, endpointName);

            Annotations.Of(endpointConstruct).AddInfo(
                $"Invocation by {Describe(grantee)} requires an identity policy in the caller's account allowing " +
                $"'{InvokeAction}' on {endpointArn}.");

            return resourceGrant;
        }

        if (!IsEstablished(principal, endpointAccount))
        {
            if (principal is ServicePrincipal)
                return AddResourceStatement(resourcePolicyOwner, grantee, principal, endpointArn, endpointName);

            throw new InvalidOperationException(
                $"Invoke access to Web Function endpoint '{endpointName}' cannot be fully configured for " +
                $"{Describe(grantee)}. The caller's account cannot be established in this deployment, so the " +
                "library will not write a cross-account identity policy. Give the caller and the endpoint matching " +
                "explicit environment accounts (or import the caller with a literal ARN in the deployment account), " +
                "or pass CallerPolicy = ExternallyManaged and configure the caller's identity policy in its own " +
                "account.");
        }

        var isOwnedPrincipal = principal is IConstruct owned && Resource.IsOwnedResource(owned);

        if (isOwnedPrincipal)
        {
            var ownedIdentityGrant = Grant.AddToPrincipal(new GrantOnPrincipalOptions
            {
                Grantee = grantee,
                Actions = [InvokeAction],
                ResourceArns = [endpointArn],
            });

            if (ownedIdentityGrant.PrincipalStatements.Length > 0)
                return ownedIdentityGrant;
        }
        else if (IdentityPolicyCanBeWritten(grantee, endpointArn))
        {
            return Grant.AddToPrincipal(new GrantOnPrincipalOptions
            {
                Grantee = grantee,
                Actions = [InvokeAction],
                ResourceArns = [endpointArn],
            });
        }

        // The account is established but the identity policy could not be attached (an immutable same-account
        // imported role), so the same-account grant falls back to the resource policy.
        return AddResourceStatement(resourcePolicyOwner, grantee, principal, endpointArn, endpointName);
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

    private static bool IdentityPolicyCanBeWritten(IGrantable grantee, string endpointArn)
    {
        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = [InvokeAction],
            Resources = [endpointArn],
        });

        var result = grantee.GrantPrincipal.AddToPrincipalPolicy(statement);

        // A mutable imported role attaches a real policy (an IConstruct); an immutable role reports success but
        // writes nothing and returns a bare DependencyGroup. See design §10.2.
        return result.StatementAdded && result.PolicyDependable is IConstruct;
    }

    private static bool IsEstablished(IPrincipal principal, string? endpointAccount)
    {
        if (principal is IConstruct construct && Resource.IsOwnedResource(construct))
        {
            if (endpointAccount is null)
                return true;

            var principalAccount = Stack.Of(construct).Account;
            return Token.CompareStrings(principalAccount, endpointAccount) != TokenComparison.DIFFERENT;
        }

        if (endpointAccount is null || Token.IsUnresolved(endpointAccount))
            return false;

        var account = principal.PrincipalAccount;
        if (account is null || Token.IsUnresolved(account))
            return false;

        return Token.CompareStrings(account, endpointAccount) == TokenComparison.SAME;
    }

    private static string Describe(IGrantable grantee)
    {
        var principal = grantee.GrantPrincipal;
        return principal is IConstruct construct
            ? $"'{construct.Node.Path}'"
            : $"'{principal.GetType().Name}'";
    }
}
