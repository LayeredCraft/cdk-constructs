using Amazon.CDK.Assertions;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Extension methods for asserting Cognito resources in CDK templates.
/// These helpers simplify common testing scenarios for consumers of the library.
/// </summary>
public static class CognitoUserPoolConstructAssertions
{
    /// <summary>
    /// Asserts that the template contains a Cognito user pool with the specified name.
    /// </summary>
    public static void ShouldHaveUserPool(this Template template, string userPoolName)
    {
        template.HasResourceProperties("AWS::Cognito::UserPool", Match.ObjectLike(new Dictionary<string, object>
        {
            { "UserPoolName", userPoolName },
        }));
    }

    /// <summary>
    /// Asserts that the template contains a Cognito-hosted user pool domain with the specified prefix.
    /// </summary>
    public static void ShouldHaveCognitoUserPoolDomain(this Template template, string domainPrefix)
    {
        template.HasResourceProperties("AWS::Cognito::UserPoolDomain", Match.ObjectLike(new Dictionary<string, object>
        {
            { "Domain", domainPrefix },
        }));
    }

    /// <summary>
    /// Asserts that the template contains a user pool app client with the specified name.
    /// </summary>
    public static void ShouldHaveUserPoolClient(this Template template, string clientName)
    {
        template.HasResourceProperties("AWS::Cognito::UserPoolClient", Match.ObjectLike(new Dictionary<string, object>
        {
            { "ClientName", clientName },
        }));
    }

    /// <summary>
    /// Asserts that the template contains a user pool resource server with the specified identifier.
    /// </summary>
    public static void ShouldHaveResourceServer(this Template template, string identifier)
    {
        template.HasResourceProperties("AWS::Cognito::UserPoolResourceServer", Match.ObjectLike(new Dictionary<string, object>
        {
            { "Identifier", identifier },
        }));
    }

    /// <summary>
    /// Asserts that the template contains a user pool group with the specified name.
    /// </summary>
    public static void ShouldHaveUserPoolGroup(this Template template, string groupName)
    {
        template.HasResourceProperties("AWS::Cognito::UserPoolGroup", Match.ObjectLike(new Dictionary<string, object>
        {
            { "GroupName", groupName },
        }));
    }

    /// <summary>
    /// Asserts that the template contains exactly one <c>AWS::Cognito::ManagedLoginBranding</c> resource
    /// and that it carries a <c>ClientId</c> property linking it to an app client.
    /// </summary>
    public static void ShouldHaveManagedLoginBranding(this Template template)
    {
        template.ResourceCountIs("AWS::Cognito::ManagedLoginBranding", 1);

        var brandings = template.FindResources("AWS::Cognito::ManagedLoginBranding");
        var props = (IDictionary<string, object>)brandings.Values.First()["Properties"];

        if (!props.ContainsKey("ClientId"))
        {
            throw new InvalidOperationException(
                "Expected AWS::Cognito::ManagedLoginBranding to have a ClientId property linking it to an app client.");
        }

        if (!props.ContainsKey("Settings"))
        {
            throw new InvalidOperationException(
                "Expected AWS::Cognito::ManagedLoginBranding to have a Settings property.");
        }
    }

    /// <summary>
    /// Asserts that the template contains no <c>AWS::Cognito::ManagedLoginBranding</c> resources.
    /// </summary>
    public static void ShouldNotHaveManagedLoginBranding(this Template template)
    {
        template.ResourceCountIs("AWS::Cognito::ManagedLoginBranding", 0);
    }

    /// <summary>
    /// Asserts that the template exports the user pool ID with the expected export name
    /// (<c>{stackName}-{constructId}-user-pool-id</c>).
    /// </summary>
    public static void ShouldExportUserPoolId(this Template template, string stackName, string constructId)
    {
        var exportName = $"{stackName.ToLowerInvariant()}-{constructId.ToLowerInvariant()}-user-pool-id";
        AssertExportExists(template, exportName);
    }

    /// <summary>
    /// Asserts that the template exports the user pool ARN with the expected export name
    /// (<c>{stackName}-{constructId}-user-pool-arn</c>).
    /// </summary>
    public static void ShouldExportUserPoolArn(this Template template, string stackName, string constructId)
    {
        var exportName = $"{stackName.ToLowerInvariant()}-{constructId.ToLowerInvariant()}-user-pool-arn";
        AssertExportExists(template, exportName);
    }

    /// <summary>
    /// Asserts that the template exports the app client ID for the given client name with the expected export name
    /// (<c>{stackName}-{constructId}-client-{clientName}-id</c>).
    /// </summary>
    public static void ShouldExportAppClientId(this Template template, string stackName, string constructId, string clientName)
    {
        var sanitized = clientName.ToLowerInvariant().Replace(' ', '-');
        var exportName = $"{stackName.ToLowerInvariant()}-{constructId.ToLowerInvariant()}-client-{sanitized}-id";
        AssertExportExists(template, exportName);
    }

    private static void AssertExportExists(Template template, string exportName)
    {
        var json = template.ToJSON();
        if (!json.TryGetValue("Outputs", out var outputsObj))
        {
            throw new InvalidOperationException($"Template has no Outputs section. Expected export '{exportName}'.");
        }

        var outputs = (IDictionary<string, object>)outputsObj;
        foreach (var output in outputs.Values)
        {
            var outputDict = (IDictionary<string, object>)output;
            if (outputDict.TryGetValue("Export", out var exportObj))
            {
                var export = (IDictionary<string, object>)exportObj;
                if (export.TryGetValue("Name", out var name) && name?.ToString() == exportName)
                    return;
            }
        }

        throw new InvalidOperationException($"Expected CloudFormation export '{exportName}' was not found in the template.");
    }
}
