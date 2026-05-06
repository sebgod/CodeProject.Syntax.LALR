// Opt into nullable annotations locally: the runtime project sets
// <Nullable>disable</Nullable> in Directory.Build.props, but Production._rewriter
// is deliberately annotated `Delegate?` so that the netstandard2.0 generator's
// <Nullable>enable</Nullable> view doesn't warn on the bare-ctor null assignment.
// Without this directive the runtime trips CS8632 on the `?`.
#nullable enable

using System;
using System.Linq;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR;

/// <summary>
/// A grammatical production. Linkable partial: `Left`, `Right`, the bare
/// constructor, and the rewriter-storage field (typed as the base
/// <see cref="Delegate"/> to keep this file netstandard2.0-friendly) are pure
/// C# with no <c>LexicalGrammar</c> dependency at the type level. The runtime
/// partial <see cref="Production.Rewriter"/> adds the strongly-typed
/// <c>Func&lt;int, Item[], object&gt;</c> ctor + <c>Rewrite</c> method.
///
/// `<Compile Link>`-shared into the netstandard2.0 source generator so the
/// generator can construct productions from a YAML schema at build time
/// without dragging in <c>LexicalGrammar.Item</c> &amp; co.
/// </summary>
public readonly partial struct Production
{
    // Base Delegate so this struct's storage layout is declared in a single
    // partial. The runtime partial (Production.Rewriter.cs) supplies the
    // strongly-typed Func<int, Item[], object> ctor and casts back on Rewrite.
    // Annotated nullable (Delegate?) so the netstandard2.0 generator view
    // (Nullable=enable) doesn't warn on the `null` assignment in the bare ctor.
    private readonly Delegate? _rewriter;

    public int Left { get; }

    public int[] Right { get; }

    public Production(int left, params int[] right)
    {
        Left = left;
        Right = right;
        _rewriter = null;
    }

    /// <summary>
    /// True when this production carries a semantic-action rewriter. Lives in
    /// the linkable partial (rather than next to <c>Rewrite</c> in the runtime
    /// partial) so the netstandard2.0 generator view sees `_rewriter` actually
    /// read — otherwise CS0414 trips on the field being assigned but never used.
    /// </summary>
    public bool HasRewriter => _rewriter != null;
}

/// <summary>
/// A collection of productions at a particular precedence
/// </summary>
public readonly struct PrecedenceGroup(Derivation derivation, params Production[] productions)
{
    private readonly Production[] _productions = productions;

    public Derivation Derivation { get; } = derivation;

    public System.Collections.Generic.IEnumerable<Production> Productions => _productions;
}

/// <summary>
/// All of the information required to make a Parser
/// </summary>
public readonly struct Grammar
{
    public SymbolName[] SymbolNames { get; }

    public PrecedenceGroup[] PrecedenceGroups { get; }

    public Grammar(string[] symbolNames, params PrecedenceGroup[] precedenceGroups)
        : this(AsSymbolNames(symbolNames), precedenceGroups)
    {
        // calls below
    }

    public Grammar(SymbolName[] symbolNames, params PrecedenceGroup[] precedenceGroups)
    {
        SymbolNames = symbolNames;
        PrecedenceGroups = precedenceGroups;
    }

    private static SymbolName[] AsSymbolNames(params string[] symbolNames)
    {
        return symbolNames.Select((name, index) => new SymbolName(index, name)).ToArray();
    }
}
