using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Schema;

namespace Bootstrap.Stage1;

/// <summary>
/// Stage1 Bootstrap: same BNF input, same parse output, but the grammar
/// definition is loaded from <c>bnf.lalr.yaml</c> via the source generator
/// instead of being inlined as C#. Pair with the original <c>Bootstrap</c>
/// (stage0) project — running both should print identical Accept lines on the
/// same input.
/// </summary>
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

    /// <summary>
    /// Mirror of the YAML symbols list. <see cref="MakeList{T}"/> casts
    /// <c>(S)item.ID</c> to recover the operator symbol when building list nodes —
    /// that cast only works when this enum's underlying values match the IDs
    /// <c>SchemaCompiler</c> assigns from <c>bnf.lalr.yaml</c>'s symbols list. Keep
    /// the two in lockstep when editing the grammar.
    /// </summary>
    private enum S
    {
        Ast,                        // 0
        Syntax,                     // 1
        Rule,                       // 2
        Clauses,                    // 3
        RuleName,                   // 4
        Expr,                       // 5  (declared in symbols[] for stage0 parity; no production references it)
        Terms,                      // 6
        Term,                       // 7
        Literal,                    // 8
        TextInDoubleQuotes,         // 9
        TextInSingleQuotes,         // 10
        RuleCharacters,             // 11
        CharacterInDoubleQuotes,    // 12
        CharacterInSingleQuotes,    // 13
        RuleCharacter,              // 14
        OrOp,                       // 15
        DefineOp,                   // 16
        DoubleQuote,                // 17
        SingleQuote,                // 18
        RuleLiteralLeft,            // 19
        RuleLiteralRight,           // 20
        RuleEnd,                    // 21
        TermSep,                    // 22
        WS,                         // 23
        EOL,                        // 24
    }

    private static async Task MainAsync(string[] args, CancellationToken cancellationToken)
    {
        // Schema + lexer table come from the source-generated Bnf class (built from
        // bnf.lalr.yaml at compile time). The visitor turns each production action
        // into the same MakeList / MakeQuotedString / MakeMetaTerm rewriter logic
        // stage0 wires into Production constructors directly.
        var visitor = new BnfVisitor();
        var (grammar, lexerTable) = SchemaCompiler.Compile(Bnf.Schema, Bnf.BuildActions(visitor));

        var parser = new Parser(grammar);
        var debugger = new Debug(parser, Console.Write, Console.Error.Write);
        var parseTime = System.Diagnostics.Stopwatch.StartNew();

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

    /// <summary>
    /// Implements the typed visitor surface generated from <c>bnf.lalr.yaml</c>'s
    /// production actions. Each method delegates to the same helpers stage0 uses
    /// (<see cref="MakeList{T}"/> / <see cref="MakeQuotedString"/> /
    /// <see cref="MakeMetaTerm"/>) — the helpers themselves are unchanged, only
    /// the wiring from grammar shape to action invocation differs.
    /// </summary>
    private sealed class BnfVisitor : Bnf.IVisitor
    {
        // Group 0 (None): A -> S has no action.
        public object Visit(Bnf.SyntaxOne node) => MakeList((int)S.Syntax, [node.Arg0], item => item);
        public object Visit(Bnf.SyntaxCons node) => MakeList((int)S.Syntax, [node.Arg0, node.Arg1], item => item);
        public object Visit(Bnf.Rule node) => Tuple.Create(node.Arg0.Content, node.Arg2);

        // Group 1 (LeftMost): the operator-keyed lists.
        public object Visit(Bnf.ClausesOne node) => MakeList((int)S.Clauses, [node.Arg0], item => item);
        public object Visit(Bnf.ClausesCons node) => MakeList((int)S.Clauses, [node.Arg0, node.Arg1, node.Arg2], item => item);
        public object Visit(Bnf.TermsOne node) => MakeList<MetaTerm>((int)S.Terms, [node.Arg0], MakeMetaTerm);
        public object Visit(Bnf.TermsCons node) => MakeList<MetaTerm>((int)S.Terms, [node.Arg0, node.Arg1, node.Arg2], MakeMetaTerm);

        // Group 2 (None): T -> V and T -> N have no action; only the epsilon case rewrites.
        public object Visit(Bnf.TermEpsilon node) => MakeMetaTerm(null);

        // Group 3 (LeftMost): rule-name and its character list.
        public object Visit(Bnf.RuleName node) => MakeQuotedString((int)S.RuleName, [node.Arg0, node.Arg1, node.Arg2]);
        public object Visit(Bnf.RuleCharOne node) => MakeQuotedString((int)S.RuleCharacters, [node.Arg0]);
        public object Visit(Bnf.RuleCharCons node) => MakeQuotedString((int)S.RuleCharacters, [node.Arg0, node.Arg1]);

        // Group 4 (LeftMost): "double-quoted" literal and its inner text.
        public object Visit(Bnf.LiteralDouble node) => MakeQuotedString((int)S.Literal, [node.Arg0, node.Arg1, node.Arg2]);
        public object Visit(Bnf.TextDoubleOne node) => MakeQuotedString((int)S.TextInDoubleQuotes, [node.Arg0]);
        public object Visit(Bnf.TextDoubleCons node) => MakeQuotedString((int)S.TextInDoubleQuotes, [node.Arg0, node.Arg1]);

        // Group 5 (LeftMost): 'single-quoted' literal and its inner text.
        public object Visit(Bnf.LiteralSingle node) => MakeQuotedString((int)S.Literal, [node.Arg0, node.Arg1, node.Arg2]);
        public object Visit(Bnf.TextSingleOne node) => MakeQuotedString((int)S.TextInSingleQuotes, [node.Arg0]);
        public object Visit(Bnf.TextSingleCons node) => MakeQuotedString((int)S.TextInSingleQuotes, [node.Arg0, node.Arg1]);
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
