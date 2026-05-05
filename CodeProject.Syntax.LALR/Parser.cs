using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR;

public class Parser
{
    private readonly HashSet<int>[] _firstSets;
    private readonly List<LR0Item> _lr0Items;
    private readonly List<LR1Item> _lr1Items;

    private readonly List<HashSet<int>> _lr0States;
    private readonly List<HashSet<int>> _lr0Kernels;
    private readonly List<HashSet<int>> _lalrStates;

    private readonly List<int[]> _lrGotos;
    private readonly List<int[]> _gotoPrecedence;

    private readonly Grammar _grammar;
    private readonly List<Production> _productions;
    private readonly HashSet<int> _terminals;
    private readonly HashSet<int> _nonterminals;

    private readonly List<IDictionary<int, IList<LALRPropogation>>> _lalrPropogations;

    private readonly ParseTable _parseTable;

    private readonly List<int> _productionPrecedence;
    private readonly List<Derivation> _productionDerivation;

    private readonly List<GrammarConflict> _conflicts;

    public HashSet<int>[] FirstSets => _firstSets;

    public IList<LR0Item> LR0Items => _lr0Items;

    public IList<LR1Item> LR1Items => _lr1Items;

    public ICollection<HashSet<int>> LR0States => _lr0States;

    public ICollection<HashSet<int>> LR0Kernels => _lr0Kernels;

    public ICollection<HashSet<int>> LALRStates => _lalrStates;

    public ICollection<int[]> LRGotos => _lrGotos;

    public ICollection<int[]> GotoPrecedence => _gotoPrecedence;

    public IList<Production> Productions => _productions;

    public ISet<int> Terminals => _terminals;

    public ISet<int> NonTerminals => _nonterminals;

    public IList<IDictionary<int, IList<LALRPropogation>>> LALRPropogations => _lalrPropogations;

    public ICollection<int> ProductionPrecedence => _productionPrecedence;

    public ICollection<Derivation> ProductionDerivation => _productionDerivation;

    public Grammar Grammar => _grammar;

    public ParseTable ParseTable => _parseTable;

    /// <summary>
    /// Adds a propogation to the propogation table
    /// </summary>
    private void AddPropogation(int nLR0SourceState, int nLR0SourceItem, int nLR0TargetState, int nLR0TargetItem)
    {
        while (_lalrPropogations.Count <= nLR0SourceState)
        {
            _lalrPropogations.Add(new Dictionary<int, IList<LALRPropogation>>());
        }

        var propogationsForState = _lalrPropogations[nLR0SourceState];
        if (!propogationsForState.TryGetValue(nLR0SourceItem, out var propogationList))
        {
            propogationList = new List<LALRPropogation>();
            propogationsForState[nLR0SourceItem] = propogationList;
        }

        propogationList.Add(new LALRPropogation(nLR0TargetItem, nLR0TargetState));
    }

    /// <summary>
    /// Gets the ID for a particular LR0 Item
    /// </summary>
    private int GetLR0ItemID(LR0Item item)
    {
        var nItemID = 0;
        foreach (var oItem in _lr0Items)
        {
            if (oItem.Equals(item))
            {
                return nItemID;
            }
            nItemID++;
        }
        _lr0Items.Add(item);
        return nItemID;
    }

    /// <summary>
    /// Gets the ID for a particular LR1 Item
    /// </summary>
    private int GetLR1ItemID(LR1Item item)
    {
        var nItemID = 0;
        foreach (var oItem in _lr1Items)
        {
            if (oItem.Equals(item))
            {
                return nItemID;
            }
            nItemID++;
        }
        _lr1Items.Add(item);
        return nItemID;
    }

    /// <summary>
    /// Gets the ID for a particular LR0 State
    /// </summary>
    private int GetLR0StateID(HashSet<int> state, ref bool bAdded)
    {
        var nStateID = 0;
        foreach (var oState in _lr0States)
        {
            if (oState.SetEquals(state))
            {
                return nStateID;
            }
            nStateID++;
        }
        _lr0States.Add(state);
        bAdded = true;
        return nStateID;
    }

    /// <summary>
    /// takes a set of LR0 Items and Produces all of the LR0 Items that are reachable by substitution
    /// </summary>
    private HashSet<int> LR0Closure(IEnumerable<int> items)
    {
        var closed = new HashSet<int>();
        var open = items.ToList();

        while (open.Count > 0)
        {
            var nItem = open[0];
            open.RemoveAt(0);
            var item = _lr0Items[nItem];
            closed.Add(nItem);

            var nProduction = 0;
            foreach (var production in _productions)
            {
                if ((item.Position < _productions[item.Production].Right.Length) && (production.Left == _productions[item.Production].Right[item.Position]))
                {
                    var newItem = new LR0Item(nProduction, 0);
                    var nNewItemID = GetLR0ItemID(newItem);
                    if (!open.Contains(nNewItemID) && !closed.Contains(nNewItemID))
                    {
                        open.Add(nNewItemID);
                    }
                }
                nProduction++;
            }
        }

        return closed;
    }

    /// <summary>
    /// takes a set of LR1 Items (LR0 items with lookaheads) and produces all of those LR1 items reachable by substitution
    /// </summary>
    private HashSet<int> LR1Closure(IEnumerable<int> items)
    {
        var closed = new HashSet<int>();
        var open = items.ToList();

        while (open.Count > 0)
        {
            var nLR1Item = open[0];
            open.RemoveAt(0);
            var lr1Item = _lr1Items[nLR1Item];
            var lr0Item = _lr0Items[lr1Item.LR0ItemID];
            closed.Add(nLR1Item);

            if (lr0Item.Position < _productions[lr0Item.Production].Right.Length)
            {
                var nToken = _productions[lr0Item.Production].Right[lr0Item.Position];
                if (_nonterminals.Contains(nToken))
                {
                    var argFirst = new List<int>();
                    for (var nIdx = lr0Item.Position + 1; nIdx < _productions[lr0Item.Production].Right.Length; nIdx++)
                    {
                        argFirst.Add(_productions[lr0Item.Production].Right[nIdx]);
                    }
                    var first = First(argFirst, lr1Item.LookAhead);
                    var nProduction = 0;
                    foreach (var production in _productions)
                    {
                        if (production.Left == nToken)
                        {
                            foreach (var nTokenFirst in first)
                            {
                                var newLR0Item = new LR0Item(nProduction, 0);
                                var nNewLR0ItemID = GetLR0ItemID(newLR0Item);
                                var newLR1Item = new LR1Item(nNewLR0ItemID, nTokenFirst);
                                var nNewLR1ItemID = GetLR1ItemID(newLR1Item);
                                if (!open.Contains(nNewLR1ItemID) && !closed.Contains(nNewLR1ItemID))
                                {
                                    open.Add(nNewLR1ItemID);
                                }
                            }
                        }
                        nProduction++;
                    }
                }
            }
        }

        return closed;
    }

    /// <summary>
    /// takes an LR0 state, and a tokenID, and produces the next state given the Item and productions of the grammar
    /// </summary>
    private int GotoLR0(int nState, int nTokenID, ref bool bAdded, ref int nPrecedence)
    {
        var gotoLR0 = new HashSet<int>();
        var state = _lr0States[nState];
        foreach (var nItem in state)
        {
            var item = _lr0Items[nItem];
            if (item.Position < _productions[item.Production].Right.Length && (_productions[item.Production].Right[item.Position] == nTokenID))
            {
                var newItem = new LR0Item(item.Production, item.Position + 1);
                var nNewItemID = GetLR0ItemID(newItem);
                gotoLR0.Add(nNewItemID);
                var nProductionPrecedence = _productionPrecedence[item.Production];
                if (nPrecedence < nProductionPrecedence)
                {
                    nPrecedence = nProductionPrecedence;
                }
            }
        }
        return gotoLR0.Count == 0 ? -1 : GetLR0StateID(LR0Closure(gotoLR0), ref bAdded);
    }

    /// <summary>
    /// Generates all of the LR 0 Items
    /// </summary>
    private void GenerateLR0Items()
    {
        var startState = new HashSet<int> { GetLR0ItemID(new LR0Item(0, 0)) };

        var bIgnore = false;
        var open = new List<int> { GetLR0StateID(LR0Closure(startState), ref bIgnore) };

        while (open.Count > 0)
        {
            var nState = open[0];
            open.RemoveAt(0);
            while (_lrGotos.Count <= nState)
            {
                _lrGotos.Add(new int[_grammar.SymbolNames.Length]);
                _gotoPrecedence.Add(new int[_grammar.SymbolNames.Length]);
            }

            for (var nToken = 0; nToken < _grammar.SymbolNames.Length; nToken++)
            {
                var bAdded = false;
                var nPrecedence = int.MinValue;
                var nGoto = GotoLR0(nState, nToken, ref bAdded, ref nPrecedence);

                _lrGotos[nState][nToken] = nGoto;
                _gotoPrecedence[nState][nToken] = nPrecedence;

                if (bAdded)
                {
                    open.Add(nGoto);
                }
            }
        }
    }

    /// <summary>
    /// Computes the set of first terminals for each Item in the grammar
    /// </summary>
    private void ComputeFirstSets()
    {
        var nCountTokens = _nonterminals.Count + _terminals.Count;

        for (var nIdx = 0; nIdx < nCountTokens; nIdx++)
        {
            _firstSets[nIdx] = [];
            if (_terminals.Contains(nIdx))
            {
                _firstSets[nIdx].Add(nIdx);
            }
        }

        foreach (var production in _productions)
        {
            if (production.Right.Length == 0)
            {
                _firstSets[production.Left].Add(-1);
            }
        }

        bool bDidSomething;
        do
        {
            bDidSomething = false;
            foreach (var production in _productions)
            {
                foreach (var nToken in production.Right)
                {
                    var bLookAhead = false;
                    foreach (var nTokenFirst in _firstSets[nToken])
                    {
                        if (nTokenFirst == -1)
                        {
                            bLookAhead = true;
                        }
                        else if (_firstSets[production.Left].Add(nTokenFirst))
                        {
                            bDidSomething = true;
                        }
                    }

                    if (!bLookAhead)
                    {
                        break;
                    }
                }
            }
        }
        while (bDidSomething);
    }

    /// <summary>
    /// returns the set of terminals that are possible to see next given an arbitrary list of tokens
    /// </summary>
    private HashSet<int> First(IEnumerable<int> tokens, int nTerminal)
    {
        var first = new HashSet<int>();
        foreach (var nToken in tokens)
        {
            var bLookAhead = false;
            foreach (var nTokenFirst in _firstSets[nToken])
            {
                if (nTokenFirst == -1)
                {
                    bLookAhead = true;
                }
                else
                {
                    first.Add(nTokenFirst);
                }
            }

            if (!bLookAhead)
            {
                return first;
            }
        }

        first.Add(nTerminal);
        return first;
    }

    /// <summary>
    /// Initializes the propogation table, and initial state of the LALR table
    /// </summary>
    private int InitLALRTables()
    {
        var nLR0State = 0;
        for (var i = 0; i < _lr0States.Count; i++)
        {
            _lalrStates.Add([]);
        }
        foreach (var lr0Kernel in _lr0Kernels)
        {
            var j = new HashSet<int>();
            foreach (var jLR0ItemID in lr0Kernel)
            {
                var lr1Item = new LR1Item(jLR0ItemID, -1);
                var nLR1ItemID = GetLR1ItemID(lr1Item);
                j.Add(nLR1ItemID);
            }
            var jPrime = LR1Closure(j);
            foreach (var jpLR1ItemID in jPrime)
            {
                var lr1Item = _lr1Items[jpLR1ItemID];
                var lr0Item = _lr0Items[lr1Item.LR0ItemID];

                if ((lr1Item.LookAhead != -1) || (nLR0State == 0))
                {
                    _lalrStates[nLR0State].Add(jpLR1ItemID);
                }

                if (lr0Item.Position < _productions[lr0Item.Production].Right.Length)
                {
                    var nToken = _productions[lr0Item.Production].Right[lr0Item.Position];
                    var lr0Successor = new LR0Item(lr0Item.Production, lr0Item.Position + 1);
                    var nLR0Successor = GetLR0ItemID(lr0Successor);
                    var nSuccessorState = _lrGotos[nLR0State][nToken];
                    if (lr1Item.LookAhead == -1)
                    {
                        AddPropogation(nLR0State, lr1Item.LR0ItemID, nSuccessorState, nLR0Successor);
                    }
                    else
                    {
                        var lalrItem = new LR1Item(nLR0Successor, lr1Item.LookAhead);
                        var nLALRItemID = GetLR1ItemID(lalrItem);
                        _lalrStates[nSuccessorState].Add(nLALRItemID);
                    }
                }
            }

            nLR0State++;
        }
        return _lalrStates.Count;
    }

    /// <summary>
    /// Calculates the states in the LALR table
    /// </summary>
    private void CalculateLookAheads()
    {
        bool bChanged;
        do
        {
            bChanged = false;
            var nState = 0;
            foreach (var statePropogations in _lalrPropogations)
            {
                var bStateChanged = false;
                foreach (var nLR1Item in _lalrStates[nState])
                {
                    var lr1Item = _lr1Items[nLR1Item];

                    if (statePropogations.TryGetValue(lr1Item.LR0ItemID, out var propogations))
                    {
                        foreach (var lalrPropogation in propogations)
                        {
                            var nGoto = lalrPropogation.LR0TargetState;
                            var item = new LR1Item(lalrPropogation.LR0TargetItem, lr1Item.LookAhead);
                            if (_lalrStates[nGoto].Add(GetLR1ItemID(item)))
                            {
                                bChanged = true;
                                bStateChanged = true;
                            }
                        }
                    }
                }

                if (bStateChanged)
                {
                    _lalrStates[nState] = LR1Closure(_lalrStates[nState]);
                }
                nState++;
            }
        }
        while (bChanged);
    }

    /// <summary>
    /// Initializes the tokens for the grammar
    /// </summary>
    private void InitSymbols()
    {
        for (var nSymbol = 0; nSymbol < _grammar.SymbolNames.Length; nSymbol++)
        {
            var symbol = nSymbol;
            var isTerminal = _productions.All(production => production.Left != symbol);

            if (isTerminal)
            {
                _terminals.Add(nSymbol);
            }
            else
            {
                _nonterminals.Add(nSymbol);
            }
        }
    }

    /// <summary>
    /// Converts an LR0 State to an LR0 Kernel consisting of only the 'initiating' LR0 Items in the state
    /// </summary>
    private void ConvertLR0ItemsToKernels()
    {
        foreach (var lr0State in _lr0States)
        {
            var lr0Kernel = new HashSet<int>();
            foreach (var nLR0Item in lr0State)
            {
                var item = _lr0Items[nLR0Item];
                if (item.Position != 0)
                {
                    lr0Kernel.Add(nLR0Item);
                }
                else if (_productions[item.Production].Left == 0)
                {
                    lr0Kernel.Add(nLR0Item);
                }
            }
            _lr0Kernels.Add(lr0Kernel);
        }
    }

    /// <summary>
    /// Generates the parse table given the lalr states, and grammar
    /// </summary>
    private void GenerateParseTable()
    {
        for (var nStateID = 0; nStateID < _lalrStates.Count; nStateID++)
        {
            var lalrState = _lalrStates[nStateID];

            for (var nToken = -1; nToken < _grammar.SymbolNames.Length; nToken++)
            {
                var actions = new List<Action>();
                if (nToken >= 0 && _lrGotos[nStateID][nToken] >= 0)
                {
                    actions.Add(new Action(ActionType.Shift, _lrGotos[nStateID][nToken]));
                }

                foreach (var nLR1ItemID in lalrState)
                {
                    var lr1Item = _lr1Items[nLR1ItemID];
                    var lr0Item = _lr0Items[lr1Item.LR0ItemID];

                    if ((lr0Item.Position == _productions[lr0Item.Production].Right.Length) && lr1Item.LookAhead == nToken)
                    {
                        var action = new Action(ActionType.Reduce, lr0Item.Production);
                        if (!actions.Contains(action))
                        {
                            actions.Add(action);
                        }
                    }
                }

                var nMaxPrecedence = int.MinValue;
                var importantActions = new List<Action>();
                foreach (var action in actions)
                {
                    var nActionPrecedence = int.MinValue;
                    if (action.ActionType == ActionType.Shift)
                    {
                        nActionPrecedence = _gotoPrecedence[nStateID][nToken]; //nToken will never be -1
                    }
                    else if (action.ActionType == ActionType.Reduce)
                    {
                        nActionPrecedence = _productionPrecedence[action.ActionParameter];
                    }

                    if (nActionPrecedence > nMaxPrecedence)
                    {
                        nMaxPrecedence = nActionPrecedence;
                        importantActions.Clear();
                        importantActions.Add(action);
                    }
                    else if (nActionPrecedence == nMaxPrecedence)
                    {
                        importantActions.Add(action);
                    }
                }

                if (importantActions.Count == 1)
                {
                    _parseTable.Actions[nStateID, nToken + 1] = importantActions[0];
                }
                else if (importantActions.Count > 1)
                {
                    Action? shiftAction = null;
                    var reduceActions = new List<Action>();
                    foreach (var action in importantActions)
                    {
                        if (action.ActionType == ActionType.Reduce)
                        {
                            reduceActions.Add(action);
                        }
                        else
                        {
                            shiftAction = action;
                        }
                    }

                    var derivation = _grammar.PrecedenceGroups[-nMaxPrecedence].Derivation;
                    if (derivation == Derivation.LeftMost && reduceActions.Count == 1)
                    {
                        _parseTable.Actions[nStateID, nToken + 1] = reduceActions[0];
                    }
                    else if (derivation == Derivation.RightMost && shiftAction.HasValue)
                    {
                        _parseTable.Actions[nStateID, nToken + 1] = shiftAction.Value;
                    }
                    else
                    {
                        var isShiftReduce = shiftAction.HasValue;
                        var errorType = isShiftReduce ? ActionType.ErrorSR : ActionType.ErrorRR;
                        _parseTable.Actions[nStateID, nToken + 1] = new Action(errorType, nToken);

                        var reduceIds = new int[reduceActions.Count];
                        for (var r = 0; r < reduceActions.Count; r++)
                        {
                            reduceIds[r] = reduceActions[r].ActionParameter;
                        }
                        _conflicts.Add(new GrammarConflict(
                            state: nStateID,
                            lookaheadSymbolId: nToken,
                            kind: isShiftReduce ? ConflictKind.ShiftReduce : ConflictKind.ReduceReduce,
                            shiftTargetState: shiftAction?.ActionParameter ?? -1,
                            reduceProductionIds: reduceIds));
                    }
                }
                else
                {
                    _parseTable.Actions[nStateID, nToken + 1] = new Action(ActionType.Error, nToken);
                }
            }
        }
    }

    /// <summary>
    /// helper function
    /// </summary>
    private void PopulateProductions()
    {
        var nPrecedence = 0;
        foreach (var oGroup in _grammar.PrecedenceGroups)
        {
            foreach (var oProduction in oGroup.Productions)
            {
                _productions.Add(oProduction);
                _productionPrecedence.Add(nPrecedence);
                _productionDerivation.Add(oGroup.Derivation);
            }
            nPrecedence--;
        }
    }

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

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = await tokenIterator.LookAheadAsync();
            var action = ParseTable.Actions[state, token.ID + 1];
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
                    var production = Productions[nProduction];
                    var nChildren = production.Right.Length;
                    Item reduction;
                    if (trimReductions && nChildren == 1 && _nonterminals.Contains(production.Right[0]))
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
                    state = ParseTable.Actions[lastState, production.Left + 1].ActionParameter;
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
        _lrGotos = [];
        _gotoPrecedence = [];
        _lr0Items = [];
        _lr1Items = [];
        _lr0States = [];
        _lr0Kernels = [];
        _lalrStates = [];
        _terminals = [];

        _nonterminals = [];
        _lalrPropogations = [];
        _grammar = grammar;
        _productions = [];
        _productionDerivation = [];
        _productionPrecedence = [];
        _conflicts = [];
        var nTokens = _grammar.SymbolNames.Length;
        _firstSets = new HashSet<int>[nTokens];

        PopulateProductions();
        InitSymbols();
        GenerateLR0Items();
        ComputeFirstSets();
        ConvertLR0ItemsToKernels();
        var nLalrStates = InitLALRTables();
        CalculateLookAheads();

        _parseTable = new ParseTable(nLalrStates, nTokens);
        GenerateParseTable();

        if (_conflicts.Count > 0)
        {
            // Surface unresolved S/R and R/R conflicts immediately rather than waiting for
            // an input to drive the parser through the offending parse-table cell.
            throw new GrammarConflictException(_conflicts,
                GrammarConflictException.FormatMessage(_conflicts, _grammar, _productions));
        }
    }

    /// <summary>
    /// Unresolved shift-reduce / reduce-reduce conflicts found while building the parse
    /// table. Always empty after a successful constructor call: the constructor throws
    /// <see cref="GrammarConflictException"/> if any conflicts remain. Useful for tools
    /// that catch the exception and want to introspect the offending cells.
    /// </summary>
    public IReadOnlyList<GrammarConflict> Conflicts => _conflicts;
}
