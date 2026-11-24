using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;

namespace LayeredCraft.Cdk.Constructs.Models;

public interface ILambdaFunctionConstructProps
{
    string FunctionName { get; set; }
    string FunctionSuffix { get; set; }

    /// <summary>Path to a local Lambda deployment package. Exactly one of <see cref="AssetPath"/> or <see cref="Code"/> must be set.</summary>
    string? AssetPath { get; set; }

    /// <summary>Existing Lambda code source. Exactly one of <see cref="AssetPath"/> or <see cref="Code"/> must be set.</summary>
    Code? Code { get; set; }

    /// <summary>Name of the alias pointing at the published version (default: live).</summary>
    string AliasName { get; set; }

    string RoleName { get; set; }
    string PolicyName { get; set; }
    double MemorySize { get; set; }
    double TimeoutInSeconds { get; set; }
    PolicyStatement[] PolicyStatements { get; set; }
    IDictionary<string, string> EnvironmentVariables { get; set; }
    bool IncludeOtelLayer { get; set; }
    string OtelLayerVersion { get; set; }
    string Architecture { get; set; }
    List<LambdaPermission> Permissions { get; set; }
    bool EnableSnapStart { get; set; }
    bool GenerateUrl { get; set; }
}
public sealed record LambdaFunctionConstructProps : ILambdaFunctionConstructProps
{
    public required string FunctionName { get; set; }
    public required string FunctionSuffix { get; set; }

    /// <summary>Path to a local Lambda deployment package. Exactly one of <see cref="AssetPath"/> or <see cref="Code"/> must be set.</summary>
    public string? AssetPath { get; set; }

    /// <summary>Existing Lambda code source. Exactly one of <see cref="AssetPath"/> or <see cref="Code"/> must be set.</summary>
    public Code? Code { get; set; }

    /// <summary>Name of the alias pointing at the published version (default: live).</summary>
    public string AliasName { get; set; } = "live";

    public required string RoleName { get; set; }
    public required string PolicyName { get; set; }
    public double MemorySize { get; set; } = 1024;
    public double TimeoutInSeconds { get; set; } = 6;
    public PolicyStatement[] PolicyStatements { get; set; } = [];
    public IDictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();
    public bool IncludeOtelLayer { get; set; } = false;
    public string OtelLayerVersion { get; set; } = "0-117-0";
    public string Architecture { get; set; } = "amd64";
    public List<LambdaPermission> Permissions { get; set; } = [];
    public bool EnableSnapStart { get; set; }
    public bool GenerateUrl { get; set; }
}