using System;
using CodeProject.Syntax.LALR;

namespace TestProject
{
    static class MainClass
    {
        public static void Main (string[] args)
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
            var grammar = new Grammar();
            grammar.Tokens = new[]{"S'", "e", "+", "-", "*", "/", "i", "(", ")"};

            grammar.PrecedenceGroups = new[]
            {
                new PrecedenceGroup
                {
                    Derivation = Derivation.None,
                    Productions = new[]
                    {
                        //S' -> e
                        new Production{
                            Left = 0,
                            Right = new[]{1}
                        },
                        //e -> i
                        new Production{
                            Left = 1,
                            Right = new []{6}
                        },
                        //e -> ( e )
                        new Production{
                            Left = 1,
                            Right = new []{7, 1, 8}
                        }
                    }
                },
                new PrecedenceGroup
                {
                    Derivation = Derivation.LeftMost,
                    Productions = new[]
                    {
                        //e -> e * e
                        new Production{
                            Left = 1,
                            Right = new []{1, 4, 1}
                        },
                        //e -> e / e
                        new Production{
                            Left = 1,
                            Right = new []{1, 5, 1}
                        }
                    }
                },
                new PrecedenceGroup
                {
                    //productions are left associative and bind less tightly than * or /
                    Derivation = Derivation.LeftMost,
                    Productions = new[]
                    {
                        //e -> e + e
                        new Production{
                            Left = 1,
                            Right = new []{1, 2, 1}
                        },
                        //e -> e - e
                        new Production{
                            Left = 1,
                            Right = new []{1, 3, 1}
                        }
                    }
                }
            };

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
                    new Token(8, ")"),
                };

            var result = parser.ParseInput(input, debugger);
            if (result.State < 0)
            {
                debugger.WriteErrorToken("Error while parsing: ", result);
            }
            Console.ReadKey();
        }
    }
}

