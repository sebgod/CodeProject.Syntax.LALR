using System;
using CodeProject.Syntax.LALR;
using System.Text;

namespace TestProject
{
    internal static class MainClass
    {
        public static void Main(string[] args)
        {
            Console.InputEncoding = Encoding.Unicode;
            Console.OutputEncoding = Encoding.Unicode;
            Console.Out.Flush();

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

            debugger.DumpParseTable();
            debugger.Flush();

            var input = new[]
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

            var result =
                parser.ParseInputAsync(new AsyncLATokenIterator(new AsyncEnumerableWrapper(input)), debugger).Result;
            if (result.State < 0)
            {
                debugger.WriteErrorToken("Error while parsing: ", result);
            }
            else
            {
                Console.WriteLine("Accept: {0}", debugger.TokenInfo(result));
            }
            Console.ReadKey();
        }
    }
}

