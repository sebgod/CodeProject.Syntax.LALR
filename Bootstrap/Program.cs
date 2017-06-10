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
        private const string BNF =
        @"<syntax>         ::= <rule> | <rule>, <syntax>.
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
            LeftAngle,
            RightAngle,
            RuleEnd,
            Comma,
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
                {S.LeftAngle, "<"},
                {S.RightAngle, ">"},
                {S.RuleEnd, ";"},
                {S.Comma, ","},
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

                   P(S.Syntax, S.Rule),
                   P(S.Syntax, S.Rule, S.Syntax),

                   P(S.Rule, S.RuleName, S.DefineOp, S.Clauses, S.RuleEnd)
                ),
                PG(Derivation.LeftMost,
                   P(S.Clauses, S.Terms),
                   P(S.Clauses, S.Terms, S.OrOp, S.Clauses),

                   P(S.Terms, S.Term),
                   P(S.Terms, S.Term, S.Comma, S.Terms)
                ),
                PG(Derivation.None,
                   P(S.Term, S.Literal),
                   P(S.Term, S.RuleName),
                   P(S.Term)
                ),
                PG(Derivation.LeftMost,
                   P(S.RuleName, S.LeftAngle, S.RuleCharacters, S.RightAngle),
                   P(S.RuleCharacters, S.RuleCharacter),
                   P(S.RuleCharacters, S.RuleCharacter, S.RuleCharacters)
                ),
                PG(Derivation.LeftMost,
                   P(S.Literal, S.DoubleQuote, S.TextInDoubleQuotes, S.DoubleQuote),
                   P(S.TextInDoubleQuotes, S.CharacterInDoubleQuotes),
                   P(S.TextInDoubleQuotes, S.CharacterInDoubleQuotes, S.TextInDoubleQuotes)
                ),
                PG(Derivation.LeftMost,
                   P(S.Literal, S.SingleQuote, S.TextInSingleQuotes, S.SingleQuote),
                   P(S.TextInSingleQuotes, S.CharacterInSingleQuotes),
                   P(S.TextInSingleQuotes, S.CharacterInSingleQuotes, S.TextInSingleQuotes)
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
                                    pair(S.LeftAngle, "[<]"),
                                    pair(S.RightAngle, "[>]"),
                                    pair(S.RuleEnd, "[.]"),
                                    pair(S.Comma, "[,]"),
                                    triple(S.EOL, @"[\r]?[\n]", AsyncRegexLexer.Ignore),
                                    triple(S.WS, @"[ \t]", AsyncRegexLexer.Ignore)
                                }
                        },
                        {
                            doubleQuoteState, new[]
                                {
                                    triple(S.DoubleQuote, doubleQuoteMarker, AsyncRegexLexer.PopState),
                                    pair(S.CharacterInDoubleQuotes, "[" + commonSet + symbolSet + "'" + "]"),
                                }
                        },
                        {
                            singleQuoteState, new[]
                                {
                                    triple(S.SingleQuote, singleQuoteMarker, AsyncRegexLexer.PopState),
                                    pair(S.CharacterInSingleQuotes, "[" + commonSet + symbolSet + "\"" + "]"),
                                }
                        }
                    };
            Item result;
            using (var charIterator = new AsyncLACharIterator(new StringReader(BNF)))
            using (var regexLexer = new AsyncRegexLexer(charIterator, lexerTable))
            using (var tokenIterator = new AsyncLATokenIterator(regexLexer))
            {
                debugger.DumpParseTable();
                result = await parser.ParseInputAsync(tokenIterator, debugger);
            }
            parseTime.Stop();
            var timeElapsed = string.Format("{0} ms", parseTime.Elapsed.TotalMilliseconds);

            debugger.WriteFinalToken(
                string.Format("Accept ({0}): ", timeElapsed),
                string.Format("Error while parsing ({0}): ", timeElapsed),
                result);
        }
    }
}