using System;
using System.Collections.Generic;
using System.Linq;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR;

/// <summary>
/// Describes how to resolve ambiguities within a precedence group
/// </summary>
public enum Derivation : byte
{
    None,
    LeftMost,
    RightMost,
}

/// <summary>
/// A grammatical production
/// </summary>
public readonly struct Production
{
    private readonly Func<int, Item[], object> _rewriter;

    public int Left { get; }

    public int[] Right { get; }

    public Production(int left, params int[] right)
        : this(left, null, right)
    {
        // calls below
    }

    public Production(int left, Func<int, Item[], object> rewriter, params int[] right)
    {
        Left = left;
        Right = right;
        _rewriter = rewriter;
    }

    public object Rewrite(Item[] children)
    {
        return _rewriter?.Invoke(Left, children);
    }
}

/// <summary>
/// A reduced production with all leaf tokens and a reference to the reduced production
/// </summary>
public class Reduction(int production, params Item[] children)
{
    /// <summary>
    /// Reference to the reduced production in the production table
    /// </summary>
    public int Production { get; } = production;

    public IList<Item> Children { get; } = children;
}

/// <summary>
/// A collection of productions at a particular precedence
/// </summary>
public readonly struct PrecedenceGroup(Derivation derivation, params Production[] productions)
{
    private readonly Production[] _productions = productions;

    public Derivation Derivation { get; } = derivation;

    public IEnumerable<Production> Productions => _productions;
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
