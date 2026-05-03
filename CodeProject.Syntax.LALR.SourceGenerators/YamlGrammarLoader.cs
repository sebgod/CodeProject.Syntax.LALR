using System;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeProject.Syntax.LALR.SourceGenerators;

/// <summary>
/// Wraps YamlDotNet with the project-specific configuration: camelCase property
/// names, ignore unmatched fields (forward compat), surface parse errors via
/// <see cref="YamlException"/> with line + column info already included.
/// </summary>
internal static class YamlGrammarLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Deserialise <paramref name="yamlText"/> into <see cref="YamlGrammarSchema"/>.
    /// Returns null on empty input. Lets <see cref="YamlException"/> propagate so
    /// the caller can convert it into a Roslyn diagnostic with file/line info.
    /// </summary>
    public static YamlGrammarSchema? Load(string? yamlText)
    {
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            return null;
        }
        return Deserializer.Deserialize<YamlGrammarSchema>(yamlText!);
    }

    public static Mark TryGetMark(YamlException ex) => ex?.Start ?? Mark.Empty;

    public static bool TryParseDerivation(string? raw, out string canonical)
    {
        // Three accepted spellings, mapped to the runtime enum names emitted later.
        canonical = raw?.Trim().ToLowerInvariant() switch
        {
            null or "" or "none" => "None",
            "leftmost" => "LeftMost",
            "rightmost" => "RightMost",
            _ => "",
        };
        return canonical.Length > 0;
    }
}
