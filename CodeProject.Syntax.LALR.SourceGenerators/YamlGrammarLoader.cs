using CodeProject.Syntax.LALR.Schema;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeProject.Syntax.LALR.SourceGenerators;

/// <summary>
/// Wraps YamlDotNet with the project-specific configuration: camelCase property
/// names for the schema POCOs (Symbols ↔ symbols, ClassName ↔ className), lower-case
/// enum names so <c>derivation: leftmost</c> binds to <see cref="Derivation.LeftMost"/>,
/// ignore unmatched fields for forward-compat, surface parse errors as
/// <see cref="YamlException"/> with line + column info already embedded.
/// </summary>
internal static class YamlGrammarLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnumNamingConvention(LowerCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Deserialise <paramref name="yamlText"/> into <see cref="GrammarSchema"/>.
    /// Returns null on empty input. Lets <see cref="YamlException"/> propagate so
    /// the caller can convert it into a Roslyn diagnostic with file/line info.
    /// </summary>
    public static GrammarSchema? Load(string? yamlText)
    {
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            return null;
        }
        return Deserializer.Deserialize<GrammarSchema>(yamlText!);
    }

    public static Mark TryGetMark(YamlException ex) => ex?.Start ?? Mark.Empty;
}
