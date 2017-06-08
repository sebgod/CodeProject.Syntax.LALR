using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using CodeProject.Syntax.LALR;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace TestProject
{
    internal static class MainClass
    {
        public static void Main(string[] args)
        {
            Console.InputEncoding = Encoding.Unicode;
            Console.OutputEncoding = Encoding.Unicode;
            Console.Out.Flush();

            var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

            MainAsync(args, cts.Token).GetAwaiter().GetResult();
            Console.ReadKey();
        }

        enum SymbolId
        {
            Result,
            Expr,
            Plus,
            Minus,
            Times,
            Divide,
            Integer,
            LeftParen,
            RightParen
        }

        private static readonly SymbolName[] Symbols = new SortedDictionary<SymbolId, string>
            {
                {SymbolId.Result, "S'"},
                {SymbolId.Expr, "e"},
                {SymbolId.Plus, "+"},
                {SymbolId.Minus, "-"},
                {SymbolId.Times, "*"},
                {SymbolId.Divide, "/"},
                {SymbolId.Integer, "i"},
                {SymbolId.LeftParen, "("},
                {SymbolId.RightParen, ")"},
            }.ToSymbolTable();

        private static SymbolName[] ToSymbolTable(this ICollection<KeyValuePair<SymbolId, string>> @this)
        {
            var symbols = new SymbolName[@this.Count];
            foreach (var kv in @this)
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
            var result = null as object;

            if (left.ID == (int)SymbolId.Integer && right.ID == (int)SymbolId.Integer)
            {
                var leftInt = (int) left.Content;
                var rightInt = (int) right.Content;
                try
                {
                    checked
                    {
                        switch ((SymbolId)op.ID)
                        {
                            case SymbolId.Plus:
                                result = leftInt + rightInt;
                                break;
                            case SymbolId.Minus:
                                result = leftInt - rightInt;
                                break;
                            case SymbolId.Times:
                                result = leftInt*rightInt;
                                break;
                            case SymbolId.Divide:
                                result = leftInt/rightInt;
                                break;
                        }
                    }
                }
                catch (OverflowException overflow)
                {
                    return new Reduction(production, left, op, right,
                                         new Item(6, overflow.Message) {State = -1});
                }
                return result != null ? new Item(6, result) : null;
            }
            return null;
        }

        private static async Task MainAsync(string[] args, CancellationToken cancellationToken)
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
                                    new Production((int)SymbolId.Result, (_, x) => x[0],(int)SymbolId.Expr),
                                    //e -> i
                                    new Production((int)SymbolId.Expr, (_, x) => x.Length == 1 ? x[0] : null,(int)SymbolId.Integer),
                                    //e -> ( e )
                                    new Production((int)SymbolId.Expr,
                                                   (_, x) => x[1].ContentType == ContentType.Nested ? x[1].Nested : null,
                                                   (int)SymbolId.LeftParen, (int)SymbolId.Expr, (int)SymbolId.RightParen)
                    ),
                new PrecedenceGroup(Derivation.LeftMost,
                                    //e -> e * e
                                    new Production((int)SymbolId.Expr, RewriteConstBinaryExpr,(int)SymbolId.Expr,(int)SymbolId.Times,(int)SymbolId.Expr),
                                    //e -> e / e
                                    new Production((int)SymbolId.Expr, RewriteConstBinaryExpr,(int)SymbolId.Expr,(int)SymbolId.Divide,(int)SymbolId.Expr)
                    ),
                // productions are left associative and bind less tightly than * or /
                new PrecedenceGroup(Derivation.LeftMost,
                                    //e -> e + e
                                    new Production((int)SymbolId.Expr, RewriteConstBinaryExpr,(int)SymbolId.Expr,(int)SymbolId.Plus,(int)SymbolId.Expr),
                                    //e -> e - e
                                    new Production((int)SymbolId.Expr, RewriteConstBinaryExpr,(int)SymbolId.Expr,(int)SymbolId.Minus,(int)SymbolId.Expr)
                    )
                );

            // generate the parse table
            var parser = new Parser(grammar);
            var debugger = new Debug(parser, Console.Write, Console.Error.Write);

            debugger.DumpParseTable();
            debugger.Flush();

            var parseTime = System.Diagnostics.Stopwatch.StartNew();

            Collection<PSParseError> syntaxErrors;
            var inputSource = PSParser.Tokenize("(24 / 12) + 2 * (3-4)", out syntaxErrors).Select(PSTokenToItem);
            Item result;
            using (var tokenIterator = new AsyncLATokenIterator(inputSource.AsAsync()))
            {
                result = await parser.ParseInputAsync(tokenIterator, debugger);
            }
            parseTime.Stop();
            var timeElapsed = string.Format("{0} ms", parseTime.Elapsed.TotalMilliseconds);
            debugger.WriteFinalToken(
                string.Format("Accept ({0}): ", timeElapsed),
                string.Format("Error while parsing ({0}): ", timeElapsed),
                result);
        }

        private static Item PSTokenToItem(PSToken psToken)
        {
            Func<SymbolId, Item> asItem = pID => new Item((int)pID, psToken.Content);

            switch (psToken.Type)
            {
                case PSTokenType.Number:
                    return new Item((int) SymbolId.Integer, int.Parse(psToken.Content));

                case PSTokenType.Operator:
                    switch (psToken.Content)
                    {
                        case "/":
                            return asItem(SymbolId.Divide);
                        case "*":
                            return asItem(SymbolId.Divide);
                        case "+":
                            return asItem(SymbolId.Plus);
                        case "-":
                            return asItem(SymbolId.Minus);
                    }
                    break;

                case PSTokenType.GroupStart:
                    if (psToken.Content == "(")
                    {
                        return asItem(SymbolId.LeftParen);
                    }
                    break;

                case PSTokenType.GroupEnd:
                    if (psToken.Content == ")")
                    {
                        return asItem(SymbolId.RightParen);
                    }
                    break;
            }

            throw new ArgumentException("Invalid PS token: " + psToken, "psToken");
        }
    }
}

