using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeProject.Syntax.LALR
{
    public class Parser
    {
        readonly HashSet<int>[] _firstSets;
        readonly List<LR0Item> _lr0Items;
        readonly List<LR1Item> _lr1Items;

        readonly List<HashSet<int>> _lr0States;
        readonly List<HashSet<int>> _lr0Kernels;
        readonly List<HashSet<int>> _lalrStates;

        readonly List<int[]> _lrGotos;
        readonly List<int[]> _gotoPrecedence;

        readonly Grammar _grammar;
        readonly List<Production> _productions;
        readonly HashSet<int> _terminals;
        readonly HashSet<int> _nonterminals;

        readonly List<IDictionary<int, IList<LALRPropogation>>> _lalrPropogations;

        readonly ParseTable _parseTable;

// ReSharper disable ReturnTypeCanBeEnumerable.Global
        readonly List<int> _productionPrecedence;
        readonly List<Derivation> _productionDerivation;

        public HashSet<int>[] FirstSets { get { return _firstSets; } }

        public IList<LR0Item> LR0Items { get { return _lr0Items; } }

        public IList<LR1Item> LR1Items { get { return _lr1Items; } }

        public ICollection<HashSet<int>> LR0States { get { return _lr0States; } }

        public ICollection<HashSet<int>> LR0Kernels { get { return _lr0Kernels; } }

        public ICollection<HashSet<int>> LALRStates { get { return _lalrStates; } }

        public ICollection<int[]> LRGotos { get { return _lrGotos; } }

        public ICollection<int[]> GotoPrecedence { get { return _gotoPrecedence; } }

        public IList<Production> Productions { get { return _productions; } }

        public ISet<int> Terminals { get { return _terminals; } }

        public ISet<int> NonTerminals { get { return _nonterminals; } }

        public IList<IDictionary<int, IList<LALRPropogation>>> LALRPropogations { get { return _lalrPropogations; } }

        public ICollection<int> ProductionPrecedence { get { return _productionPrecedence; } }

        public ICollection<Derivation> ProductionDerivation { get { return _productionDerivation; } }

        public Grammar Grammar { get { return _grammar; } }

        public ParseTable ParseTable { get { return _parseTable; } }
// ReSharper restore ReturnTypeCanBeEnumerable.Global

        /// <summary>
        /// Adds a propogation to the propogation table
        /// </summary>
        void AddPropogation(int nLR0SourceState, int nLR0SourceItem, int nLR0TargetState, int nLR0TargetItem)
        {
            while (_lalrPropogations.Count <= nLR0SourceState)
            {
                _lalrPropogations.Add(new Dictionary<int, IList<LALRPropogation>>());
            }

            var propogationsForState = _lalrPropogations[nLR0SourceState];
            IList<LALRPropogation> propogationList;
            if (!propogationsForState.TryGetValue(nLR0SourceItem, out propogationList))
            {
                propogationList = new List<LALRPropogation>();
                propogationsForState[nLR0SourceItem] = propogationList;
            }

            propogationList.Add(new LALRPropogation(nLR0TargetItem, nLR0TargetState));
        }

        /// <summary>
        /// Gets the ID for a particular LR0 Item
        /// </summary>
        int GetLR0ItemID(LR0Item item)
        {
            int nItemID = 0;
            foreach (LR0Item oItem in _lr0Items)
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
        int GetLR1ItemID(LR1Item item)
        {
            int nItemID = 0;
            foreach (LR1Item oItem in _lr1Items)
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
        int GetLR0StateID(HashSet<int> state, ref bool bAdded)
        {
            int nStateID = 0;
            foreach (HashSet<int> oState in _lr0States)
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
        HashSet<int> LR0Closure(IEnumerable<int> items)
        {
            var closed = new HashSet<int>();
            var open = items.ToList();

            while (open.Count > 0)
            {
                int nItem = open[0];
                open.RemoveAt(0);
                LR0Item item = _lr0Items[nItem];
                closed.Add(nItem);

                int nProduction = 0;
                foreach (var production in _productions)
                {
                    if ((item.Position < _productions[item.Production].Right.Length) && (production.Left == _productions[item.Production].Right[item.Position]))
                    {
                        var newItem = new LR0Item(nProduction, 0);
                        int nNewItemID = GetLR0ItemID(newItem);
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
        HashSet<int> LR1Closure(IEnumerable<int> items)
        {
            var closed = new HashSet<int>();
            var open = items.ToList();

            while (open.Count > 0)
            {
                int nLR1Item = open[0];
                open.RemoveAt(0);
                LR1Item lr1Item = _lr1Items[nLR1Item];
                LR0Item lr0Item = _lr0Items[lr1Item.LR0ItemID];
                closed.Add(nLR1Item);

                if (lr0Item.Position < _productions[lr0Item.Production].Right.Length)
                {
                    int nToken = _productions[lr0Item.Production].Right[lr0Item.Position];
                    if (_nonterminals.Contains(nToken))
                    {
                        var argFirst = new List<int>();
                        for (int nIdx = lr0Item.Position + 1; nIdx < _productions[lr0Item.Production].Right.Length; nIdx++)
                        {
                            argFirst.Add(_productions[lr0Item.Production].Right[nIdx]);
                        }
                        var first = First(argFirst, lr1Item.LookAhead);
                        int nProduction = 0;
                        foreach (var production in _productions)
                        {
                            if (production.Left == nToken)
                            {
                                foreach (int nTokenFirst in first)
                                {
                                    var newLR0Item = new LR0Item(nProduction, 0);
                                    int nNewLR0ItemID = GetLR0ItemID(newLR0Item);
                                    var newLR1Item = new LR1Item(nNewLR0ItemID, nTokenFirst);
                                    int nNewLR1ItemID = GetLR1ItemID(newLR1Item);
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
        /// takes an LR0 state, and a tokenID, and produces the next state given the token and productions of the grammar
        /// </summary>
        int GotoLR0(int nState, int nTokenID, ref bool bAdded, ref int nPrecedence)
        {
            var gotoLR0 = new HashSet<int>();
            var state = _lr0States[nState];
            foreach (int nItem in state)
            {
                LR0Item item = _lr0Items[nItem];
                if (item.Position < _productions[item.Production].Right.Length && (_productions[item.Production].Right[item.Position] == nTokenID))
                {
                    var newItem = new LR0Item(item.Production, item.Position + 1);
                    int nNewItemID = GetLR0ItemID(newItem);
                    gotoLR0.Add(nNewItemID);
                    int nProductionPrecedence = _productionPrecedence[item.Production];
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
        void GenerateLR0Items()
        {
            var startState = new HashSet<int> {GetLR0ItemID(new LR0Item(0, 0))};

            var bIgnore = false;
            var open = new List<int> {GetLR0StateID(LR0Closure(startState), ref bIgnore)};

            while (open.Count > 0)
            {
                int nState = open[0];
                open.RemoveAt(0);
                while (_lrGotos.Count <= nState)
                {
                    _lrGotos.Add(new int[_grammar.Tokens.Length]);
                    _gotoPrecedence.Add(new int[_grammar.Tokens.Length]);
                }

                for (int nToken = 0; nToken < _grammar.Tokens.Length; nToken++)
                {
                    bool bAdded = false;
                    int nPrecedence = Int32.MinValue;
                    int nGoto = GotoLR0(nState, nToken, ref bAdded, ref nPrecedence);

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
        /// Computes the set of first terminals for each token in the grammar
        /// </summary>
        void ComputeFirstSets()
        {
            int nCountTokens = _nonterminals.Count + _terminals.Count;

            for (int nIdx = 0; nIdx < nCountTokens; nIdx++)
            {
                _firstSets[nIdx] = new HashSet<int>();
                if (_terminals.Contains(nIdx))
                {
                    _firstSets[nIdx].Add(nIdx);
                }
            }

            foreach (Production production in _productions)
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
                    foreach (int nToken in production.Right)
                    {
                        bool bLookAhead = false;
                        foreach (int nTokenFirst in _firstSets[nToken])
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
        HashSet<int> First(IEnumerable<int> tokens, int nTerminal)
        {
            var first = new HashSet<int>();
            foreach (int nToken in tokens)
            {
                bool bLookAhead = false;
                foreach (int nTokenFirst in _firstSets[nToken])
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
        void InitLALRTables()
        {
            int nLR0State = 0;
            for (var i = 0; i < _lr0States.Count; i++ )
            {
                _lalrStates.Add(new HashSet<int>());
            }
            foreach (var lr0Kernel in _lr0Kernels)
            {
                var j = new HashSet<int>();
                foreach (int jLR0ItemID in lr0Kernel)
                {
                    var lr1Item = new LR1Item(jLR0ItemID, -1);
                    int nLR1ItemID = GetLR1ItemID(lr1Item);
                    j.Add(nLR1ItemID);
                }
                var jPrime = LR1Closure(j);
                foreach (int jpLR1ItemID in jPrime)
                {
                    var lr1Item = _lr1Items[jpLR1ItemID];
                    var lr0Item = _lr0Items[lr1Item.LR0ItemID];

                    if ((lr1Item.LookAhead != -1) || (nLR0State == 0))
                    {
                        _lalrStates[nLR0State].Add(jpLR1ItemID);
                    }

                    if (lr0Item.Position < _productions[lr0Item.Production].Right.Length)
                    {
                        int nToken = _productions[lr0Item.Production].Right[lr0Item.Position];
                        var lr0Successor = new LR0Item(lr0Item.Production, lr0Item.Position + 1);
                        int nLR0Successor = GetLR0ItemID(lr0Successor);
                        int nSuccessorState = _lrGotos[nLR0State][nToken];
                        if (lr1Item.LookAhead == -1)
                        {
                            AddPropogation(nLR0State, lr1Item.LR0ItemID, nSuccessorState, nLR0Successor);
                        }
                        else
                        {
                            var lalrItem = new LR1Item( nLR0Successor, lr1Item.LookAhead);
                            int nLALRItemID = GetLR1ItemID(lalrItem);
                            _lalrStates[nSuccessorState].Add(nLALRItemID);
                        }
                    }
                }

                nLR0State++;
            }
        }

        /// <summary>
        /// Calculates the states in the LALR table
        /// </summary>
        void CalculateLookAheads()
        {
            bool bChanged;
            do
            {
                bChanged = false;
                int nState = 0;
                foreach (var statePropogations in _lalrPropogations)
                {
                    bool bStateChanged = false;
                    foreach (int nLR1Item in _lalrStates[nState])
                    {
                        var lr1Item = _lr1Items[nLR1Item];

                        if (statePropogations.ContainsKey(lr1Item.LR0ItemID))
                        {
                            foreach (var lalrPropogation in statePropogations[lr1Item.LR0ItemID])
                            {
                                int nGoto = lalrPropogation.LR0TargetState;
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
        void InitSymbols()
        {
            for (int nSymbol = 0; nSymbol < _grammar.Tokens.Length; nSymbol++)
            {
                var isTerminal = _productions.All(production => production.Left != nSymbol);

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

        void ConvertLR0ItemsToKernels()
        {
            foreach (var lr0State in _lr0States)
            {
                var lr0Kernel = new HashSet<int>();
                foreach (int nLR0Item in lr0State)
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
        void GenerateParseTable()
        {
            _parseTable.Actions = new Action[_lalrStates.Count, _grammar.Tokens.Length + 1];
            for (int nStateID = 0; nStateID < _lalrStates.Count; nStateID++)
            {
                var lalrState = _lalrStates[nStateID];

                for (int nToken = -1; nToken < _grammar.Tokens.Length; nToken++)
                {
                    var actions = new List<Action>();
                    if (nToken >= 0 && _lrGotos[nStateID][nToken] >= 0)
                    {
                        actions.Add(new Action(ActionType.Shift, _lrGotos[nStateID][nToken]));

                    }

                    foreach (int nLR1ItemID in lalrState)
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

                    int nMaxPrecedence = Int32.MinValue;
                    var importantActions = new List<Action>();
                    foreach (Action action in actions)
                    {
                        int nActionPrecedence = Int32.MinValue;
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
                        var shiftAction = null as Action?;
                        var reduceActions = new List<Action>();
                        foreach (Action action in importantActions)
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
                        else if (derivation == Derivation.RightMost && shiftAction != null)
                        {
                            _parseTable.Actions[nStateID, nToken + 1] = shiftAction.Value;
                        }
                        else
                        {
                            var errorType = derivation == Derivation.None && reduceActions.Count == 1
                                                ? ActionType.ErrorSR
                                                : ActionType.ErrorRR;
                            _parseTable.Actions[nStateID, nToken + 1] = new Action(errorType, nToken);
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
        void PopulateProductions()
        {
            int nPrecedence = 0;
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
        /// Based on: http://www.goldparser.org/doc/engine-pseudo/parse-token.htm
        /// </summary>
        /// <param name="input">Input tokens to parse</param>
        /// <param name="debugger">Enables debugging support</param>
        /// <param name="trimReductions">If true (default), trim reductions of the form L -> R, where R is a non-terminal</param>
        /// <returns>The reduced program tree on acceptance or the erroneous token</returns>
        public Token ParseInput(IEnumerable<Token> input, Debug debugger, bool trimReductions = true)
        {
            const int initState = 0;
            var tokenStack = new Stack<Token>();
            var state = initState;

            using (var tokenIterator = new LATokenIterator(input))
            {
                while (true)
                {
                    var token = tokenIterator.LookAhead;
                    var action = ParseTable.Actions[state, token.ID + 1];
                    debugger.DumpParsingState(state, tokenStack, token, action);

                    switch (action.ActionType)
                    {
                        case ActionType.Shift:
                            state = action.ActionParameter;
                            token.State = state;
                            tokenStack.Push(token);
                            tokenIterator.MoveNext();
                            break;

                        case ActionType.Reduce:
                            var prod = Productions[action.ActionParameter];
                            var nChildren = prod.Right.Length;
                            Token reduction;
                            if (trimReductions && nChildren == 1 && _nonterminals.Contains(prod.Right[0]))
                            {
                                reduction = new Token(prod.Left, tokenStack.Pop().Content);
                            }
                            else
                            {
                                var children = new Token[nChildren];
                                for (var i = 0; i < nChildren; i++)
                                {
                                    children[nChildren - i - 1] = tokenStack.Pop();
                                }
                                reduction = new Token(prod.Left, children);
                            }
                            var lastState = tokenStack.Count > 0 ? tokenStack.Peek().State : initState;
                            state = ParseTable.Actions[lastState, prod.Left + 1].ActionParameter;
                            reduction.State = prod.Left;
                            tokenStack.Push(reduction);
                            if (tokenStack.Count == 1 && tokenStack.Peek().ID == 0)
                            {
                                return tokenStack.Pop();
                            }
                            break;

                        case ActionType.Error:
                            return token;

                        case ActionType.ErrorRR:
                            throw new InvalidOperationException("Reduce-Reduce conflict in grammar: " + token);

                        case ActionType.ErrorSR:
                            throw new InvalidOperationException("Shift-Reduce conflict in grammar: " + token);
                    }
                    debugger.Flush();
                }
            }
        }

        /// <summary>
        /// constructor, construct parser table
        /// </summary>
        public Parser(Grammar grammar)
        {
            _lrGotos = new List<int[]>();
            _gotoPrecedence = new List<int[]>();
            _lr0Items = new List<LR0Item>();
            _lr1Items = new List<LR1Item>();
            _lr0States = new List<HashSet<int>>();
            _lr0Kernels = new List<HashSet<int>>();
            _lalrStates = new List<HashSet<int>>();
            _terminals = new HashSet<int>();

            _nonterminals = new HashSet<int>();
            _lalrPropogations = new List<IDictionary<int, IList<LALRPropogation>>>();
            _grammar = grammar;
            _productions = new List<Production>();
            _productionDerivation = new List<Derivation>();
            _productionPrecedence = new List<int>();
            _firstSets = new HashSet<int>[_grammar.Tokens.Length];
            _parseTable = new ParseTable();

            PopulateProductions();
            InitSymbols();
            GenerateLR0Items();
            ComputeFirstSets();
            ConvertLR0ItemsToKernels();
            InitLALRTables();
            CalculateLookAheads();
            GenerateParseTable();
        }
    }
}
