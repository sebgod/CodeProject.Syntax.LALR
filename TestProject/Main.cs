using System;
using System.IO;
using CodeProject.Syntax.LALR;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

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
            var left = children[0].Content as Token;
            var op = children[1].ID;
            var right = children[2].Content as Token;
            if (left != null && right != null && left.ID == 6 && right.ID == 6)
            {
                var result = null as object;
                var leftInt = (int)left.Content;
                var rightInt = (int)left.Content;
                try
                {
                    checked
                    {
                        switch (op)
                        {
                            case 2: result = leftInt + rightInt; break;
                            case 3: result = leftInt - rightInt; break;
                            case 4: result = leftInt * rightInt; break;
                            case 5: result = leftInt / rightInt; break;
                        }
                    }
                }
                catch (OverflowException overflow)
                {
                    return new Reduction(production, left, children[1], right, new Token(6, overflow.Message) { State = -1 });
                }
                if (result != null)
                {
                    return new Token(6, result);
                }
            }
            return null;
        }

        static async Task MainAsync(string[] args, CancellationToken token)
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
                new[] { "S'", "e", "+", "-", "*", "/", "i", "(", ")" },
                new PrecedenceGroup(Derivation.None,
                                    //S' -> e
                                    new Production(0, (_, x) => x[0], 1),
                                    //e -> i
                                    new Production(1, (_, x) => x[0], 6),
                                    //e -> ( e )
                                    new Production(1, (_, x) => x[1].Content as Token, 7, 1, 8)
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

            var inputString = "(1/5)+2*(3-4)";
            using (var charReader = new AsyncLACharIterator(new StringReader(inputString)))
            {
                while (charReader.MoveNextAsync().Result)
                {
                    var current = char.ConvertFromUtf32(await charReader.CurrentAsync());
                    var la = charReader.LookAheadAsync().Result;
                    Console.WriteLine("current={0} la={1}", current, la == AsyncLACharIterator.EOF ? "$" : char.ConvertFromUtf32(la));
                }
            }

            debugger.DumpParseTable();
            debugger.Flush();

            var parseTime = System.Diagnostics.Stopwatch.StartNew();
            IEnumerable<Token> inputSource;
#if DEBUG
            // (24 / 12) + 2 * (3-4)
            inputSource = new Token[]
                {
                    new Token(7, "("),
                    new Token(6, int.MaxValue),
                    new Token(4, "*"),
                    new Token(6, int.MaxValue),
                    new Token(8, ")"),
                    new Token(2, "+"),
                    new Token(6, 2),
                    new Token(4, "*"),
                    new Token(7, "("),
                    new Token(6, 3),
                    new Token(3, "-"),
                    new Token(6, 4),
                    new Token(8, ")")
                };
#else
            inputSource = TestLarge();
#endif
            var result = await parser.ParseInputAsync(new AsyncLATokenIterator(new AsyncEnumerableWrapper(inputSource)), debugger);
            parseTime.Stop();
            debugger.WriteFinalToken(string.Format("Accept ({0} ms): ", parseTime.Elapsed.TotalMilliseconds), "Error while parsing: ", result);
        }

        private static Func<Token[], object> NewMethod()
        {
            return x => x[1].ID == 6 ? x[1] : null;
        }

        private static IEnumerable<Token> TestLarge()
        {
            yield return new Token(6, 0);

            for (var i = 0; i < int.MaxValue >> 8; i++)
            {
                yield return new Token(4, "*");
                yield return new Token(6, i);
            }
        }
    }
}

