using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Schema;
using Xunit;

namespace CodeProject.Syntax.LALR.Tests.Schema;

public class SchemaCompilerTests
{
    /// <summary>
    /// Build a minimal arithmetic-expression schema in code, run it end-to-end through
    /// SchemaCompiler → Parser/Lexer → ParseInputAsync, and assert the parse accepts.
    /// This is the integration check that the schema POCOs + tiny regex parser +
    /// SchemaCompiler all agree on the eventual YAML format's semantics.
    /// </summary>
    [Fact]
    public async Task RoundTrip_TinyArithmeticGrammar_Accepts()
    {
        var schema = new GrammarSchema
        {
            Symbols = ["S'", "E", "+", "i"],
            Productions =
            [
                new ProductionGroupSchema
                {
                    Derivation = Derivation.None,
                    Rules =
                    [
                        new ProductionSchema { Lhs = "S'", Rhs = ["E"] },
                        new ProductionSchema { Lhs = "E",  Rhs = ["i"] },
                    ],
                },
                new ProductionGroupSchema
                {
                    Derivation = Derivation.LeftMost,
                    Rules =
                    [
                        new ProductionSchema { Lhs = "E", Rhs = ["E", "+", "E"] },
                    ],
                },
            ],
            Lexer = new Dictionary<string, List<LexRuleSchema>>
            {
                [PipeBytesLexer.RootState] =
                [
                    new LexRuleSchema { Symbol = "i",  Match = "[0-9]+" },
                    new LexRuleSchema { Symbol = "+", Match = @"\+" },
                    new LexRuleSchema { Symbol = "i",  Match = @"[ \t]+", Action = "ignore" },
                ],
            },
        };

        var (grammar, lexerTable) = SchemaCompiler.Compile(schema);
        var parser = new Parser(grammar);
        var debug = new Debug(parser, null, null);

        using var lexer = PipeBytesLexer.FromString("12 + 34", lexerTable,
            cancellationToken: TestContext.Current.CancellationToken);
        using var la = new AsyncLATokenIterator(lexer);
        var result = await parser.ParseInputAsync(la, debug,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task ActionsResolve_AndAreAppliedDuringReduction()
    {
        // Pattern reaches a single-rule reduction with an action that wraps the matched
        // text in a marker; verify the result has that marker.
        var schema = new GrammarSchema
        {
            Symbols = ["Root", "i"],
            Productions =
            [
                new ProductionGroupSchema
                {
                    Derivation = Derivation.None,
                    Rules =
                    [
                        new ProductionSchema { Lhs = "Root", Rhs = ["i"], Action = "wrap" },
                    ],
                },
            ],
            Lexer = new Dictionary<string, List<LexRuleSchema>>
            {
                [PipeBytesLexer.RootState] =
                [
                    new LexRuleSchema { Symbol = "i", Match = "[0-9]+" },
                ],
            },
        };

        var actions = new Dictionary<string, Func<int, Item[], object>>
        {
            { "wrap", (lhs, rhs) => "wrapped:" + rhs[0].Content }
        };

        var (grammar, lexerTable) = SchemaCompiler.Compile(schema, actions);
        var parser = new Parser(grammar);
        var debug = new Debug(parser, null, null);

        using var lexer = PipeBytesLexer.FromString("42", lexerTable,
            cancellationToken: TestContext.Current.CancellationToken);
        using var la = new AsyncLATokenIterator(lexer);
        var result = await parser.ParseInputAsync(la, debug,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("wrapped:42", result.Content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NullSchema_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SchemaCompiler.Compile(null));
    }

    [Fact]
    public void EmptySymbols_Throws()
    {
        var schema = new GrammarSchema { Symbols = [] };
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("symbols", ex.Message);
    }

    [Fact]
    public void DuplicateSymbol_Throws()
    {
        var schema = new GrammarSchema { Symbols = ["a", "b", "a"] };
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("duplicate", ex.Message);
    }

    [Fact]
    public void UnknownSymbolInLhs_Throws()
    {
        var schema = MinimalValidSchema();
        schema.Productions[0].Rules[0].Lhs = "DoesNotExist";
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("DoesNotExist", ex.Message);
        Assert.Contains("lhs", ex.Message);
    }

    [Fact]
    public void UnknownSymbolInRhs_Throws()
    {
        var schema = MinimalValidSchema();
        schema.Productions[0].Rules[0].Rhs = ["DoesNotExist"];
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("DoesNotExist", ex.Message);
    }

    [Fact]
    public void MissingRootLexerState_Throws()
    {
        var schema = MinimalValidSchema();
        schema.Lexer = new Dictionary<string, List<LexRuleSchema>>
        {
            ["other"] = [new LexRuleSchema { Symbol = "i", Match = "[0-9]" }],
        };
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("root", ex.Message);
    }

    [Fact]
    public void ConflictingLexerInstructions_Throws()
    {
        var schema = MinimalValidSchema();
        var rule = schema.Lexer[PipeBytesLexer.RootState][0];
        rule.Push = "other";
        rule.Pop = true;
        // Add an "other" state to satisfy the Push reference if validation checks it.
        schema.Lexer["other"] = [new LexRuleSchema { Symbol = "i", Match = "[0-9]" }];
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("at most one", ex.Message);
    }

    [Fact]
    public void UnknownPushState_Throws()
    {
        var schema = MinimalValidSchema();
        schema.Lexer[PipeBytesLexer.RootState][0].Push = "noSuchState";
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("noSuchState", ex.Message);
    }

    [Fact]
    public void UnrecognizedActionKeyword_Throws()
    {
        var schema = MinimalValidSchema();
        schema.Lexer[PipeBytesLexer.RootState][0].Action = "explode";
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("explode", ex.Message);
    }

    [Fact]
    public void BadRegex_ThrowsWithLocator()
    {
        var schema = MinimalValidSchema();
        schema.Lexer[PipeBytesLexer.RootState][0].Match = "[a-";   // unterminated class
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("lexer.root[0].match", ex.Message);
        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public void UnresolvedActionName_Throws()
    {
        var schema = MinimalValidSchema();
        schema.Productions[0].Rules[0].Action = "ghost";
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("ghost", ex.Message);
    }

    [Fact]
    public void ActionNameWithoutActionsDictionary_Throws()
    {
        var schema = MinimalValidSchema();
        schema.Productions[0].Rules[0].Action = "wrap";
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema, actions: null));
        Assert.Contains("actions", ex.Message);
    }

    /// <summary>
    /// Live-edit defence: when the YAML deserialiser produces a list with a
    /// null entry (e.g. the user types <c>- </c> with no body and triggers a
    /// recompile mid-edit), the compiler must surface a SchemaCompilationException
    /// with a path locator instead of NREing — lalr-tui's grammar editor relies
    /// on every keystroke producing a clean compile error rather than crashing.
    /// </summary>
    [Fact]
    public void NullProductionRule_ThrowsWithPathLocator()
    {
        var schema = MinimalValidSchema();
        schema.Productions[0].Rules.Add(null);
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("productions[0].rules[1]", ex.Message);
    }

    [Fact]
    public void NullProductionGroup_ThrowsWithPathLocator()
    {
        var schema = MinimalValidSchema();
        schema.Productions.Add(null);
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("productions[1]", ex.Message);
    }

    [Fact]
    public void NullLexerRule_ThrowsWithPathLocator()
    {
        var schema = MinimalValidSchema();
        schema.Lexer[PipeBytesLexer.RootState].Add(null);
        var ex = Assert.Throws<SchemaCompilationException>(() => SchemaCompiler.Compile(schema));
        Assert.Contains("lexer.root[1]", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static GrammarSchema MinimalValidSchema() => new()
    {
        Symbols = ["S'", "i"],
        Productions =
        [
            new ProductionGroupSchema
            {
                Derivation = Derivation.None,
                Rules = [new ProductionSchema { Lhs = "S'", Rhs = ["i"] }],
            },
        ],
        Lexer = new Dictionary<string, List<LexRuleSchema>>
        {
            [PipeBytesLexer.RootState] =
            [
                new LexRuleSchema { Symbol = "i", Match = "[0-9]+" },
            ],
        },
    };
}
