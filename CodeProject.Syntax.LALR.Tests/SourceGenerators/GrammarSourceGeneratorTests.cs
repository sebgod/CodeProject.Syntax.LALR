// The generator project is aliased (Aliases="Generators" in the csproj) because
// it ships its own copy of the linked GrammarSchema / Derivation source. Pull
// GrammarSourceGenerator in via the alias; everything else uses the runtime
// library's types via the default global alias.
extern alias Generators;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using GrammarSourceGenerator = Generators::CodeProject.Syntax.LALR.SourceGenerators.GrammarSourceGenerator;

namespace CodeProject.Syntax.LALR.Tests.SourceGenerators;

public class GrammarSourceGeneratorTests
{
    /// <summary>
    /// In-memory <see cref="AdditionalText"/> for feeding the generator under test
    /// without touching disk.
    /// </summary>
    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(content, Encoding.UTF8);
    }

    /// <summary>Drive the generator and return (generated source files, diagnostics).</summary>
    private static (ImmutableArray<SyntaxTree> GeneratedTrees, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        string yamlContent, string yamlPath = "test.lalr.yaml", string rootNamespace = "TestSpace")
    {
        // Bring along references to the BCL plus the runtime library so the
        // generated code is compilable in this test's compilation context.
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(GrammarSchema).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new GrammarSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [new InMemoryAdditionalText(yamlPath, yamlContent)],
            optionsProvider: new OptionsProvider(rootNamespace));

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        return (result.GeneratedTrees, result.Diagnostics);
    }

    /// <summary>
    /// Custom AnalyzerConfigOptionsProvider that supplies <c>build_property.RootNamespace</c>
    /// to the generator. Roslyn's default provider returns nothing, which would make every
    /// generator emit into <c>Generated</c>.
    /// </summary>
    private sealed class OptionsProvider(string rootNamespace) : AnalyzerConfigOptionsProvider
    {
        private readonly RootNsOptions _global = new(rootNamespace);

        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;

        private sealed class RootNsOptions(string rootNamespace) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.RootNamespace")
                {
                    value = rootNamespace;
                    return true;
                }
                value = null!;
                return false;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Class-name derivation
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("arithmetic.lalr.yaml", "Arithmetic")]
    [InlineData("/some/path/Bnf.lalr.yaml", "Bnf")]
    [InlineData("foo-bar.lalr.yaml", "FooBar")]
    [InlineData("9start.lalr.yaml", "_9start")]
    [InlineData(".lalr.yaml", "Grammar")]
    public void ClassNameDerivation_HandlesCommonShapes(string path, string expected)
    {
        Assert.Equal(expected, GrammarSourceGenerator.DeriveClassName(path));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Empty / minimal input
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyYaml_EmitsNothingAndNoDiagnostics()
    {
        var (trees, diags) = RunGenerator("");
        Assert.Empty(trees);
        Assert.Empty(diags);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Diagnostics on bad YAML
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MalformedYaml_ReportsLalr0001Diagnostic()
    {
        // The unbalanced brace makes YamlDotNet throw a YamlException.
        var (trees, diags) = RunGenerator("symbols: [a, b\n  not: closed");
        Assert.Empty(trees);
        Assert.Contains(diags, d => d.Id == "LALR0001");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Real schema → emitted source content + Roslyn-level compile check
    // ──────────────────────────────────────────────────────────────────────────

    private const string SampleArithmetic = """
        symbols:
          - "S'"
          - E
          - "+"
          - i
        productions:
          - derivation: none
            rules:
              - { lhs: "S'", rhs: [E] }
              - { lhs: E,  rhs: [i] }
          - derivation: leftmost
            rules:
              - { lhs: E, rhs: [E, "+", E] }
        lexer:
          root:
            - { symbol: i, match: "[0-9]+" }
            - { symbol: "+", match: "\\+" }
            - { symbol: i, match: "[ \\t]+", action: ignore }
        """;

    [Fact]
    public void SampleArithmetic_EmitsExpectedShape()
    {
        var (trees, diags) = RunGenerator(SampleArithmetic, "arithmetic.lalr.yaml", "MyApp");
        Assert.Empty(diags);
        Assert.Single(trees);
        var source = trees[0].ToString();

        // Header + namespace + class
        Assert.Contains("// <auto-generated/>", source);
        Assert.Contains("namespace MyApp;", source);
        Assert.Contains("public static partial class Arithmetic", source);

        // Symbols
        Assert.Contains("\"S'\"", source);
        Assert.Contains("\"E\"", source);
        Assert.Contains("\"+\"", source);

        // Derivation
        Assert.Contains("Derivation.None", source);
        Assert.Contains("Derivation.LeftMost", source);

        // Lexer
        Assert.Contains("[\"root\"]", source);
        Assert.Contains("Match = \"[0-9]+\"", source);
        Assert.Contains("Action = \"ignore\"", source);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase 3 / slice 1: typed AST record emission
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Same arithmetic grammar as <see cref="SampleArithmetic"/> but with action
    /// names attached. Expected AST: one record per distinct action — <c>MakeNum</c>
    /// (arity 1) and <c>MakeAdd</c> (arity 3). Productions without an action stay
    /// untyped (no record emitted), so the start symbol's pass-through rule and the
    /// whitespace lexer instruction don't pollute the AST file.
    /// </summary>
    private const string SampleArithmeticWithActions = """
        symbols:
          - "S'"
          - E
          - "+"
          - i
        productions:
          - derivation: none
            rules:
              - { lhs: "S'", rhs: [E] }
              - { lhs: E,  rhs: [i], action: makeNum }
          - derivation: leftmost
            rules:
              - { lhs: E, rhs: [E, "+", E], action: makeAdd }
        lexer:
          root:
            - { symbol: i, match: "[0-9]+" }
            - { symbol: "+", match: "\\+" }
            - { symbol: i, match: "[ \\t]+", action: ignore }
        """;

    [Fact]
    public void NoActions_EmitsOnlySchemaFileNoAstFile()
    {
        // The original SampleArithmetic has zero actions on productions, so the AST
        // emitter should produce nothing. Single tree = the schema file alone.
        var (trees, diags) = RunGenerator(SampleArithmetic, "arithmetic.lalr.yaml", "MyApp");
        Assert.Empty(diags);
        Assert.Single(trees);
    }

    [Fact]
    public void Actions_EmitOneRecordPerDistinctActionInAstFile()
    {
        var (trees, diags) = RunGenerator(SampleArithmeticWithActions, "arithmetic.lalr.yaml", "MyApp");
        Assert.Empty(diags);
        Assert.Equal(3, trees.Length); // schema + ast + visitor (slice 2)

        // Find the AST tree by content — the order in the array isn't guaranteed.
        // Distinguish from the visitor tree by looking for the record syntax (the
        // visitor file contains "interface IVisitor", not "public sealed record").
        var astSource = trees.Select(t => t.ToString())
            .Single(src => src.Contains("public sealed record", StringComparison.Ordinal));

        Assert.Contains("namespace MyApp;", astSource);
        Assert.Contains("public static partial class Arithmetic", astSource);
        Assert.Contains("public sealed record MakeNum(Item Arg0);", astSource);
        Assert.Contains("public sealed record MakeAdd(Item Arg0, Item Arg1, Item Arg2);", astSource);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LALR0003: structural-validation diagnostics
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnknownSymbolInProduction_ReportsLalr0003()
    {
        // Production references "Z", which isn't in symbols[]. Pre-fix this
        // surfaced only at runtime via SchemaCompilationException — now it's
        // a build-time diagnostic with the offending path locator.
        const string yaml = """
            symbols: ["S'", E, n]
            productions:
              - derivation: none
                rules:
                  - { lhs: "S'", rhs: [E] }
                  - { lhs: E,  rhs: [n] }
                  - { lhs: E,  rhs: [Z] }
            lexer:
              root:
                - { symbol: n, match: "[0-9]+" }
            """;

        var (_, diags) = RunGenerator(yaml);
        Assert.Contains(diags, d => d.Id == "LALR0003" && d.GetMessage().Contains("rhs[0]") && d.GetMessage().Contains("'Z'"));
    }

    [Fact]
    public void MissingRootLexerState_ReportsLalr0003()
    {
        const string yaml = """
            symbols: ["S'", E]
            productions:
              - derivation: none
                rules:
                  - { lhs: "S'", rhs: [E] }
                  - { lhs: E,    rhs: [] }
            lexer:
              other:
                - { symbol: E, match: "x" }
            """;

        var (_, diags) = RunGenerator(yaml);
        Assert.Contains(diags, d => d.Id == "LALR0003" && d.GetMessage().Contains("'root'"));
    }

    [Fact]
    public void DuplicateSymbol_ReportsLalr0003()
    {
        const string yaml = """
            symbols: ["S'", E, E, n]
            productions:
              - derivation: none
                rules:
                  - { lhs: "S'", rhs: [E] }
                  - { lhs: E,  rhs: [n] }
            lexer:
              root:
                - { symbol: n, match: "[0-9]+" }
            """;

        var (_, diags) = RunGenerator(yaml);
        Assert.Contains(diags, d => d.Id == "LALR0003" && d.GetMessage().Contains("duplicate"));
    }

    [Fact]
    public void LexerRuleWithMutuallyExclusiveInstructions_ReportsLalr0003()
    {
        // Both push and action set on the same lexer rule — only one allowed.
        const string yaml = """
            symbols: ["S'", E, n]
            productions:
              - derivation: none
                rules:
                  - { lhs: "S'", rhs: [E] }
                  - { lhs: E,  rhs: [n] }
            lexer:
              root:
                - { symbol: n, match: "[0-9]+", push: other, action: ignore }
              other:
                - { symbol: n, match: "y" }
            """;

        var (_, diags) = RunGenerator(yaml);
        Assert.Contains(diags, d => d.Id == "LALR0003" && d.GetMessage().Contains("at most one"));
    }

    [Fact]
    public void ValidGrammar_ProducesNoLalr0003()
    {
        // Sanity: the existing arithmetic sample should pass validation cleanly.
        var (_, diags) = RunGenerator(SampleArithmetic);
        Assert.DoesNotContain(diags, d => d.Id == "LALR0003");
    }

    [Fact]
    public void DuplicateActionWithDifferentArity_ReportsLalr0002()
    {
        // Same action name on two productions with different RHS lengths: 1 vs 3.
        // The slice-1 contract is one record per action name, so divergent arity is
        // a hard error surfaced as LALR0002.
        const string yaml = """
            symbols: ["S'", E, "+", i]
            productions:
              - derivation: none
                rules:
                  - { lhs: "S'", rhs: [E] }
                  - { lhs: E,  rhs: [i],          action: makeBoth }
                  - { lhs: E,  rhs: [E, "+", E],  action: makeBoth }
            lexer:
              root:
                - { symbol: i, match: "[0-9]+" }
                - { symbol: "+", match: "\\+" }
            """;

        var (_, diags) = RunGenerator(yaml);
        Assert.Contains(diags, d => d.Id == "LALR0002");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase 3 / slice 2: visitor interface + BuildActions wiring
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoActions_EmitsNoVisitorFile()
    {
        // Already covered by NoActions_EmitsOnlySchemaFileNoAstFile (single tree =
        // schema only) but assert here too to keep slice 2's contract local: with
        // zero actions, the visitor surface is zero-method noise; don't emit it.
        var (trees, diags) = RunGenerator(SampleArithmetic, "arithmetic.lalr.yaml", "MyApp");
        Assert.Empty(diags);
        Assert.DoesNotContain(trees, t => t.ToString().Contains("interface IVisitor", StringComparison.Ordinal));
    }

    [Fact]
    public void Actions_EmitVisitorInterfaceAndBuildActions()
    {
        var (trees, diags) = RunGenerator(SampleArithmeticWithActions, "arithmetic.lalr.yaml", "MyApp");
        Assert.Empty(diags);
        Assert.Equal(3, trees.Length); // schema + ast + visitor

        var visitorSource = trees.Select(t => t.ToString())
            .Single(src => src.Contains("interface IVisitor", StringComparison.Ordinal));

        Assert.Contains("namespace MyApp;", visitorSource);
        Assert.Contains("public static partial class Arithmetic", visitorSource);

        // Interface methods: one Visit overload per AST record, returning the
        // visitor's generic T so consumers can pick a typed return (e.g. int
        // for an evaluator) instead of always boxing through object.
        Assert.Contains("public interface IVisitor<out T>", visitorSource);
        Assert.Contains("T Visit(MakeNum node);", visitorSource);
        Assert.Contains("T Visit(MakeAdd node);", visitorSource);

        // BuildActions wiring: dictionary keys are the original camelCase action
        // names (what SchemaCompiler looks up); each entry constructs the record
        // from the parser's args and dispatches to the matching Visit overload.
        Assert.Contains("BuildActions<T>(IVisitor<T> visitor)", visitorSource);
        Assert.Contains("[\"makeNum\"] = (lhs, args) => visitor.Visit(new MakeNum(args[0])),", visitorSource);
        Assert.Contains("[\"makeAdd\"] = (lhs, args) => visitor.Visit(new MakeAdd(args[0], args[1], args[2])),", visitorSource);

        // Build<T>(IVisitor<T>) is the convenience wrapper around
        // SchemaCompiler.Compile(Schema, BuildActions(visitor)) — emitted in
        // the visitor file because it depends on the IVisitor type.
        Assert.Contains("Build<T>(IVisitor<T> visitor)", visitorSource);
        Assert.Contains("SchemaCompiler.Compile(Schema, BuildActions(visitor))", visitorSource);
    }

    [Fact]
    public void NoActions_SchemaFileEmitsNoArgBuild()
    {
        // Even with zero actions, the schema file emits a no-arg Build() so
        // simple grammars don't have to import SchemaCompiler at all.
        var (trees, diags) = RunGenerator(SampleArithmetic, "arithmetic.lalr.yaml", "MyApp");
        Assert.Empty(diags);
        Assert.Single(trees);
        var source = trees[0].ToString();
        Assert.Contains("public static (global::CodeProject.Syntax.LALR.Grammar Grammar, Dictionary<string, global::CodeProject.Syntax.LALR.LexicalGrammar.LexRule[]> Lexer) Build()", source);
        Assert.Contains("SchemaCompiler.Compile(Schema)", source);
    }

    [Fact]
    public void Actions_SchemaAndVisitorFilesBothEmitBuild()
    {
        // With actions, both files contribute a Build overload to the partial
        // class: a no-arg one in the schema file (will throw at runtime if
        // anything names an action — present as a compile-time hint that this
        // grammar has actions) and a typed-visitor one in the visitor file.
        var (trees, diags) = RunGenerator(SampleArithmeticWithActions, "arithmetic.lalr.yaml", "MyApp");
        Assert.Empty(diags);
        Assert.Equal(3, trees.Length);
        var schemaSource = trees.Select(t => t.ToString())
            .Single(s => s.Contains("public static GrammarSchema Schema", StringComparison.Ordinal));
        var visitorSource = trees.Select(t => t.ToString())
            .Single(s => s.Contains("interface IVisitor", StringComparison.Ordinal));

        Assert.Contains("Build()", schemaSource);
        Assert.Contains("Build<T>(IVisitor<T> visitor)", visitorSource);
    }

    [Fact]
    public async Task Actions_EndToEnd_StubVisitorParsesInput()
    {
        // The full slice-2 happy path: drive the generator on a grammar with
        // actions, compile the three generated files together with a stub IVisitor
        // implementation, load the assembly, call BuildActions(stub), feed the
        // dictionary into SchemaCompiler.Compile, parse "12 + 34", and assert
        // (a) the parse accepts and (b) the visitor methods got dispatched. This
        // proves the entire chain — emitted visitor → generated wiring →
        // SchemaCompiler → parser — works end to end.
        var (trees, diags) = RunGenerator(SampleArithmeticWithActions, "arithmetic.lalr.yaml", "GenVisit");
        Assert.Empty(diags);
        Assert.Equal(3, trees.Length);

        const string stubSource = """
            using System.Collections.Generic;
            using CodeProject.Syntax.LALR.LexicalGrammar;

            namespace GenVisit;

            public sealed class StubVisitor : Arithmetic.IVisitor<object>
            {
                public List<string> Calls { get; } = new();
                public object Visit(Arithmetic.MakeNum node) { Calls.Add("makeNum:" + node.Arg0.Content); return node.Arg0; }
                public object Visit(Arithmetic.MakeAdd node) { Calls.Add("makeAdd"); return node.Arg0; }
            }
            """;

        var allTrees = trees.Add(CSharpSyntaxTree.ParseText(stubSource,
            cancellationToken: TestContext.Current.CancellationToken));

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(GrammarSchema).Assembly.Location),
        };
        foreach (var name in new[] { "System.Runtime", "System.Collections", "netstandard" })
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(Assembly.Load(name).Location));
            }
            catch { /* best-effort */ }
        }

        var compilation = CSharpCompilation.Create(
            "GenVisitIntegration",
            syntaxTrees: allTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitResult.Success,
            "visitor end-to-end compilation failed:\n  "
            + string.Join("\n  ", emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        ms.Position = 0;
        var asm = Assembly.Load(ms.ToArray());

        var arithmeticType = asm.GetType("GenVisit.Arithmetic")!;
        var stubType = asm.GetType("GenVisit.StubVisitor")!;

        var stub = Activator.CreateInstance(stubType)!;
        var schema = (GrammarSchema)arithmeticType.GetProperty("Schema")!.GetValue(null)!;

        // BuildActions is now generic — IVisitor is `IVisitor<out T>` and the
        // method is `BuildActions<T>(IVisitor<T>)`. Find the open generic by
        // name (only one BuildActions, no ambiguity), close it with T=object
        // for this stub, then dispatch.
        var buildActionsOpen = arithmeticType.GetMethods()
            .Single(m => m.Name == "BuildActions" && m.IsGenericMethodDefinition);
        var buildActions = buildActionsOpen.MakeGenericMethod(typeof(object));
        var actions = (IReadOnlyDictionary<string, Func<int, Item[], object>>)buildActions.Invoke(null, new[] { stub })!;

        var (grammar, lexerTable) = SchemaCompiler.Compile(schema, actions);
        var parser = new Parser(grammar);
        using var lexer = PipeBytesLexer.FromString("12 + 34", lexerTable,
            cancellationToken: TestContext.Current.CancellationToken);
        using var la = new AsyncLATokenIterator(lexer);
        var debug = new Debug(parser, null, null);
        var result = await parser.ParseInputAsync(la, debug,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsError);

        // Visitor was actually dispatched: at least one MakeNum (per integer literal)
        // and one MakeAdd (the binary op).
        var calls = (List<string>)stubType.GetProperty("Calls")!.GetValue(stub)!;
        Assert.Contains(calls, c => c.StartsWith("makeNum:", StringComparison.Ordinal));
        Assert.Contains("makeAdd", calls);
    }

    [Fact]
    public void Actions_SchemaAndAstFilesCompileTogether()
    {
        // Slice 1's compile-check, kept around as a smoke test for the combined
        // emission. Slice 2 added the visitor file, so we now have three trees
        // (schema + ast + visitor); they must all compile together without naming
        // collisions or missing references.
        var (trees, diags) = RunGenerator(SampleArithmeticWithActions, "arithmetic.lalr.yaml", "GenAst");
        Assert.Empty(diags);
        Assert.Equal(3, trees.Length);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(GrammarSchema).Assembly.Location),
        };
        foreach (var name in new[] { "System.Runtime", "System.Collections", "netstandard" })
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(Assembly.Load(name).Location));
            }
            catch { /* best-effort */ }
        }

        var compilation = CSharpCompilation.Create(
            "GenAstIntegration",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compileDiags = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(compileDiags.Count == 0,
            "AST + schema files failed to compile together:\n  "
            + string.Join("\n  ", compileDiags));
    }

    [Fact]
    public async Task SampleArithmetic_GeneratedSourceCompilesAndParsesInput()
    {
        // Drive the generator, take the generated source, compile it into a real
        // assembly together with the runtime library, load the assembly, fish out
        // the Schema property, feed it through SchemaCompiler, then parse a sample
        // input. This is the end-to-end check that the emitted code is correct in
        // every dimension that matters.
        var (trees, diags) = RunGenerator(SampleArithmetic, "arithmetic.lalr.yaml", "GenTest");
        Assert.Empty(diags);
        Assert.Single(trees);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(GrammarSchema).Assembly.Location),
        };
        // Pull in the assemblies the generated code's `using`s reference; on .NET 10
        // many BCL types live in System.Runtime / System.Collections / etc.
        foreach (var name in new[] { "System.Runtime", "System.Collections", "netstandard" })
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(Assembly.Load(name).Location));
            }
            catch { /* best-effort; some names may not exist on this runtime */ }
        }

        var compilation = CSharpCompilation.Create(
            "GenIntegrationTest",
            syntaxTrees: [trees[0]],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitResult.Success,
            "generated code failed to compile:\n  " +
            string.Join("\n  ", emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        ms.Position = 0;
        var asm = Assembly.Load(ms.ToArray());
        var arithmeticType = asm.GetType("GenTest.Arithmetic")
            ?? throw new InvalidOperationException("emitted assembly has no GenTest.Arithmetic type");
        var schemaProp = arithmeticType.GetProperty("Schema")
            ?? throw new InvalidOperationException("Arithmetic has no Schema property");
        var schema = (GrammarSchema)schemaProp.GetValue(null)!;

        var (grammar, lexerTable) = SchemaCompiler.Compile(schema);
        var parser = new Parser(grammar);
        using var lexer = PipeBytesLexer.FromString("12 + 34", lexerTable,
            cancellationToken: TestContext.Current.CancellationToken);
        using var la = new AsyncLATokenIterator(lexer);
        var debug = new Debug(parser, null, null);
        var result = await parser.ParseInputAsync(la, debug,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
    }
}
