using System.Collections.Generic;

namespace CodeProject.Syntax.LALR.SourceGenerators;

/// <summary>
/// Generator-side mirror of the runtime <c>GrammarSchema</c> POCOs. Lives in the
/// netstandard2.0 generator assembly so YamlDotNet can deserialise into it without
/// needing a reference to the net10.0 runtime library. The shapes must match the
/// runtime POCOs field-for-field; the generator emits C# code referencing the runtime
/// types, so any drift here surfaces immediately as a compile error in generator
/// tests.
/// </summary>
internal sealed class YamlGrammarSchema
{
    public List<string> Symbols { get; set; } = new();
    public List<YamlProductionGroup> Productions { get; set; } = new();
    public Dictionary<string, List<YamlLexRule>> Lexer { get; set; } = new();
    public YamlActions? Actions { get; set; }
}

internal sealed class YamlProductionGroup
{
    /// <summary>Lower-case string in YAML: <c>none</c>, <c>leftmost</c>, <c>rightmost</c>.</summary>
    public string Derivation { get; set; } = "none";
    public List<YamlProduction> Rules { get; set; } = new();
}

internal sealed class YamlProduction
{
    public string Lhs { get; set; } = "";
    public List<string> Rhs { get; set; } = new();
    public string? Action { get; set; }
}

internal sealed class YamlLexRule
{
    public string Symbol { get; set; } = "";
    public string Match { get; set; } = "";
    public string? Push { get; set; }
    public bool Pop { get; set; }
    public string? Action { get; set; }
}

internal sealed class YamlActions
{
    public string? ClassName { get; set; }
}
