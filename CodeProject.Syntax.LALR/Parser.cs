using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR;

public class Parser
{
    private readonly ParserTableBuilder _builder;
    private readonly Grammar _grammar;
    private readonly ParseTable _parseTable;

    public IReadOnlyList<HashSet<int>> FirstSets => _builder.FirstSets;

    public IReadOnlyList<LR0Item> LR0Items => _builder.LR0Items;

    public IReadOnlyList<LR1Item> LR1Items => _builder.LR1Items;

    public IReadOnlyList<HashSet<int>> LR0States => _builder.LR0States;

    public IReadOnlyList<HashSet<int>> LR0Kernels => _builder.LR0Kernels;

    public IReadOnlyList<HashSet<int>> LALRStates => _builder.LALRStates;

    public IReadOnlyList<int[]> LRGotos => _builder.LRGotos;

    public IReadOnlyList<int[]> GotoPrecedence => _builder.GotoPrecedence;

    public IReadOnlyList<Production> Productions => _builder.Productions;

    public HashSet<int> Terminals => _builder.Terminals;

    public HashSet<int> NonTerminals => _builder.NonTerminals;

    public IReadOnlyList<IDictionary<int, IList<LALRPropogation>>> LALRPropogations => _builder.LALRPropogations;

    public IReadOnlyList<int> ProductionPrecedence => _builder.ProductionPrecedence;

    public IReadOnlyList<Derivation> ProductionDerivation => _builder.ProductionDerivation;

    public Grammar Grammar => _grammar;

    public ParseTable ParseTable => _parseTable;

    /// <summary>
    /// Unresolved shift-reduce / reduce-reduce conflicts found while building the parse
    /// table. Always empty after a successful constructor call: the constructor throws
    /// <see cref="GrammarConflictException"/> if any conflicts remain. Useful for tools
    /// that catch the exception and want to introspect the offending cells.
    /// </summary>
    public IReadOnlyList<GrammarConflict> Conflicts => _builder.Conflicts;

    /// <summary>
    /// Symbol ids that have a non-error action at <paramref name="state"/>. Column 0
    /// is end-of-input (id -1); column N is symbol id N-1. Used to build the
    /// "expected one of …" set for <see cref="ParseErrorException"/>.
    /// </summary>
    private List<int> ExpectedTerminalsAt(int state)
    {
        var expected = new List<int>();
        var nCols = _parseTable.Tokens;
        for (var col = 0; col < nCols; col++)
        {
            var act = _parseTable.Actions[state, col];
            if (act.ActionType == ActionType.Shift || act.ActionType == ActionType.Reduce)
            {
                expected.Add(col - 1);
            }
        }
        return expected;
    }

    /// <summary>
    /// Based on: http://www.goldparser.org/doc/engine-pseudo/parse-Item.htm
    /// </summary>
    /// <param name="tokenIterator">Item iterator which will be owned by the caller</param>
    /// <param name="debugger">Enables debugging support</param>
    /// <param name="trimReductions">If true (default), trim reductions of the form L -> R, where R is a non-terminal</param>
    /// <param name="allowRewriting">Apply rewriting functions</param>
    /// <param name="errorMode">How to surface parse errors: throw <see cref="ParseErrorException"/> (default) or return the offending Item.</param>
    /// <param name="cancellationToken">Cancels the parse loop between iterations.</param>
    /// <returns>The reduced program tree on acceptance, or — when <paramref name="errorMode"/> is <see cref="ParserErrorMode.Return"/> — the erroneous Item.</returns>
    public async Task<Item> ParseInputAsync(IAsyncLAIterator<Item> tokenIterator, Debug debugger = null,
        bool trimReductions = true,
        bool allowRewriting = true,
        ParserErrorMode errorMode = ParserErrorMode.Throw,
        CancellationToken cancellationToken = default)
    {
        const int initState = 0;
        var tokenStack = new Stack<Item>();
        var state = initState;
        var productions = Productions;
        var nonterminals = NonTerminals;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = await tokenIterator.LookAheadAsync();
            var action = _parseTable.Actions[state, token.ID + 1];
            // debugger may be null (caller wants no trace); the methods are
            // [Conditional("DEBUG")] so the call vanishes in Release regardless,
            // but in Debug builds we'd dereference null without this guard.
            debugger?.DumpParsingState(state, tokenStack, token, action);

            switch (action.ActionType)
            {
                case ActionType.Shift:
                    state = action.ActionParameter;
                    token.State = state;
                    tokenStack.Push(token);
                    await tokenIterator.MoveNextAsync();
                    break;

                case ActionType.Reduce:
                    var nProduction = action.ActionParameter;
                    var production = productions[nProduction];
                    var nChildren = production.Right.Length;
                    Item reduction;
                    if (trimReductions && nChildren == 1 && nonterminals.Contains(production.Right[0]))
                    {
                        var popped = tokenStack.Pop();
                        reduction = new Item(production.Left, popped.Content, popped.Position);
                    }
                    else
                    {
                        var children = new Item[nChildren];
                        for (var i = 0; i < nChildren; i++)
                        {
                            children[nChildren - i - 1] = tokenStack.Pop();
                        }
                        // Distinguish "rewriter present, returned null" (keep the null —
                        // it's the legitimate content the visitor produced) from "no
                        // rewriter at all" (fall back to the default Reduction). Driving
                        // this off Production.HasRewriter avoids the old `?? new Reduction`
                        // conflation that ate a JSON visitor's null literal returns.
                        object rewrite = allowRewriting && production.HasRewriter
                            ? production.Rewrite(children)
                            : new Reduction(nProduction, children);
                        // empty reductions (epsilon) take the lookahead position so the
                        // emitted item still has a meaningful place in the source.
                        var pos = nChildren > 0 ? children[0].Position : token.Position;
                        reduction = new Item(production.Left, rewrite, pos);
                    }
                    var lastState = tokenStack.Count > 0 ? tokenStack.Peek().State : initState;
                    state = _parseTable.Actions[lastState, production.Left + 1].ActionParameter;
                    // Stash the goto-target parser state on the item so the next
                    // reduction's `lastState = Peek().State` resolves to a real state.
                    // The constructor already set State to the symbol id (or -1 if any
                    // child is an error); the IsError property recomputes from
                    // children when State >= 0, so marking the parser state here keeps
                    // error propagation working while routing nested reductions correctly.
                    // The original code stored production.Left here, which only happened
                    // to work for grammars where a reduction is never the stack item
                    // immediately below the next reduction's children — Wikipedia LaTeX's
                    // `\frac{...}{...}` (two adjacent A non-terminals separated only by
                    // a brace pair, where the second {E}'s reduction peeks the first A)
                    // is the case that exposed it.
                    reduction.State = state;
                    tokenStack.Push(reduction);
                    if (tokenStack.Count == 1 && tokenStack.Peek().ID == 0)
                    {
                        return tokenStack.Pop();
                    }
                    break;

                case ActionType.Error:
                    // Item.IsError is driven by State < 0. Use -(state+1) so even state 0
                    // produces a negative marker (the original `-state` collapsed to 0 at
                    // the initial state, leaving IsError==false on errors at the very
                    // first token — a latent bug in the pre-2026 code).
                    token.State = -(state + 1);
                    if (errorMode == ParserErrorMode.Throw)
                    {
                        var expected = ExpectedTerminalsAt(state);
                        throw new ParseErrorException(token, state, expected,
                            ParseErrorException.FormatMessage(token, state, expected, _grammar));
                    }
                    return token;

                case ActionType.ErrorRR:
                    throw new InvalidOperationException("Reduce-Reduce conflict in grammar: " + token);

                case ActionType.ErrorSR:
                    throw new InvalidOperationException("Shift-Reduce conflict in grammar: " + token);
            }
            debugger?.Flush();
        }
    }

    /// <summary>
    /// constructor, construct parser table
    /// </summary>
    public Parser(Grammar grammar)
    {
        _grammar = grammar;
        _builder = new ParserTableBuilder(grammar);
        _parseTable = _builder.ParseTable;

        if (_builder.Conflicts.Count > 0)
        {
            // Surface unresolved S/R and R/R conflicts immediately rather than waiting for
            // an input to drive the parser through the offending parse-table cell.
            throw new GrammarConflictException(_builder.Conflicts,
                GrammarConflictException.FormatMessage(_builder.Conflicts, _grammar, _builder.Productions));
        }
    }
}
