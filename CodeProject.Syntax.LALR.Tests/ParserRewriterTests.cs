using System;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Schema;
using Xunit;

namespace CodeProject.Syntax.LALR.Tests;

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
}
