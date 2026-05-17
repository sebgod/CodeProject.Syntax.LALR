using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LALR.CC;
using LALR.CC.LexicalGrammar;

namespace Bootstrap;

internal static class Program
{
    private const string BNF = """

      <syntax>         ::= <rule> | <rule>, <syntax>.
      <rule>           ::= "<", <rule-name>, ">", "::=",  <expression>, <rule-end>.
      <expression>     ::= <list> | <list>,  "|",  <expression>.
      <list>           ::= <term> | <term>, <list> | .
      <term>           ::= <literal> | "<", <rule-name>, ">".
      <literal>        ::= '"', <text1>, '"' | "'", <text2>, "'".
      <text1>          ::= <character1>, <text1>.
      <text2>          ::= <character2>, <text2>.
      <character>      ::= <letter> | <digit> | <symbol>.
      <letter>         ::= "A" | "B" | "C" | "D" | "E" | "F" | "G" | "H" | "I" | "J" | "K" | "L" | "M" | "N" | "O" | "P" | "Q" | "R" | "S" | "T" | "U" | "V" | "W" | "X" | "Y" | "Z" | "a" | "b" | "c" | "d" | "e" | "f" | "g" | "h" | "i" | "j" | "k" | "l" | "m" | "n" | "o" | "p" | "q" | "r" | "s" | "t" | "u" | "v" | "w" | "x" | "y" | "z".
      <digit>          ::= "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9".
      <symbol>         ::=  "|" | " " | "-" | "!" | "#" | "$" | "%" | "&" | "(" | ")" | "*" | "+" | "," | "-" | "." | "/" | ":" | ";" | "<" | "=" | ">" | "?" | "@" | "[" | "\" | "]" | "^" | "_" | "`" | "{" | "|" | "}" | "~".
      <character1>     ::= <character> | "'".
      <character2>     ::= <character> | '"'.
      <rule-name>      ::= <letter> | <rule-name>, <rule-char>.
      <rule-char>      ::= <letter> | <digit> | "-".
    """;

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

        MainAsync(args, cts.Token).GetAwaiter().GetResult();

        if (System.Diagnostics.Debugger.IsAttached)
        {
            Console.ReadKey();
        }
    }

    private enum S
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
        EOL,
    }

    private static readonly SymbolName[] Symbols = ToSymbolTable(new Dictionary<S, string>
    {
        { S.Ast, "A" },
        { S.Syntax, "S" },
        { S.Rule, "R" },
        { S.Clauses, "Cs" },
        { S.RuleName, "N" },
        { S.DefineOp, "::=" },
        { S.Expr, "E" },
        { S.Terms, "Ts" },
        { S.Term, "T" },
        { S.Literal, "V" },
        { S.RuleCharacters, "RCs" },
        { S.TextInDoubleQuotes, "T\"" },
        { S.TextInSingleQuotes, "T'" },
        { S.CharacterInDoubleQuotes, "c\"" },
        { S.CharacterInSingleQuotes, "c'" },
        { S.RuleCharacter, "rc" },
        { S.OrOp, "|" },
        { S.DoubleQuote, "\"" },
        { S.SingleQuote, "'" },
        { S.RuleLiteralLeft, "<" },
        { S.RuleLiteralRight, ">" },
        { S.RuleEnd, "." },
        { S.TermSep, "," },
        { S.WS, @"\w" },
        { S.EOL, @"\n" },
    });

    private static SymbolName[] ToSymbolTable(ICollection<KeyValuePair<S, string>> source)
    {
        var symbols = new SymbolName[source.Count];
        foreach (var kv in source)
        {
            var idx = (int)kv.Key;
            symbols[idx] = new SymbolName(idx, kv.Value);
        }
        return symbols;
    }

    private static PrecedenceGroup PG(Derivation derivation, params Production[] productions)
        => new(derivation, productions);

    private static Production P(S lhs, params S[] rhs)
        => rhs is { Length: > 0 }
            ? new Production((int)lhs, rhs.Select(p => (int)p).ToArray())
            : new Production((int)lhs);

    private static Production PSL(S lhs, params S[] rhs) => PL(lhs, p => p, rhs);

    private static Production PL<T>(S lhs, Func<object, T> convertFunc, params S[] rhs)
    {
        Func<int, Item[], object> makeList = (pLHS, pRHS) => MakeList(pLHS, pRHS, convertFunc);
        return PR(lhs, makeList, rhs);
    }

    private static Production PR(S lhs, Func<int, Item[], object> rewriter, params S[] rhs)
        => rhs is { Length: > 0 }
            ? new Production((int)lhs, rewriter, rhs.Select(p => (int)p).ToArray())
            : new Production((int)lhs, rewriter);

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

        const string doubleQuoteState = "double";
        const string singleQuoteState = "single";

        // Common character classes shared between root and quoted-string states.
        // [-A-Za-z0-9_] — identifier-ish bytes used for rule names and BNF text.
        static CharClassRx CommonSet() => new(true, [
            (CharRx)'-',
            new CharRangeRx('A', 'Z'),
            new CharRangeRx('a', 'z'),
            new CharRangeRx('0', '9'),
            (CharRx)'_',
        ]);

        // ASCII punctuation set carried inside quoted literals — mirrors the original
        // regex `| !#$%&()*+,./:;\\<=>?@[\]^`{|}~` plus the leading space.
        static CharClassRx SymbolSet() => new(true, [
            (CharRx)'|', (CharRx)' ', (CharRx)'!', (CharRx)'#', (CharRx)'$', (CharRx)'%', (CharRx)'&',
            (CharRx)'(', (CharRx)')', (CharRx)'*', (CharRx)'+', (CharRx)',', (CharRx)'.', (CharRx)'/',
            (CharRx)':', (CharRx)';', (CharRx)'<', (CharRx)'=', (CharRx)'>', (CharRx)'?', (CharRx)'@',
            (CharRx)'[', (CharRx)'\\', (CharRx)']', (CharRx)'^', (CharRx)'`', (CharRx)'{', (CharRx)'}',
            (CharRx)'~',
        ]);

        static LexRule R(S sym, IRx pattern, string instr = null) => new((int)sym, pattern, instr);

        var lexerTable = new Dictionary<string, LexRule[]>
        {
            { PipeBytesLexer.RootState, [
                R(S.DoubleQuote, new CharRx('"'), doubleQuoteState),
                R(S.SingleQuote, new CharRx('\''), singleQuoteState),
                R(S.RuleCharacter, CommonSet()),
                R(S.DefineOp, new CharSequenceRx("::=")),
                R(S.OrOp, new CharRx('|')),
                R(S.RuleLiteralLeft, new CharRx('<')),
                R(S.RuleLiteralRight, new CharRx('>')),
                R(S.RuleEnd, new CharRx('.')),
                R(S.TermSep, new CharRx(',')),
                // PipeBytesLexer reads raw UTF-8 bytes so it sees CRLF directly — match the optional CR explicitly.
                R(S.EOL, new GroupRx(Multiplicity.Once,
                    new GroupRx(Multiplicity.ZeroOrOnce, new CharRx('\r')),
                    new CharRx('\n')), PipeBytesLexer.Ignore),
                R(S.WS, new CharClassRx(true, ' ', '\t'), PipeBytesLexer.Ignore),
            ] },
            { doubleQuoteState, [
                R(S.DoubleQuote, new CharRx('"'), PipeBytesLexer.PopState),
                R(S.CharacterInDoubleQuotes, new CharClassRx(true, [CommonSet(), SymbolSet(), (CharRx)'\''])),
            ] },
            { singleQuoteState, [
                R(S.SingleQuote, new CharRx('\''), PipeBytesLexer.PopState),
                R(S.CharacterInSingleQuotes, new CharClassRx(true, [CommonSet(), SymbolSet(), (CharRx)'"'])),
            ] },
        };

        using var lexer = PipeBytesLexer.FromString(BNF, lexerTable, cancellationToken: cancellationToken);
        using var tokenIterator = new AsyncLATokenIterator(lexer);

        var result = await parser.ParseInputAsync(tokenIterator, debugger, cancellationToken: cancellationToken);
        parseTime.Stop();
        var timeElapsed = $"{parseTime.Elapsed.TotalMilliseconds} ms";

        debugger.WriteFinalToken(
            $"Accept ({timeElapsed}): ",
            $"Error while parsing ({timeElapsed}): ",
            result);
    }

    private sealed class MetaTerm : Tuple<S?, string>
    {
        public static readonly MetaTerm Empty = new();

        private MetaTerm() : base(null, null)
        {
            // empty term
        }

        public MetaTerm(S name, object content) : base(name, content as string)
        {
            // calls base
        }

        public override string ToString() => ReferenceEquals(this, Empty) ? "()" : base.ToString();
    }

    private sealed class MetaItemList<T>(S @operator, LinkedList<T> items = null)
        : Tuple<S, LinkedList<T>>(@operator, items ?? new LinkedList<T>())
    {
        public override string ToString() => $"({Item1}, {string.Join(" ", Item2)})";
    }

    private static MetaTerm MakeMetaTerm(object arg)
    {
        if (ReferenceEquals(arg, null))
        {
            return MetaTerm.Empty;
        }
        if (arg is MetaTerm metaTerm)
        {
            return metaTerm;
        }
        if (arg is Item item && item.ContentType == ContentType.Scalar)
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
        return rhs.Length switch
        {
            1 => firstChar,
            2 => firstChar + rhs[1].Content,
            3 => new MetaTerm((S)lhs, rhs[1].Content),
            _ => null,
        };
    }
}
