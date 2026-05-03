using System.Linq;
using CodeProject.Syntax.LALR;
using Xunit;

namespace CodeProject.Syntax.LALR.Tests;

public class GrammarConflictTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Test grammars
    // ──────────────────────────────────────────────────────────────────────────

    // Symbols for the classic ambiguous expression grammar:
    //   S' -> E
    //   E  -> E + E
    //   E  -> i
    private const int Sp = 0;
    private const int E = 1;
    private const int Plus = 2;
    private const int I = 3;
    private static readonly string[] _exprSymbols = ["S'", "E", "+", "i"];

    private static Grammar AmbiguousExprGrammar(Derivation derivation) => new(
        _exprSymbols,
        new PrecedenceGroup(derivation,
            new Production(Sp, E),
            new Production(E, E, Plus, E),
            new Production(E, I)));

    // Symbols for an R/R-conflict grammar:
    //   S' -> S
    //   S  -> A b
    //   S  -> B b
    //   A  -> a
    //   B  -> a
    // On input "a b" the parser must choose between A->a and B->a after consuming 'a'.
    private const int RrSp = 0;
    private const int RrS = 1;
    private const int RrA = 2;
    private const int RrB = 3;
    private const int RrLowerA = 4;
    private const int RrLowerB = 5;
    private static readonly string[] _rrSymbols = ["S'", "S", "A", "B", "a", "b"];

    private static Grammar ReduceReduceGrammar() => new(
        _rrSymbols,
        new PrecedenceGroup(Derivation.None,
            new Production(RrSp, RrS),
            new Production(RrS, RrA, RrLowerB),
            new Production(RrS, RrB, RrLowerB),
            new Production(RrA, RrLowerA),
            new Production(RrB, RrLowerA)));

    // ──────────────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CleanGrammar_ConstructsWithoutThrowing()
    {
        // S' -> E, E -> i — no ambiguity at all.
        var grammar = new Grammar(
            ["S'", "E", "i"],
            new PrecedenceGroup(Derivation.None,
                new Production(0, 1),
                new Production(1, 2)));
        var parser = new Parser(grammar);
        Assert.Empty(parser.Conflicts);
    }

    [Fact]
    public void AmbiguousExprWithDerivationNone_ThrowsShiftReduce()
    {
        var ex = Assert.Throws<GrammarConflictException>(() => new Parser(AmbiguousExprGrammar(Derivation.None)));

        Assert.NotEmpty(ex.Conflicts);
        Assert.Contains(ex.Conflicts, c => c.Kind == ConflictKind.ShiftReduce);

        var sr = ex.Conflicts.First(c => c.Kind == ConflictKind.ShiftReduce);
        Assert.Equal(Plus, sr.LookaheadSymbolId); // ambiguity surfaces on the '+' lookahead
        Assert.True(sr.ShiftTargetState >= 0);
        Assert.NotEmpty(sr.ReduceProductionIds);
    }

    [Fact]
    public void AmbiguousExpr_LeftMostDerivation_ResolvesConflict()
    {
        // Derivation.LeftMost picks the reduce, so the same grammar should now build clean.
        var parser = new Parser(AmbiguousExprGrammar(Derivation.LeftMost));
        Assert.Empty(parser.Conflicts);
    }

    [Fact]
    public void AmbiguousExpr_RightMostDerivation_ResolvesConflict()
    {
        var parser = new Parser(AmbiguousExprGrammar(Derivation.RightMost));
        Assert.Empty(parser.Conflicts);
    }

    [Fact]
    public void ReduceReduceGrammar_Throws()
    {
        var ex = Assert.Throws<GrammarConflictException>(() => new Parser(ReduceReduceGrammar()));

        Assert.Contains(ex.Conflicts, c => c.Kind == ConflictKind.ReduceReduce);

        var rr = ex.Conflicts.First(c => c.Kind == ConflictKind.ReduceReduce);
        Assert.Equal(RrLowerB, rr.LookaheadSymbolId); // reduce decision is forced by the 'b' lookahead
        Assert.Equal(-1, rr.ShiftTargetState);
        Assert.Equal(2, rr.ReduceProductionIds.Length); // A->a and B->a
    }

    [Fact]
    public void ExceptionMessage_NamesLookaheadAndProductions()
    {
        var ex = Assert.Throws<GrammarConflictException>(() => new Parser(AmbiguousExprGrammar(Derivation.None)));

        // The message should mention the offending symbol and at least one production's RHS.
        Assert.Contains("'+'", ex.Message);
        Assert.Contains("E -> E + E", ex.Message);
        Assert.Contains("SHIFT-REDUCE", ex.Message);
        // And it should hint at the resolution.
        Assert.Contains("PrecedenceGroup", ex.Message);
    }

    [Fact]
    public void ExceptionMessage_ReduceReduceShape()
    {
        var ex = Assert.Throws<GrammarConflictException>(() => new Parser(ReduceReduceGrammar()));

        Assert.Contains("REDUCE-REDUCE", ex.Message);
        Assert.Contains("'b'", ex.Message);
        Assert.Contains("A -> a", ex.Message);
        Assert.Contains("B -> a", ex.Message);
    }
}
