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
 <syntax>         ::= <rule> | <rule> <syntax>
 <rule>           ::= <opt-whitespace> ""<"" <rule-name> "">"" <opt-whitespace> ""::="" <opt-whitespace> <expression> <line-end>
 <opt-whitespace> ::= "" "" <opt-whitespace> | """"
 <expression>     ::= <list> | <list> <opt-whitespace> ""|"" <opt-whitespace> <expression>
 <line-end>       ::= <opt-whitespace> <EOL> | <line-end> <line-end>
 <list>           ::= <term> | <term> <opt-whitespace> <list>
 <term>           ::= <literal> | ""<"" <rule-name> "">""
 <literal>        ::= '""' <text1> '""' | ""'"" <text2> ""'""
 <text1>          ::= """" | <character1> <text1>
 <text2>          ::= """" | <character2> <text2>
 <character>      ::= <letter> | <digit> | <symbol>
 <letter>         ::= ""A"" | ""B"" | ""C"" | ""D"" | ""E"" | ""F"" | ""G"" | ""H"" | ""I"" | ""J"" | ""K"" | ""L"" | ""M"" | ""N"" | ""O"" | ""P"" | ""Q"" | ""R"" | ""S"" | ""T"" | ""U"" | ""V"" | ""W"" | ""X"" | ""Y"" | ""Z"" | ""a"" | ""b"" | ""c"" | ""d"" | ""e"" | ""f"" | ""g"" | ""h"" | ""i"" | ""j"" | ""k"" | ""l"" | ""m"" | ""n"" | ""o"" | ""p"" | ""q"" | ""r"" | ""s"" | ""t"" | ""u"" | ""v"" | ""w"" | ""x"" | ""y"" | ""z""
 <digit>          ::= ""0"" | ""1"" | ""2"" | ""3"" | ""4"" | ""5"" | ""6"" | ""7"" | ""8"" | ""9""
 <symbol>         ::=  ""|"" | "" "" | ""-"" | ""!"" | ""#"" | ""$"" | ""%"" | ""&"" | ""("" | "")"" | ""*"" | ""+"" | "","" | ""-"" | ""."" | ""/"" | "":"" | "";"" | ""<"" | ""="" | "">"" | ""?"" | ""@"" | ""["" | ""\"" | ""]"" | ""^"" | ""_"" | ""`"" | ""{"" | ""|"" | ""}"" | ""~""
 <character1>     ::= <character> | ""'""
 <character2>     ::= <character> | '""'
 <rule-name>      ::= <letter> | <rule-name> <rule-char>
 <rule-char>      ::= <letter> | <digit> | ""-""
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

        enum BNFSymbol
        {
            Syntax,
            Rule,
            RuleName,
            OrOp,
            DefineOp,
            DoubleQuote,
            SingleQuote,
            Letter,
            Symbol,
            Digit,
            HyphenMinus,
            LeftAngle,
            RightAngle,
            LeftParen,
            RightParen,
            Whitespace,
            EOL
        }

        private static readonly SymbolName[] Symbols = new Dictionary<BNFSymbol, string>
            {
                {BNFSymbol.Syntax, "S"},
                {BNFSymbol.Rule, "R"},
                {BNFSymbol.RuleName, "CL"},
                {BNFSymbol.DefineOp, "::="},
                {BNFSymbol.OrOp, "|"},
                {BNFSymbol.DoubleQuote, "\""},
                {BNFSymbol.SingleQuote, "\'"},
                {BNFSymbol.Letter, "A-Z"},
                {BNFSymbol.Symbol, "%!#"},
                {BNFSymbol.Digit, "0-9"},
                {BNFSymbol.HyphenMinus, "-"},
                {BNFSymbol.LeftAngle, "<"},
                {BNFSymbol.RightAngle, ">"},
                {BNFSymbol.LeftParen, "("},
                {BNFSymbol.RightParen, ")"},
                {BNFSymbol.Whitespace, " "},
                {BNFSymbol.EOL, @"\n"},
            }.ToSymbolTable();

        private static SymbolName[] ToSymbolTable(this ICollection<KeyValuePair<BNFSymbol, string>> @this)
        {
            var symbols = new SymbolName[@this.Count];
            foreach (var kv in @this)
            {
                var idx = (int) kv.Key;
                symbols[idx] = new SymbolName(idx, kv.Value);
            }
            return symbols;
        }

        private static async Task MainAsync(string[] args, CancellationToken cancellationToken)
        {
            var grammar = new Grammar(
                Symbols,
                new PrecedenceGroup(Derivation.None,
                                    new Production((int) BNFSymbol.Syntax, (int) BNFSymbol.Rule),
                                    new Production((int) BNFSymbol.Syntax, (int) BNFSymbol.Rule, (int) BNFSymbol.Syntax),
                                    new Production((int) BNFSymbol.Rule, (int) BNFSymbol.LeftAngle,
                                                   (int) BNFSymbol.RuleName, (int) BNFSymbol.RightAngle)
                    )
                );

            // generate the parse table
            var parser = new Parser(grammar);
            var debugger = new Debug(parser, Console.Write, Console.Error.Write);

            debugger.DumpParseTable();
            debugger.Flush();

            var parseTime = System.Diagnostics.Stopwatch.StartNew();

            Func<string, Regex> compileRegex =
                p => new Regex("^" + (p.StartsWith("[")
                                          ? p
                                          : (p.Substring(0, 2) + "(" + p.Substring(2) + "|$)")),
                               RegexOptions.CultureInvariant |
                               RegexOptions.ExplicitCapture);

            Func<BNFSymbol, string, string, Tuple<int, Regex, string>> triple =
                (pSymbol, pPattern, pState) => Tuple.Create((int) pSymbol, compileRegex(pPattern), pState);

            Func<BNFSymbol, string, Tuple<int, Regex, string>> pair =
                (pSymbol, pPattern) => triple(pSymbol, pPattern, null);

            const string doubleQuoteState = "double";
            const string singleQuoteState = "single";

            const string doubleQuoteMarker = @"[""]";
            const string singleQuoteMarker = "[']";

            var letterSet = pair(BNFSymbol.Letter, "[A-Za-z]");
            var digitSet = pair(BNFSymbol.Digit, "[0-9]");
            var symbolSet = pair(BNFSymbol.Symbol, @"[-| -!#$%&()*+,./:;\\<=>?@[\]^_`{|}~]");

            var lexerTable =
                new Dictionary<string, Tuple<int, Regex, string>[]>
                    {
                        {
                            AsyncRegexLexer.RootState, new[]
                                {
                                    triple(BNFSymbol.DoubleQuote, doubleQuoteMarker, doubleQuoteState),
                                    triple(BNFSymbol.SingleQuote, singleQuoteMarker, singleQuoteState),
                                    letterSet,
                                    digitSet,
                                    pair(BNFSymbol.DefineOp, "::="),
                                    pair(BNFSymbol.OrOp, "[|]"),
                                    pair(BNFSymbol.LeftAngle, "[<]"),
                                    pair(BNFSymbol.RightAngle, "[>]"),
                                    pair(BNFSymbol.HyphenMinus, "[-]"),
                                    pair(BNFSymbol.EOL, @"[\n]"),
                                    pair(BNFSymbol.Whitespace, @"[ \t]")
                                }
                        },
                        {
                            doubleQuoteState, new[]
                                {
                                    triple(BNFSymbol.DoubleQuote, doubleQuoteMarker, AsyncRegexLexer.PopState),
                                    pair(BNFSymbol.SingleQuote, singleQuoteMarker),
                                    letterSet,
                                    digitSet,
                                    symbolSet
                                }
                        },
                        {
                            singleQuoteState, new[]
                                {
                                    triple(BNFSymbol.SingleQuote, singleQuoteMarker, AsyncRegexLexer.PopState),
                                    pair(BNFSymbol.DoubleQuote, doubleQuoteMarker),
                                    letterSet,
                                    digitSet,
                                    symbolSet
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

            return;
            debugger.WriteFinalToken(
                string.Format("Accept ({0}): ", timeElapsed),
                string.Format("Error while parsing ({0}): ", timeElapsed),
                result);
        }
    }
}