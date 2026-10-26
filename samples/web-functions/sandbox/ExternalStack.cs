using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace WebFunctionSandbox;

/// <summary>
/// Owns a pre-provisioned artifact bucket the app stack imports by name and consumes with
/// <see cref="LayeredCraft.Cdk.Constructs.Models.WebFunctionCodeBucketPolicy.ExternallyManaged"/>. The bucket
/// owner writes the <c>lambda.amazonaws.com</c> read statement here, scoped to exactly the two function
/// revisions that read from it, so the app stack adds nothing to the policy.
/// </summary>
internal sealed class ExternalStack : Stack
{
    public ExternalStack(
        Construct scope,
        string id,
        IStackProps? props,
        SandboxConfiguration configuration,
        SandboxNaming naming)
        : base(scope, id, props)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(naming);

        naming.ApplyTags(this);

        ExternalBucket = new Bucket(this, "ExternalBucket", new BucketProps
        {
            BucketName = naming.ExternalBucketName,
            Versioned = true,
            Encryption = BucketEncryption.S3_MANAGED,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            EnforceSSL = true,
            RemovalPolicy = RemovalPolicy.DESTROY,
            AutoDeleteObjects = false,
        });

        // ExternallyManaged read grants, written by the bucket owner with the same scoping the construct's
        // Automatic policy would use, but narrowed to exactly the ASP.NET Core and Blazor web-function revision
        // ARNs that read from this bucket - never a wildcard function name. One statement per workload keeps each
        // object readable only by its own function's revisions.
        ExternalBucket.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["s3:GetObject", "s3:GetObjectVersion"],
            Resources = [$"{ExternalBucket.BucketArn}/{configuration.AspNetCore.ObjectKey}"],
            Principals = [new ServicePrincipal("lambda.amazonaws.com")],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string>
                {
                    ["aws:SourceAccount"] = configuration.Account,
                },
                ["ArnLike"] = new Dictionary<string, string>
                {
                    ["aws:SourceArn"] =
                        $"arn:aws:lambda:{configuration.Region}:{configuration.Account}:web-function/{naming.AspNetCoreFunctionName}/revision/*",
                },
            },
        }));

        ExternalBucket.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["s3:GetObject", "s3:GetObjectVersion"],
            Resources = [$"{ExternalBucket.BucketArn}/{configuration.Blazor.ObjectKey}"],
            Principals = [new ServicePrincipal("lambda.amazonaws.com")],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string>
                {
                    ["aws:SourceAccount"] = configuration.Account,
                },
                ["ArnLike"] = new Dictionary<string, string>
                {
                    ["aws:SourceArn"] =
                        $"arn:aws:lambda:{configuration.Region}:{configuration.Account}:web-function/{naming.BlazorFunctionName}/revision/*",
                },
            },
        }));

        // Two owned roles the app stack imports by literal ARN and grants invoke with the default Automatic caller
        // policy. Because each is imported (not an owned mutable identity) and its literal account matches the
        // endpoint's, the library records each grant in the function's resource policy only - the callers carry no
        // identity policy. One is imported with Mutable = false and one with the default (mutable), so both
        // resource-policy-only sub-cases in §19.1 3.3 are exercised.
        ImportedCallerImmutableRole = new Role(this, "ImportedCallerImmutableRole", new RoleProps
        {
            RoleName = naming.ImportedCallerImmutableRoleName,
            AssumedBy = new AccountPrincipal(configuration.Account),
            Description = "Sandbox role imported with Mutable = false (resource-policy-only invoke authorization).",
        });

        ImportedCallerMutableRole = new Role(this, "ImportedCallerMutableRole", new RoleProps
        {
            RoleName = naming.ImportedCallerMutableRoleName,
            AssumedBy = new AccountPrincipal(configuration.Account),
            Description = "Sandbox role imported with the default mutable setting (resource-policy-only invoke authorization).",
        });

        _ = new CfnOutput(this, "ExternalBucketName", new CfnOutputProps
        {
            Value = ExternalBucket.BucketName,
            Description = "Versioned S3 bucket holding externally managed sandbox artifacts.",
            ExportName = $"{naming.Prefix}-external-bucket-name",
        });

        _ = new CfnOutput(this, "ImportedCallerImmutableRoleArn", new CfnOutputProps
        {
            Value = ImportedCallerImmutableRole.RoleArn,
            Description = "Literal ARN of the imported caller role created with Mutable = false.",
            ExportName = $"{naming.Prefix}-imported-caller-immutable-role-arn",
        });

        _ = new CfnOutput(this, "ImportedCallerMutableRoleArn", new CfnOutputProps
        {
            Value = ImportedCallerMutableRole.RoleArn,
            Description = "Literal ARN of the imported caller role created with the default mutable setting.",
            ExportName = $"{naming.Prefix}-imported-caller-mutable-role-arn",
        });
    }

    public IBucket ExternalBucket { get; }

    public IRole ImportedCallerImmutableRole { get; }

    public IRole ImportedCallerMutableRole { get; }
}