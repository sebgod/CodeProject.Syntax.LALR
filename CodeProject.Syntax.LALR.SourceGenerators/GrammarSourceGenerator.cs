using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using YamlDotNet.Core;

[assembly: InternalsVisibleTo("CodeProject.Syntax.LALR.Tests")]

namespace CodeProject.Syntax.LALR.SourceGenerators;

/// <summary>
/// Roslyn incremental source generator that turns <c>*.lalr.yaml</c> files (added
/// to the consumer's csproj as <c>&lt;AdditionalFiles&gt;</c>) into a static partial
/// class exposing a populated <c>GrammarSchema</c>. Consumers feed the schema to
/// <c>SchemaCompiler.Compile</c> at runtime.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GrammarSourceGenerator : IIncrementalGenerator
{
    public const string YamlFileSuffix = ".lalr.yaml";
    private const string DefaultNamespace = "Generated";

    private static readonly DiagnosticDescriptor InvalidYamlDescriptor = new(
        id: "LALR0001",
        title: "Invalid LALR grammar YAML",
        messageFormat: "{0}: {1}",
        category: "CodeProject.Syntax.LALR.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The consumer's RootNamespace (csproj <RootNamespace>) lands as a
        // build_property in AnalyzerConfigOptionsProvider.GlobalOptions.
        var rootNamespaceProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns) && !string.IsNullOrEmpty(ns)
                    ? ns
                    : DefaultNamespace);

        var grammarFiles = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(YamlFileSuffix, StringComparison.OrdinalIgnoreCase))
            .Combine(rootNamespaceProvider);

        context.RegisterSourceOutput(grammarFiles, Emit);
    }

    private static void Emit(SourceProductionContext context, (AdditionalText Text, string RootNamespace) input)
    {
        var (additionalText, rootNamespace) = input;
        var path = additionalText.Path;
        var content = additionalText.GetText(context.CancellationToken)?.ToString();

        YamlGrammarSchema? schema;
        try
        {
            schema = YamlGrammarLoader.Load(content);
        }
        catch (YamlException yamlEx)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidYamlDescriptor,
                LocationFor(additionalText, yamlEx),
                Path.GetFileName(path),
                yamlEx.Message));
            return;
        }
        catch (Exception ex)
        {
            // Defence in depth: any other deserialiser-level failure becomes a
            // diagnostic instead of crashing the build.
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidYamlDescriptor,
                Location.None,
                Path.GetFileName(path),
                ex.Message));
            return;
        }

        if (schema == null)
        {
            // Empty file — emit nothing rather than a class with an empty schema.
            return;
        }

        var className = DeriveClassName(path);
        var hintName = className + ".g.cs";
        var source = CodeEmitter.Emit(schema, rootNamespace, className);
        context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
    }

    /// <summary>
    /// <c>arithmetic.lalr.yaml</c> → <c>Arithmetic</c>. Strips the suffix and any
    /// directory components, then sanitises the remaining identifier to a valid C#
    /// PascalCase name (digits at the start get an underscore prefix, dashes/dots
    /// become underscores).
    /// </summary>
    internal static string DeriveClassName(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(YamlFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - YamlFileSuffix.Length);
        }
        var sb = new StringBuilder(name.Length);
        var capitaliseNext = true;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(capitaliseNext ? char.ToUpperInvariant(ch) : ch);
                capitaliseNext = false;
            }
            else
            {
                // dash, dot, underscore, anything else: split point but no character emitted
                capitaliseNext = true;
            }
        }
        if (sb.Length == 0)
        {
            return "Grammar";
        }
        if (char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }
        return sb.ToString();
    }

    private static Location LocationFor(AdditionalText text, YamlException yamlEx)
    {
        var mark = YamlGrammarLoader.TryGetMark(yamlEx);
        if (mark.Line == 0 && mark.Column == 0)
        {
            return Location.None;
        }
        // YamlException marks are 1-based; Roslyn LinePosition is 0-based.
        var line = mark.Line > 0 ? (int)mark.Line - 1 : 0;
        var column = mark.Column > 0 ? (int)mark.Column - 1 : 0;
        var pos = new LinePosition(line, column);
        var span = new LinePositionSpan(pos, pos);
        return Location.Create(text.Path, default, span);
    }
}
