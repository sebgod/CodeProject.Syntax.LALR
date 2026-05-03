// File is linked into the netstandard2.0 source-generator project (where
// <Nullable>enable</Nullable>) as well as the runtime library (where Nullable is
// disabled). The schema is a bag of public POCOs — null-state isn't a useful
// invariant here — so we disable nullable locally to keep one source for both.
#nullable disable

using System.Collections.Generic;

namespace CodeProject.Syntax.LALR.Schema;

/// <summary>
/// Declarative grammar definition shaped to match the YAML format we plan to ship in
/// Phase 2 (source generator) and Phase 3 (typed AST + visitors). For now this is the
/// in-memory shape — callers (or future deserializers) populate it directly and feed
/// it to <see cref="SchemaCompiler.Compile"/> to obtain a runtime <see cref="Grammar"/>
/// and lexer table.
/// </summary>
/// <remarks>
/// Properties use plain <c>set</c> rather than <c>init</c> so System.Text.Json /
/// YamlDotNet / any reflection-based deserializer round-trips cleanly. The schema is
/// consumed once at build/load time, then thrown away — mutation isn't a concern.
/// </remarks>
public sealed class GrammarSchema
{
    /// <summary>
    /// Symbol names. Index == symbol id; index 0 is the start symbol. Both terminals
    /// and non-terminals live in this list — the compiler infers which is which from
    /// productions and lexer rules.
    /// </summary>
    public List<string> Symbols { get; set; } = [];

    /// <summary>
    /// Production groups, ordered tightest-binding first. Each group's
    /// <see cref="Derivation"/> resolves shift-reduce / reduce-reduce ambiguity within
    /// the group.
    /// </summary>
    public List<ProductionGroupSchema> Productions { get; set; } = [];

    /// <summary>
    /// Lexer rules per state. The dictionary key is the state name; the runtime always
    /// starts in <see cref="PipeBytesLexer.RootState"/> ("root") so the table must
    /// contain that key.
    /// </summary>
    public Dictionary<string, List<LexRuleSchema>> Lexer { get; set; } = new();

    /// <summary>
    /// Optional metadata for the source generator (Phase 2). The runtime
    /// <see cref="SchemaCompiler"/> ignores this.
    /// </summary>
    public ActionsSchema Actions { get; set; }
}

public sealed class ProductionGroupSchema
{
    public Derivation Derivation { get; set; } = Derivation.None;
    public List<ProductionSchema> Rules { get; set; } = [];
}

public sealed class ProductionSchema
{
    /// <summary>Left-hand-side symbol name. Must appear in <see cref="GrammarSchema.Symbols"/>.</summary>
    public string Lhs { get; set; }

    /// <summary>Right-hand-side symbol names. Empty list means an epsilon production.</summary>
    public List<string> Rhs { get; set; } = [];

    /// <summary>
    /// Name of a semantic action; resolved through the <c>actions</c> dictionary passed
    /// to <see cref="SchemaCompiler.Compile"/>. <see langword="null"/> or empty means
    /// "no rewriter; use the default <see cref="Reduction"/>".
    /// </summary>
    public string Action { get; set; }
}

public sealed class LexRuleSchema
{
    /// <summary>Symbol name to emit on match. Must appear in <see cref="GrammarSchema.Symbols"/>.</summary>
    public string Symbol { get; set; }

    /// <summary>
    /// Pattern to match, in the small regex-like dialect documented on
    /// <see cref="IRxParser.Parse"/>. Quoted-string literals from YAML/JSON come in
    /// here verbatim — escapes are interpreted by the regex parser, not the host.
    /// </summary>
    public string Match { get; set; }

    /// <summary>State name to push on match. Mutually exclusive with <see cref="Pop"/> and <see cref="Action"/>.</summary>
    public string Push { get; set; }

    /// <summary>True if matching this rule should pop one state off the stack.</summary>
    public bool Pop { get; set; }

    /// <summary>
    /// Built-in action keyword. Currently only <c>"ignore"</c> is supported (drops the
    /// matched token entirely). Mutually exclusive with <see cref="Push"/> and <see cref="Pop"/>.
    /// </summary>
    public string Action { get; set; }
}

public sealed class ActionsSchema
{
    /// <summary>Class name the source generator (Phase 2) will use for the actions partial class.</summary>
    public string ClassName { get; set; }
}
