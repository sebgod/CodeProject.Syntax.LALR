using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace TestProject;

internal static class MainClass
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Out.Flush();

        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        MainCore(args, cts.Token);

        if (System.Diagnostics.Debugger.IsAttached)
        {
            Console.ReadKey();
        }
    }

    private enum SymbolId
    {
        Result,
        Expr,
        Plus,
        Minus,
        Times,
        Divide,
        Integer,
        LeftParen,
        RightParen,
    }

    private static readonly SymbolName[] Symbols = ToSymbolTable(new SortedDictionary<SymbolId, string>
    {
        { SymbolId.Result, "S'" },
        { SymbolId.Expr, "e" },
        { SymbolId.Plus, "+" },
        { SymbolId.Minus, "-" },
        { SymbolId.Times, "*" },
        { SymbolId.Divide, "/" },
        { SymbolId.Integer, "i" },
        { SymbolId.LeftParen, "(" },
        { SymbolId.RightParen, ")" },
    });

    private static SymbolName[] ToSymbolTable(ICollection<KeyValuePair<SymbolId, string>> source)
    {
        var symbols = new SymbolName[source.Count];
        foreach (var kv in source)
        {
            var idx = (int)kv.Key;
            symbols[idx] = new SymbolName(idx, kv.Value);
        }
        return symbols;
    }

    private static object RewriteConstBinaryExpr(int production, Item[] children)
    {
        if (children.Length != 3
            || children[0].ContentType != ContentType.Nested
            || children[2].ContentType != ContentType.Nested)
        {
            return null;
        }
        var left = children[0].Nested;
        var op = children[1];
        var right = children[2].Nested;
        object result = null;

        if (left.ID == (int)SymbolId.Integer && right.ID == (int)SymbolId.Integer)
        {
            var leftInt = (int)left.Content;
            var rightInt = (int)right.Content;
            try
            {
                checked
                {
                    result = (SymbolId)op.ID switch
                    {
                        SymbolId.Plus => leftInt + rightInt,
                        SymbolId.Minus => leftInt - rightInt,
                        SymbolId.Times => leftInt * rightInt,
                        SymbolId.Divide => leftInt / rightInt,
                        _ => null,
                    };
                }
            }
            catch (OverflowException overflow)
            {
                return new Reduction(production, left, op, right,
                    new Item(6, overflow.Message) { State = -1 });
            }
            return result != null ? new Item(6, result) : null;
        }
        return null;
    }

    private static void MainCore(string[] args, CancellationToken cancellationToken)
    {
        //
        // the following program produces a parse table for the following grammar
        // for infix expressions, and appropriately applies operator precedence of
        // the + - * / operators, otherwise evaluating the leftmost operations first
        //
        // S' -> e
        // e -> i
        // e -> ( e )
        // e -> e * e
        // e -> e / e
        // e -> e + e
        // e -> e - e
        //

        var grammar = new Grammar(
            Symbols,
            new PrecedenceGroup(Derivation.None,
                //S' -> e
                new Production((int)SymbolId.Result, (_, x) => x[0], (int)SymbolId.Expr),
                //e -> i
                new Production((int)SymbolId.Expr, (_, x) => x.Length == 1 ? x[0] : null, (int)SymbolId.Integer),
                //e -> ( e )
                new Production((int)SymbolId.Expr,
                    (_, x) => x[1].ContentType == ContentType.Nested ? x[1].Nested : null,
                    (int)SymbolId.LeftParen, (int)SymbolId.Expr, (int)SymbolId.RightParen)),
            new PrecedenceGroup(Derivation.LeftMost,
                //e -> e * e
                new Production((int)SymbolId.Expr, RewriteConstBinaryExpr, (int)SymbolId.Expr, (int)SymbolId.Times, (int)SymbolId.Expr),
                //e -> e / e
                new Production((int)SymbolId.Expr, RewriteConstBinaryExpr, (int)SymbolId.Expr, (int)SymbolId.Divide, (int)SymbolId.Expr)),
            // productions are left associative and bind less tightly than * or /
            new PrecedenceGroup(Derivation.LeftMost,
                //e -> e + e
                new Production((int)SymbolId.Expr, RewriteConstBinaryExpr, (int)SymbolId.Expr, (int)SymbolId.Plus, (int)SymbolId.Expr),
                //e -> e - e
                new Production((int)SymbolId.Expr, RewriteConstBinaryExpr, (int)SymbolId.Expr, (int)SymbolId.Minus, (int)SymbolId.Expr)));

        // generate the parse table
        var parser = new Parser(grammar);
        var debugger = new Debug(parser, Console.Write, Console.Error.Write);

        debugger.DumpParseTable();
        debugger.Flush();

        var parseTime = System.Diagnostics.Stopwatch.StartNew();

        var inputSource = TokenizeArithmetic("(24 / 12) + 2 * (3-4)");
        using var tokenIterator = new SyncLATokenIterator(inputSource.AsSync());
        var result = parser.ParseInput(tokenIterator, debugger, cancellationToken: cancellationToken);
        parseTime.Stop();
        var timeElapsed = $"{parseTime.Elapsed.TotalMilliseconds} ms";
        debugger.WriteFinalToken(
            $"Accept ({timeElapsed}): ",
            $"Error while parsing ({timeElapsed}): ",
            result);
    }

    /// <summary>
    /// Minimal arithmetic tokenizer. Replaces the previous PowerShell-based PSParser
    /// dependency to keep the project AOT-friendly and free of platform-specific deps.
    /// </summary>
    private static IEnumerable<Item> TokenizeArithmetic(string input)
    {
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (char.IsDigit(c))
            {
                var start = i;
                while (i < input.Length && char.IsDigit(input[i]))
                {
                    i++;
                }
                var number = int.Parse(input.AsSpan(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture);
                yield return new Item((int)SymbolId.Integer, number);
                continue;
            }
            switch (c)
            {
                case '+': yield return new Item((int)SymbolId.Plus, "+"); break;
                case '-': yield return new Item((int)SymbolId.Minus, "-"); break;
                case '*': yield return new Item((int)SymbolId.Times, "*"); break;
                case '/': yield return new Item((int)SymbolId.Divide, "/"); break;
                case '(': yield return new Item((int)SymbolId.LeftParen, "("); break;
                case ')': yield return new Item((int)SymbolId.RightParen, ")"); break;
                default:
                    throw new ArgumentException($"Unexpected character '{c}' at position {i}", nameof(input));
            }
            i++;
        }
    }
}
