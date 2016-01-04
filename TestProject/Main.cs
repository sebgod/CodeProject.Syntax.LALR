using System;
using System.IO;
using CodeProject.Syntax.LALR;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
                new[] {"S'", "e", "+", "-", "*", "/", "i", "(", ")"},
                new PrecedenceGroup(Derivation.None,
                                    //S' -> e
                                    new Production(0, 1),
                                    //e -> i
                                    new Production(1, 6),
                                    //e -> ( e )
                                    new Production(1, 7, 1, 8)
                    ),
                new PrecedenceGroup(Derivation.LeftMost,
                                    //e -> e * e
                                    new Production(1, 1, 4, 1),
                                    //e -> e / e
                                    new Production(1, 1, 5, 1)
                    ),
                // productions are left associative and bind less tightly than * or /
                new PrecedenceGroup(Derivation.LeftMost,
                                    //e -> e + e
                                    new Production(1, 1, 2, 1),
                                    //e -> e - e
                                    new Production(1, 1, 3, 1)
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

            var inputTokens = new[]
                {
                    new Token(7, "("),
                    new Token(6, 1),
                    new Token(5, "/"),
                    new Token(6, 5),
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

            var result = await parser.ParseInputAsync(new AsyncLATokenIterator(new AsyncEnumerableWrapper(inputTokens)), debugger);
            if (result.State < 0)
            {
                debugger.WriteErrorToken("Error while parsing: ", result);
            }
            else
            {
                Console.WriteLine("Accept: {0}", debugger.TokenInfo(result));
            }
        }
    }
}

