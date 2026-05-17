using System;
using System.Globalization;
using LALR.CC;
using LALR.CC.LexicalGrammar;

namespace Examples.Calculator;

/// <summary>
/// The smallest end-to-end demo of the YAML grammar pipeline. The grammar
/// itself lives in <c>grammar.lalr.yaml</c>; the source generator turns it
/// into the partial class <see cref="Grammar"/> at build time, with a
/// populated <c>Schema</c> property, two AST records (<c>Number</c>,
/// <c>Add</c>), and an <c>IVisitor</c> interface with one <c>Visit</c>
/// overload per record.
/// </summary>
internal static class Program
{
    public static int Main()
    {
        // Phase 5 / slices 5 + 6 + sync surface: the generator pre-bakes both
        // parser halves at compile time, and BytesLexer + Parser.ParseInput run
        // the in-memory input through a fully sync pipeline (no Task allocations
        // / no async state-machine restores per token). SchemaCompiler,
        // ParserTableBuilder, IRxParser, and the async iterator path are all
        // unreachable in this consumer's AOT graph.
        var calc = new Calc();
        var parser = Grammar.BuildParser(calc);
        var lexerTable = Grammar.BuildLexer();
        const string Input = "1 + 2 + 3 + 4 - 5";
        using var lexer = BytesLexer.FromString(Input, lexerTable);
        using var tokens = new SyncLATokenIterator(lexer);

        var result = parser.ParseInput(tokens);
        if (result.IsError)
        {
            Console.Error.WriteLine($"parse failed: {result}");
            return 2;
        }

        var sum = (int)result.Content;
        Console.WriteLine($"{Input} = {sum}");
        return sum == 5 ? 0 : 1;
    }

    /// <summary>
    /// Visitor: one method per generated AST record. The runtime constructs
    /// each record from the parser's reduction frame and dispatches by C#
    /// overload resolution, so the visitor signature stays in lockstep with
    /// the YAML grammar — adding a production with an action automatically
    /// extends the surface.
    /// </summary>
    private sealed class Calc : Grammar.IVisitor<int>
    {
        // Generic IVisitor<T> means this evaluator can return int directly
        // instead of object — no boxing in the visitor signature, no cast at
        // the call site of any code that holds onto a typed reference.
        public int Visit(Grammar.Number node) =>
            int.Parse((string)node.Arg0.Content, CultureInfo.InvariantCulture);

        public int Visit(Grammar.Add node) =>
            (int)node.Arg0.Content + (int)node.Arg2.Content;

        public int Visit(Grammar.Substract node) =>
            (int)node.Arg0.Content - (int)node.Arg2.Content;
    }
}
