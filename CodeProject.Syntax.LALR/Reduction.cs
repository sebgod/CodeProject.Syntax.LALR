using System.Collections.Generic;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR;

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
