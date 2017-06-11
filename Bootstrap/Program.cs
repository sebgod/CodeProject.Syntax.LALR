using System.Linq;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Bootstrap
{
    internal static class Program
    {
        private const string BNF = @"
          <syntax>         ::= <rule> | <rule>, <syntax>.
          <rule>           ::= ""<"", <rule-name>, "">"", ""::="",  <expression>, <rule-end>.
          <expression>     ::= <list> | <list>,  ""|"",  <expression>.
          <list>           ::= <term> | <term>, <list> | .
          <term>           ::= <literal> | ""<"", <rule-name>, "">"".
          <literal>        ::= '""', <text1>, '""' | ""'"", <text2>, ""'"".
          <text1>          ::= <character1>, <text1>.
          <text2>          ::= <character2>, <text2>.
          <character>      ::= <letter> | <digit> | <symbol>.
          <letter>         ::= ""A"" | ""B"" | ""C"" | ""D"" | ""E"" | ""F"" | ""G"" | ""H"" | ""I"" | ""J"" | ""K"" | ""L"" | ""M"" | ""N"" | ""O"" | ""P"" | ""Q"" | ""R"" | ""S"" | ""T"" | ""U"" | ""V"" | ""W"" | ""X"" | ""Y"" | ""Z"" | ""a"" | ""b"" | ""c"" | ""d"" | ""e"" | ""f"" | ""g"" | ""h"" | ""i"" | ""j"" | ""k"" | ""l"" | ""m"" | ""n"" | ""o"" | ""p"" | ""q"" | ""r"" | ""s"" | ""t"" | ""u"" | ""v"" | ""w"" | ""x"" | ""y"" | ""z"".
          <digit>          ::= ""0"" | ""1"" | ""2"" | ""3"" | ""4"" | ""5"" | ""6"" | ""7"" | ""8"" | ""9"".
          <symbol>         ::=  ""|"" | "" "" | ""-"" | ""!"" | ""#"" | ""$"" | ""%"" | ""&"" | ""("" | "")"" | ""*"" | ""+"" | "","" | ""-"" | ""."" | ""/"" | "":"" | "";"" | ""<"" | ""="" | "">"" | ""?"" | ""@"" | ""["" | ""\"" | ""]"" | ""^"" | ""_"" | ""`"" | ""{"" | ""|"" | ""}"" | ""~"".
          <character1>     ::= <character> | ""'"".
          <character2>     ::= <character> | '""'.
          <rule-name>      ::= <letter> | <rule-name>, <rule-char>.
          <rule-char>      ::= <letter> | <digit> | ""-"".
        ";

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

            if (System.Diagnostics.Debugger.IsAttached)
            {
                Console.ReadKey();
            }
        }

        enum S
        {
            Ast,
            Syntax,
            Rule,
            Clauses,
            RuleName,
            Expr,
            Terms,
            Term,
            Literal,
            TextInDoubleQuotes,
            TextInSingleQuotes,
            RuleCharacters,
            CharacterInDoubleQuotes,
            CharacterInSingleQuotes,
            RuleCharacter,
            OrOp,
            DefineOp,
            DoubleQuote,
            SingleQuote,
            RuleLiteralLeft,
            RuleLiteralRight,
            RuleEnd,
            TermSep,
            WS,
            EOL
        }

        private static readonly SymbolName[] Symbols = new Dictionary<S, string>
            {
                {S.Ast, "A"},
                {S.Syntax, "S"},
                {S.Rule, "R"},
                {S.Clauses, "Cs"},
                {S.RuleName, "N"},
                {S.DefineOp, "::="},
                {S.Expr, "E"},
                {S.Terms, "Ts"},
                {S.Term, "T"},
                {S.Literal, "V"},
                {S.RuleCharacters, "RCs"},
                {S.TextInDoubleQuotes, "T\""},
                {S.TextInSingleQuotes, "T'"},
                {S.CharacterInDoubleQuotes, "c\""},
                {S.CharacterInSingleQuotes, "c'"},
                {S.RuleCharacter, "rc"},
                {S.OrOp, "|"},
                {S.DoubleQuote, "\""},
                {S.SingleQuote, "'"},
                {S.RuleLiteralLeft, "<"},
                {S.RuleLiteralRight, ">"},
                {S.RuleEnd, "."},
                {S.TermSep, ","},
                {S.WS, @"\w"},
                {S.EOL, @"\n"}
            }.ToSymbolTable();

        private static SymbolName[] ToSymbolTable(this ICollection<KeyValuePair<S, string>> @this)
        {
            var symbols = new SymbolName[@this.Count];
            foreach (var kv in @this)
            {
                var idx = (int) kv.Key;
                symbols[idx] = new SymbolName(idx, kv.Value);
            }
            return symbols;
        }

        private static PrecedenceGroup PG(Derivation derivation, params Production[] productions)
        {
            return new PrecedenceGroup(derivation, productions);
        }

        private static Production P(S lhs, params S[] rhs)
        {
            return rhs != null && rhs.Length > 0
                       ? new Production((int) lhs, rhs.Select(p => (int) p).ToArray())
                       : new Production((int) lhs);
        }

        private static Production PSL(S lhs, params S[] rhs)
        {
            return PL(lhs, p => p, rhs);
        }

        private static Production PL<T>(S lhs, Func<object, T> convertFunc, params S[] rhs)
        {
            Func<int, Item[], object> makeList = (pLHS, pRHS) => MakeList(pLHS, pRHS, convertFunc);
            return PR(lhs, makeList, rhs);
        }

        private static Production PR(S lhs, Func<int, Item[], object> rewriter, params S[] rhs)
        {
            return rhs != null && rhs.Length > 0
                       ? new Production((int)lhs, rewriter, rhs.Select(p => (int)p).ToArray())
                       : new Production((int)lhs, rewriter);
        }

        private static async Task MainAsync(string[] args, CancellationToken cancellationToken)
        {
            var grammar = new Grammar(
                Symbols,
                PG(Derivation.None,
                   P(S.Ast, S.Syntax),

                   PSL(S.Syntax, S.Rule),
                   PSL(S.Syntax, S.Rule, S.Syntax),

                   PR(S.Rule, (pLHS, pRHS) => Tuple.Create(pRHS[0].Content, pRHS[2]), S.RuleName, S.DefineOp, S.Clauses, S.RuleEnd)
                ),
                PG(Derivation.LeftMost,
                   PSL(S.Clauses, S.Terms),
                   PSL(S.Clauses, S.Terms, S.OrOp, S.Clauses),

                   PL(S.Terms, MakeMetaTerm, S.Term),
                   PL(S.Terms, MakeMetaTerm, S.Term, S.TermSep, S.Terms)
                ),
                PG(Derivation.None,
                   P(S.Term, S.Literal),
                   P(S.Term, S.RuleName),
                   PR(S.Term, (pLHS, pRHS) => MakeMetaTerm(null))
                ),
                PG(Derivation.LeftMost,
                   PR(S.RuleName, MakeQuotedString, S.RuleLiteralLeft, S.RuleCharacters, S.RuleLiteralRight),
                   PR(S.RuleCharacters, MakeQuotedString, S.RuleCharacter),
                   PR(S.RuleCharacters, MakeQuotedString, S.RuleCharacter, S.RuleCharacters)
                ),
                PG(Derivation.LeftMost,
                   PR(S.Literal, MakeQuotedString, S.DoubleQuote, S.TextInDoubleQuotes, S.DoubleQuote),
                   PR(S.TextInDoubleQuotes, MakeQuotedString, S.CharacterInDoubleQuotes),
                   PR(S.TextInDoubleQuotes, MakeQuotedString, S.CharacterInDoubleQuotes, S.TextInDoubleQuotes)
                ),
                PG(Derivation.LeftMost,
                   PR(S.Literal, MakeQuotedString, S.SingleQuote, S.TextInSingleQuotes, S.SingleQuote),
                   PR(S.TextInSingleQuotes, MakeQuotedString, S.CharacterInSingleQuotes),
                   PR(S.TextInSingleQuotes, MakeQuotedString, S.CharacterInSingleQuotes, S.TextInSingleQuotes)
                )
            );

            // generate the parse table
            var parser = new Parser(grammar);
            var debugger = new Debug(parser, Console.Write, Console.Error.Write);
            var parseTime = System.Diagnostics.Stopwatch.StartNew();

            Func<string, Regex> compileRegex =
                p => new Regex("^" + (p.StartsWith("[") || p.Length <= 2
                                          ? p
                                          : (p.Substring(0, 2) + "(" + p.Substring(2) + "|$)")),
                               RegexOptions.CultureInvariant |
                               RegexOptions.ExplicitCapture);

            Func<S, string, string, Tuple<int, Regex, string>> triple =
                (pSymbol, pPattern, pState) => Tuple.Create((int) pSymbol, compileRegex(pPattern), pState);

            Func<S, string, Tuple<int, Regex, string>> pair =
                (pSymbol, pPattern) => triple(pSymbol, pPattern, null);

            const string doubleQuoteState = "double";
            const string singleQuoteState = "single";

            const string doubleQuoteMarker = @"[""]";
            const string singleQuoteMarker = "[']";
            const string commonSet = "-A-Za-z0-9_";
            const string symbolSet = @"| !#$%&()*+,./:;\\<=>?@[\]^`{|}~";

            var lexerTable =
                new Dictionary<string, Tuple<int, Regex, string>[]>
                    {
                        {
                            AsyncRegexLexer.RootState, new[]
                                {
                                    triple(S.DoubleQuote, doubleQuoteMarker, doubleQuoteState),
                                    triple(S.SingleQuote, singleQuoteMarker, singleQuoteState),
                                    pair(S.RuleCharacter, "[" + commonSet + "]"),
                                    pair(S.DefineOp, "::="),
                                    pair(S.OrOp, "[|]"),
                                    pair(S.RuleLiteralLeft, "[<]"),
                                    pair(S.RuleLiteralRight, "[>]"),
                                    pair(S.RuleEnd, "[.]"),
                                    pair(S.TermSep, "[,]"),
                                    triple(S.EOL, @"[\r]?[\n]", AsyncRegexLexer.Ignore),
                                    triple(S.WS, @"[ \t]", AsyncRegexLexer.Ignore)
                                }
                        },
                        {
                            doubleQuoteState, new[]
                                {
                                    triple(S.DoubleQuote, doubleQuoteMarker, AsyncRegexLexer.PopState),
                                    pair(S.CharacterInDoubleQuotes, "[" + commonSet + symbolSet + "'" + "]")
                                }
                        },
                        {
                            singleQuoteState, new[]
                                {
                                    triple(S.SingleQuote, singleQuoteMarker, AsyncRegexLexer.PopState),
                                    pair(S.CharacterInSingleQuotes, "[" + commonSet + symbolSet + "\"" + "]")
                                }
                        }
                    };
            Item result;
            using (var charIterator = new AsyncLACharIterator(new StringReader(BNF)))
            using (var regexLexer = new AsyncRegexLexer(charIterator, lexerTable))
            using (var tokenIterator = new AsyncLATokenIterator(regexLexer))
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

        class MetaTerm : Tuple<S?, string>
        {
            public static readonly MetaTerm Empty = new MetaTerm();

            private MetaTerm() : base(null, null)
            {
                // empty term
            }
            public MetaTerm(S name, object content) : base(name, content as string)
            {
                // calls base
            }

            public override string ToString()
            {
                return ReferenceEquals(this, Empty) ? "()" : base.ToString();
            }
        }
        class MetaItemList<T> : Tuple<S, LinkedList<T>>
        {
            public MetaItemList(S @operator, LinkedList<T> items = null)
                : base(@operator, items ?? new LinkedList<T>())
            {
            }

            public override string ToString()
            {
                return string.Format("({0}, {1})", Item1, string.Join(" ", Item2));
            }
        }

        private static MetaTerm MakeMetaTerm(object arg)
        {
            if (ReferenceEquals(arg, null))
            {
                return MetaTerm.Empty;
            }
            if (arg is MetaTerm)
            {
                return arg as MetaTerm;
            }
            var item = arg as Item;
            if (item != null && item.ContentType == ContentType.Scalar)
            {
                return MakeMetaTerm(item.Content);
            }
            return null;
        }

        private static object MakeList<T>(int lhs, Item[] rhs, Func<object, T> convertFunc)
        {
            switch (rhs.Length)
            {
                case 2:
                case 3:
                    var lastIndex = rhs.Length - 1;
                    var listTuple = rhs[lastIndex].Content as MetaItemList<T>;
                    var @operator = rhs.Length == 3 ? (S)rhs[1].ID : (S)lhs;
                    if (listTuple == null)
                    {
                        listTuple = new MetaItemList<T>(@operator);
                        listTuple.Item2.AddFirst(convertFunc(rhs[lastIndex]));
                    }
                    listTuple.Item2.AddFirst(convertFunc(rhs[0]));
                    return listTuple;

                default:
                    return null;
            }
        }

        private static object MakeQuotedString(int lhs, Item[] rhs)
        {
            var firstChar = rhs[0].Content as string;
            switch (rhs.Length)
            {
                case 1:
                    return firstChar;
                case 2:
                    return firstChar + rhs[1].Content;
                case 3:
                    return new MetaTerm((S)lhs, rhs[1].Content);
                default:
                    return null;
            }
        }
    }
}