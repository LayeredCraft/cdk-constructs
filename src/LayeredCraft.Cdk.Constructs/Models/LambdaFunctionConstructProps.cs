using Amazon.CDK.AWS.IAM;

namespace LayeredCraft.Cdk.Constructs.Models;

public interface ILambdaFunctionConstructProps
{
    string FunctionName { get; set; }
    string FunctionSuffix { get; set; }
    string AssetPath { get; set; }
    string RoleName { get; set; }
    string PolicyName { get; set; }
    PolicyStatement[] PolicyStatements { get; set; }
    IDictionary<string, string> EnvironmentVariables { get; set; }
    bool IncludeOtelLayer { get; set; }
    List<LambdaPermission> Permissions { get; set; }
    bool EnableSnapStart { get; set; }
}
public sealed record LambdaFunctionConstructProps : ILambdaFunctionConstructProps
{
    public required string FunctionName { get; set; }
    public required string FunctionSuffix { get; set; }
    public required string AssetPath { get; set; }
    public required string RoleName { get; set; }
    public required string PolicyName { get; set; }
    public PolicyStatement[] PolicyStatements { get; set; } = [];
    public IDictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();
    public bool IncludeOtelLayer { get; set; } = true;
    public List<LambdaPermission> Permissions { get; set; } = [];
    public bool EnableSnapStart { get; set; } = false;
}