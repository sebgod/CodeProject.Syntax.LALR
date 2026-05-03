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
using CodeProject.Syntax.LALR.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

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
