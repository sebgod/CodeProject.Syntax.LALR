using System;
using System.Threading.Tasks;
using LALR.CC.LexicalGrammar;
using LALR.CC.Schema;
using Xunit;

namespace LALR.CC.Tests;

/// <summary>
/// Regression tests for two parser-loop quirks the JSON example surfaced:
/// (a) <c>Production.Rewrite ?? new Reduction(...)</c> conflated "rewriter
///     returned null" with "no rewriter present", swallowing legitimate null
///     content (e.g. a JSON visitor returning C# null for the JSON null literal).
/// (b) <see cref="Parser.ParseInputAsync"/> dereferenced its <c>debugger</c>
///     parameter directly each loop iteration; a null debugger NREd in Debug
///     builds even though the trace methods are <c>[Conditional("DEBUG")]</c>.
/// Both are now fixed via <see cref="Production.HasRewriter"/> + null-conditional
/// debugger calls, respectively. These tests pin both contracts.
/// </summary>
public class ParserRewriterTests
{
    /// <summary>
    /// Build a tiny grammar (V -> x; lexer matches a single 'x') with the supplied
    /// rewriter. Drives the parser end-to-end, no debugger attached. Returns the
    /// root reduction's content for the caller to assert on.
    /// </summary>
    private static async Task<object> ParseAsync(Func<int, Item[], object> rewriter)
    {
        var schema = new GrammarSchema
        {
            Symbols = ["V", "x"],
            Productions =
            {
                new ProductionGroupSchema
                {
                    Derivation = Derivation.None,
                    Rules =
                    {
                        // V -> x. Action name is "passthrough" — actions[] supplies the rewriter.
                        new ProductionSchema { Lhs = "V", Rhs = { "x" }, Action = "passthrough" },
                    },
                },
            },
            Lexer =
            {
                ["root"] = new()
                {
                    new LexRuleSchema { Symbol = "x", Match = "x" },
                },
            },
        };

        var actions = new System.Collections.Generic.Dictionary<string, Func<int, Item[], object>>
        {
            ["passthrough"] = rewriter,
        };

        var (grammar, lexerTable) = SchemaCompiler.Compile(schema, actions);
        var parser = new Parser(grammar);
        using var lexer = PipeBytesLexer.FromString("x", lexerTable,
            cancellationToken: TestContext.Current.CancellationToken);
        using var tokens = new AsyncLATokenIterator(lexer);

        // No debugger — exercises the null-conditional fix on every loop iteration.
        var result = await parser.ParseInputAsync(tokens, debugger: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsError);
        return result.Content;
    }

    [Fact]
    public async Task RewriterReturningNull_PreservesNullContent()
    {
        // Pre-fix this returned a default Reduction object (because the parser's
        // `production.Rewrite(children) ?? new Reduction(...)` collapsed null to
        // the fallback). Post-fix, Production.HasRewriter drives the choice, so
        // a rewriter that returns null is honoured — Content stays null.
        var content = await ParseAsync(rewriter: (lhs, rhs) => null);
        Assert.Null(content);
    }

    [Fact]
    public async Task RewriterReturningValue_ContentIsThatValue()
    {
        // Sanity check that the non-null path still works the same way.
        var content = await ParseAsync(rewriter: (lhs, rhs) => "hello");
        Assert.Equal("hello", content);
    }

    [Fact]
    public async Task NullDebugger_DoesNotThrow()
    {
        // The two tests above already pass debugger:null; if either gets here
        // without throwing, the null-conditional debugger path works. Keeping
        // this as an explicit test makes the contract visible in test names.
        var content = await ParseAsync(rewriter: (lhs, rhs) => 42);
        Assert.Equal(42, content);
    }

    /// <summary>
    /// allowRewriting:false skips the rewriter entirely and forces the parser
    /// to wrap each non-trimmed reduce frame in a fresh <see cref="Reduction"/>.
    /// Surfaced via the TUI: with stub null-returning actions, the default
    /// (allowRewriting:true) collapsed the parse tree into a Scalar Item with
    /// null Content, which then rendered as a single "S 1:1" leaf instead of
    /// the expected reduction tree. allowRewriting:false avoids that without
    /// having to keep a parallel "real" rewriter dictionary around.
    /// </summary>
    [Fact]
    public async Task AllowRewritingFalse_ProducesReductionEvenWithNullStubAction()
    {
        var schema = new GrammarSchema
        {
            Symbols = ["S", "E", "+", "n"],
            Productions =
            {
                new ProductionGroupSchema { Derivation = Derivation.None,
                    Rules = { new ProductionSchema { Lhs = "S", Rhs = { "E" } } } },
                new ProductionGroupSchema { Derivation = Derivation.LeftMost,
                    Rules =
                    {
                        new ProductionSchema { Lhs = "E", Rhs = { "E", "+", "E" }, Action = "add" },
                        new ProductionSchema { Lhs = "E", Rhs = { "n" },           Action = "number" },
                    } },
            },
            Lexer = { ["root"] = new()
            {
                new LexRuleSchema { Symbol = "n", Match = "[0-9]+" },
                new LexRuleSchema { Symbol = "+", Match = "\\+" },
            } },
        };

        // Both stub actions return null — the same shape lalr-tui uses when
        // running in observational mode. Without allowRewriting:false this
        // collapses the tree to a Scalar.
        var actions = new System.Collections.Generic.Dictionary<string, Func<int, Item[], object>>
        {
            ["add"] = (_, _) => null!,
            ["number"] = (_, _) => null!,
        };

        var (grammar, lexerTable) = SchemaCompiler.Compile(schema, actions);
        var parser = new Parser(grammar);
        using var lexer = PipeBytesLexer.FromString("1+2", lexerTable,
            cancellationToken: TestContext.Current.CancellationToken);
        using var tokens = new AsyncLATokenIterator(lexer);

        var root = await parser.ParseInputAsync(tokens, allowRewriting: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // The resulting tree is the start symbol's content. With S -> E being
        // single-nonterminal-trimmed, root is the E reduction itself (a real
        // Reduction containing the three-child E + E + E shape).
        Assert.False(root.IsError);
        Assert.Equal(ContentType.Reduction, root.ContentType);
        Assert.Equal(3, root.Reduction.Children.Count);
    }

    /// <summary>
    /// Two non-terminals adjacent in an RHS — the case the Wikipedia LaTeX
    /// example surfaced. Pre-fix, after reducing the FIRST A, the parser
    /// stashed <c>production.Left</c> (the LHS symbol id) on the reduction's
    /// <c>Item.State</c>; when the SECOND A's <c>{ I }</c> reduction then did
    /// <c>lastState = Peek().State</c> to compute its goto, it read that
    /// symbol id as if it were a parser state, mis-routing into a "just
    /// reduced an atom, trim P -&gt; A now" state. The end result was a parse
    /// error in state 0 on EOF. Post-fix, reductions carry the goto-target
    /// parser state, and the correct second-A goto fires.
    ///
    /// Pre-fix this test threw <see cref="ParseErrorException"/>; post-fix it
    /// produces a reduction tree with the expected <c>x A A</c> shape.
    /// </summary>
    [Fact]
    public async Task AdjacentNonterminalsInRhs_RoutesGotoCorrectly()
    {
        // S -> P            (start, trimmed at parse time)
        // P -> x A A        (THE rule under test — two non-terminals adjacent)
        // A -> '{' I '}'    (each A is a brace-grouped reduction, like \frac's args)
        // I -> n            (innermost; arity-1 with terminal RHS so it doesn't trim)
        var schema = new GrammarSchema
        {
            Symbols = ["S", "P", "A", "I", "x", "{", "}", "n"],
            Productions =
            {
                new ProductionGroupSchema { Derivation = Derivation.None, Rules =
                {
                    new ProductionSchema { Lhs = "S", Rhs = { "P" } },
                    new ProductionSchema { Lhs = "P", Rhs = { "x", "A", "A" } },
                    new ProductionSchema { Lhs = "A", Rhs = { "{", "I", "}" } },
                    new ProductionSchema { Lhs = "I", Rhs = { "n" } },
                } },
            },
            Lexer = { ["root"] = new()
            {
                new LexRuleSchema { Symbol = "x", Match = "x" },
                new LexRuleSchema { Symbol = "{", Match = "\\{" },
                new LexRuleSchema { Symbol = "}", Match = "\\}" },
                new LexRuleSchema { Symbol = "n", Match = "[0-9]+" },
            } },
        };

        var (grammar, lexerTable) = SchemaCompiler.Compile(schema);
        var parser = new Parser(grammar);
        using var lexer = PipeBytesLexer.FromString("x{1}{2}", lexerTable,
            cancellationToken: TestContext.Current.CancellationToken);
        using var tokens = new AsyncLATokenIterator(lexer);

        var root = await parser.ParseInputAsync(tokens,
            cancellationToken: TestContext.Current.CancellationToken);

        // The accept yields the trimmed S, which carries the P reduction's
        // content directly. P -> x A A has 3 children: terminal x and two A's.
        Assert.False(root.IsError);
        Assert.Equal(ContentType.Reduction, root.ContentType);
        Assert.Equal(3, root.Reduction.Children.Count);
        // Each A child is itself a 3-child '{ I }' reduction — confirms the
        // second arg's brace-group parsed as A, not as a stray P -> A trim.
        Assert.Equal(ContentType.Reduction, root.Reduction.Children[1].ContentType);
        Assert.Equal(ContentType.Reduction, root.Reduction.Children[2].ContentType);
        Assert.Equal(3, root.Reduction.Children[1].Reduction.Children.Count);
        Assert.Equal(3, root.Reduction.Children[2].Reduction.Children.Count);
    }
}
