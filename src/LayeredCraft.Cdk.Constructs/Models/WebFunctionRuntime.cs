using System;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// A Lambda Web Function runtime identifier.
/// </summary>
public sealed class WebFunctionRuntime
{
    /// <summary>The Node.js 24.x managed runtime (<c>nodejs24.x</c>).</summary>
    public static readonly WebFunctionRuntime NodeJs24X = new("nodejs24.x");

    private WebFunctionRuntime(string name) => Name = name;

    /// <summary>
    /// Creates a runtime for a name AWS adds before this package does.
    /// </summary>
    /// <param name="name">The Lambda runtime identifier, for example <c>nodejs24.x</c>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public static WebFunctionRuntime Of(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Runtime name must be a non-empty value.", nameof(name));

        return new WebFunctionRuntime(name);
    }

    /// <summary>The Lambda runtime identifier.</summary>
    public string Name { get; }
}
