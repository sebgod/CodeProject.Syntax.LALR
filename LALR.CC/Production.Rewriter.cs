using System;
using LALR.CC.LexicalGrammar;

namespace LALR.CC;

/// <summary>
/// Runtime-only partial of <see cref="Production"/>: the strongly-typed
/// rewriter constructor and <c>Rewrite</c> method live here so the rest of
/// <see cref="Production"/> can be `&lt;Compile Link&gt;`-shared into the
/// netstandard2.0 source generator (which has no <c>LexicalGrammar</c> in
/// its closure). The generator only ever constructs productions via
/// <c>new Production(int left, params int[] right)</c> — the rewriter ctor
/// is parse-time-only.
///
/// The backing field <c>_rewriter</c> is declared in the linkable partial
/// (typed as <see cref="Delegate"/>); we cast it back to the strongly-typed
/// <c>Func&lt;int, Item[], object&gt;</c> on invocation here.
/// </summary>
public readonly partial struct Production
{
    public Production(int left, Func<int, Item[], object> rewriter, params int[] right)
    {
        Left = left;
        Right = right;
        _rewriter = rewriter;
    }

    /// <summary>
    /// Invoke the rewriter against the matched children. Returns the rewriter's
    /// output (which the parser stores as the reduced item's <c>Content</c>);
    /// null when no rewriter was supplied. Callers distinguish "no rewriter" from
    /// "rewriter returned null" via <see cref="HasRewriter"/> — the parser
    /// uses that to decide between the user-returned null (legitimate, e.g. a
    /// JSON visitor for JSON's `null` literal) and a default <see cref="Reduction"/>.
    /// </summary>
    public object Rewrite(Item[] children)
    {
        return ((Func<int, Item[], object>)_rewriter)?.Invoke(Left, children);
    }
}
