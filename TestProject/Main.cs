using System;
using System.IO;
using CodeProject.Syntax.LALR;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Utilities;

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

        private static object RewriteConstBinaryExpr(int production, Token[] children)
        {
            if (children.Length != 3 || children[0].ContentType != ContentType.Nested || children[2].ContentType != ContentType.Nested)
            {
                return null;
            }
            var left = children[0].Nested;
            var op = children[1];
            var right = children[2].Nested;
            var result = null as object;

            if (left.ID == 6 && right.ID == 6)
            {
                var leftInt = (int) left.Content;
                var rightInt = (int) right.Content;
                try
                {
                    checked
                    {
                        switch (op.ID)
                        {
                            case 2:
                                result = leftInt + rightInt;
                                break;
                            case 3:
                                result = leftInt - rightInt;
                                break;
                            case 4:
                                result = leftInt*rightInt;
                                break;
                            case 5:
                                result = leftInt/rightInt;
                                break;
                        }
                    }
                }
                catch (OverflowException overflow)
                {
                    return new Reduction(production, left, op, right,
                                         new Token(6, overflow.Message) {State = -1});
                }
                return result != null ? new Token(6, result) : null;
            }
            return null;
        }

        private static async Task MainAsync(string[] args, CancellationToken token)
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
                new[] {"S'", "e", "+", "-", "*", "/", "i", "(", ")"},
                new PrecedenceGroup(Derivation.None,
                                    //S' -> e
                                    new Production(0, (_, x) => x[0], 1),
                                    //e -> i
                                    new Production(1, (_, x) => x.Length == 1 ? x[0] : null, 6),
                                    //e -> ( e )
                                    new Production(1,
                                                   (_, x) => x[1].ContentType == ContentType.Nested ? x[1].Nested : null,
                                                   7, 1, 8)
                    ),
                new PrecedenceGroup(Derivation.LeftMost,
                                    //e -> e * e
                                    new Production(1, RewriteConstBinaryExpr, 1, 4, 1),
                                    //e -> e / e
                                    new Production(1, RewriteConstBinaryExpr, 1, 5, 1)
                    ),
                // productions are left associative and bind less tightly than * or /
                new PrecedenceGroup(Derivation.LeftMost,
                                    //e -> e + e
                                    new Production(1, RewriteConstBinaryExpr, 1, 2, 1),
                                    //e -> e - e
                                    new Production(1, RewriteConstBinaryExpr, 1, 3, 1)
                    )
                );

            // generate the parse table
            var parser = new Parser(grammar);
            var debugger = new Debug(parser, Console.Write, Console.Error.Write);

            debugger.DumpParseTable();
            debugger.Flush();

            var parseTime = System.Diagnostics.Stopwatch.StartNew();
#if DEBUG
            // (24 / 12) + 2 * (3-4)
            var inputSource = new[]
                {
                    new Token(6, 2),
                    new Token(2, "+"),
                    new Token(7, "("),
                    new Token(6, 0),
                    new Token(4, "*"),
                    new Token(6, int.MaxValue),
                    new Token(8, ")")
                };
#else
            var inputSource = TestLarge();
#endif
            Token result;
            using (var tokenIterator = new AsyncLATokenIterator(new AsyncEnumerableWrapper(inputSource)))
            {
                result = await parser.ParseInputAsync(tokenIterator, debugger, allowRewriting: true);
            }
            parseTime.Stop();
            var timeElapsed = string.Format("{0} ms", parseTime.Elapsed.TotalMilliseconds);
            debugger.WriteFinalToken(
                string.Format("Accept ({0}): ", timeElapsed),
                string.Format("Error while parsing ({0}): ", timeElapsed),
                              result);
        }

        private static IEnumerable<Token> TestLarge()
        {
            for (var i = 1; i < 50000; i++)
            {
                yield return new Token(6, 1);
                yield return new Token(4, "*");
            }
            yield return new Token(6, 1);
        }
    }
}

