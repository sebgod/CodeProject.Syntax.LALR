using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CodeProject.Syntax.LALR
{
    public class Debug
    {
        private const string Condition = "DEBUG";

        private readonly Parser _parser;
        private readonly StringBuilder _builder;
        private readonly Action<string> _infoWriter;
        private readonly Action<string> _errorWriter;

        public Debug(Parser parser, Action<string> infoWriter = null, Action<string> errorWriter = null)
        {
            _parser = parser;
            _builder = new StringBuilder();
            _infoWriter = infoWriter;
            _errorWriter = errorWriter ?? _infoWriter;
        }

// ReSharper disable MemberCanBePrivate.Global
        [Conditional(Condition)]
        public void Flush()
        {
            if (_infoWriter != null)
            {
                _infoWriter(ToString());
            }
            _builder.Clear();
        }

        public void WriteFinalToken(string acceptMsg, string failMsg, Item item)
        {
            if (item.IsError && _errorWriter != null)
            {
                _errorWriter(failMsg + TokenInfo(item) + Environment.NewLine);
            }
            else if (item.State == 0 && _infoWriter != null)
            {
                _infoWriter(acceptMsg + TokenInfo(item) + Environment.NewLine);
            }
        }

        public override string ToString()
        {
            return _builder.ToString();
        }

        /// <summary>
        /// Gets the name of a particular Item
        /// </summary>
        /// <param name="nTokenID">
        /// The Item id <see cref="System.Int32"/>
        /// </param>
        /// <returns>
        /// The Item name <see cref="System.String"/>
        /// </returns>
        public string GetTokenName(int nTokenID)
        {
            return nTokenID == -1 ? "$" : _parser.Grammar.SymbolNames[nTokenID].Name;
        }

        /// <summary>
        /// Outputs an LR0 Item
        /// </summary>
        /// <param name="item">
        /// The LR0Item <see cref="LR0Item"/>
        /// </param>
        [Conditional(Condition)]
        public void DumpLR0Item(LR0Item item)
        {
            _builder.Append(_parser.Grammar.SymbolNames[_parser.Productions[item.Production].Left]);
            _builder.Append(" ->");
            int nPosition = 0;
            for (; ; )
            {
                if (nPosition == item.Position)
                {
                    _builder.Append(" *");
                }
                if (nPosition >= _parser.Productions[item.Production].Right.Length)
                {
                    break;
                }
                int nToken = _parser.Productions[item.Production].Right[nPosition];
                _builder.Append(" " + _parser.Grammar.SymbolNames[nToken]);

                nPosition++;
            }
        }

        /// <summary>
        /// Outputs an LR0 State
        /// </summary>
        /// <param name="state">
        /// A set of LR0 Item IDs
        /// </param>
        [Conditional(Condition)]
        public void DumpLR0State(IEnumerable<int> state)
        {
            foreach (int nLR0Item in state)
            {
                DumpLR0Item(_parser.LR0Items[nLR0Item]);
                _builder.AppendLine();
            }
        }

        /// <summary>
        /// Outputs the entire set of LR0 Items
        /// </summary>
        [Conditional(Condition)]
        public void DumpLR0Items()
        {
            _builder.AppendLine("LR0Items:");
            foreach (var item in _parser.LR0Items)
            {
                DumpLR0Item(item);
                _builder.AppendLine();
            }
        }

        /// <summary>
        /// Outputs the entire set of LR0 States
        /// </summary>
        [Conditional(Condition)]
        public void DumpLR0States()
        {
            _builder.AppendLine("LR0States:");
            int nState = 0;
            foreach (var state in _parser.LR0States)
            {
                _builder.AppendLine("State " + nState.ToString(CultureInfo.InvariantCulture) + ":");
                DumpLR0State(state);
                nState++;
            }
        }

        /// <summary>
        /// Outputs the entire set of LR0 Kernels
        /// </summary>
        [Conditional(Condition)]
        public void DumpLR0Kernels()
        {
            _builder.AppendLine("LR0Kernels:");
            int nState = 0;
            foreach (var state in _parser.LR0Kernels)
            {
                _builder.AppendLine("Kernel " + nState + ":");
                DumpLR0State(state);
                nState++;
            }
        }

        /// <summary>
        /// Outputs the first sets of each Item
        /// </summary>
        [Conditional(Condition)]
        public void DumpFirstSets()
        {
            for (var nToken = 0; nToken < _parser.Grammar.SymbolNames.Length; nToken++)
            {
                _builder.AppendFormat("FIRST({0}) = {{{1}}}",
                                      _parser.Grammar.SymbolNames[nToken],
                                      string.Join(", ",
                                                  _parser.FirstSets[nToken].Select(
                                                      pFirst => pFirst == -1 ? "#" : _parser.Grammar.SymbolNames[pFirst].Name))
                    );
            }
        }

        /// <summary>
        /// Outputs an LR1 State
        /// </summary>
        /// <param name="lr1State">
        /// A List of LR1 Item IDs
        /// </param>
        [Conditional(Condition)]
        public void DumpLR1State(IEnumerable<int> lr1State)
        {
            foreach (var lr1Item in lr1State.Select(nLR1Item => _parser.LR1Items[nLR1Item]))
            {
                DumpLR1Item(lr1Item);
            }
        }

        /// <summary>
        /// Outputs all of the terminals in the grammar
        /// </summary>
        [Conditional(Condition)]
        public void DumpTerminals()
        {
            _builder.AppendFormat("TERMINALS = {{{0}}}", string.Join(", ", _parser.Terminals));
        }

        /// <summary>
        /// Outputs all of the Non Terminals in the grammar
        /// </summary>
        [Conditional(Condition)]
        public void DumpNonterminals()
        {
            _builder.AppendFormat("NONTERMINALS = {{{0}}}", string.Join(", ", _parser.NonTerminals));
        }

        /// <summary>
        /// Outputs the LALR propogations for a particular state
        /// </summary>
        /// <param name="nStateID">
        /// The state ID <see cref="System.Int32"/>
        /// </param>
        [Conditional(Condition)]
        public void DumpPropogationsForState(int nStateID)
        {
            if (nStateID >= _parser.LALRPropogations.Count) return;

            var propogationsForState = _parser.LALRPropogations[nStateID];
            _builder.AppendLine("For State " + nStateID + ":");
            foreach (var nItem in propogationsForState.Keys)
            {
                var item = _parser.LR0Items[nItem];
                DumpLR0Item(item);
                _builder.Append("-> {");
                var propogations = propogationsForState[nItem];
                foreach (var propogation in propogations)
                {
                    _builder.Append(" state " + propogation.LR0TargetState + ":");
                    DumpLR0Item(_parser.LR0Items[propogation.LR0TargetItem]);
                }
                _builder.AppendLine("}");

            }
        }

        /// <summary>
        /// Outputs the propogation table for the grammar
        /// </summary>
        [Conditional(Condition)]
        public void DumpPropogationTable()
        {
            for (int nStateID = 0; nStateID < _parser.LALRPropogations.Count; nStateID++)
            {
                DumpPropogationsForState(nStateID);
            }
        }

        /// <summary>
        /// Outputs an LR1 Item
        /// </summary>
        /// <param name="lr1Item">
        /// The LR1 Item <see cref="LR1Item"/>
        /// </param>
        [Conditional(Condition)]
        public void DumpLR1Item(LR1Item lr1Item)
        {
            var item = _parser.LR0Items[lr1Item.LR0ItemID];
            _builder.Append(_parser.Grammar.SymbolNames[_parser.Productions[item.Production].Left]);
            _builder.Append(" ->");
            int nPosition = 0;
            for (; ; )
            {
                if (nPosition == item.Position)
                {
                    _builder.Append(" *");
                }
                if (nPosition >= _parser.Productions[item.Production].Right.Length)
                {
                    break;
                }
                int nToken = _parser.Productions[item.Production].Right[nPosition];
                _builder.Append(" ").Append(_parser.Grammar.SymbolNames[nToken]);

                nPosition++;
            }
            if (lr1Item.LookAhead == -1)
            {
                _builder.Append(", $");
            }
            else
            {
                _builder.Append(", ").Append(_parser.Grammar.SymbolNames[lr1Item.LookAhead]);
            }
            _builder.AppendLine();
        }

        /// <summary>
        /// Outputs the LALR States for the parser
        /// </summary>
        [Conditional(Condition)]
        public void DumpLALRStates()
        {
            var nStateID = 0;
            foreach (var lalrState in _parser.LALRStates)
            {
                _builder.AppendLine("State " + nStateID + ":");
                foreach (var lr1Item in lalrState.Select(nLR1Item => _parser.LR1Items[nLR1Item]))
                {
                    DumpLR1Item(lr1Item);
                }
                nStateID++;
            }
        }

        /// <summary>
        /// Outputs the entire set of LR1 items constructed
        /// </summary>
        [Conditional(Condition)]
        public void DumpLR1Items()
        {
            int nLR1Item = 0;
            foreach (var lr1Item in _parser.LR1Items)
            {
                _builder.Append("LR1Item " + nLR1Item + ":");
                DumpLR1Item(lr1Item);
                nLR1Item++;
            }
        }

        /// <summary>
        /// Outputs a formatted parse table
        /// </summary>
        [Conditional(Condition)]
        public void DumpParseTable()
        {
            var nStates = _parser.ParseTable.States;
            var nTokens = _parser.ParseTable.Tokens;

            _builder.EnsureCapacity(_builder.Capacity + nStates * 3 * nTokens * 6);

            _builder.HzEdge(nTokens, BoxDrawing.Edge.Top)
                .AppendFormat("│{0,5} │", "State");
            for (var nToken = 0; nToken < nTokens; nToken++)
            {
                _builder.AppendFormat(" {0,4} │", GetTokenName(nToken - 1));
            }
            _builder.AppendLine()
                .HzEdge(nTokens);

            for (var nState = 0; nState < nStates; nState++)
            {
                _builder.AppendFormat("│ {0, 4} │", nState);
                for (var nToken = 0; nToken < nTokens; nToken++)
                {
                    var action = _parser.ParseTable.Actions[nState, nToken];
                    var sAction = "";
                    object oParm = "";
                    switch (action.ActionType)
                    {
                        case ActionType.Reduce:
                            sAction = "R";
                            oParm = action.ActionParameter;
                            break;
                        case ActionType.Shift:
                            sAction = "S";
                            oParm = action.ActionParameter;
                            break;

                        case ActionType.ErrorRR:
                            sAction = "RR";
                            oParm = action.ActionParameter;
                            break;

                        case ActionType.ErrorSR:
                            sAction = "SR";
                            oParm = action.ActionParameter;
                            break;
                    }
                    _builder.AppendFormat("{0,2}{1,-4}│", sAction, oParm);
                }

                _builder.AppendLine()
                    .HzEdge(nTokens, nState + 1 < nStates ? BoxDrawing.Edge.Middle : BoxDrawing.Edge.Bottom);
            }
        }

        [Conditional(Condition)]
        public void DumpParsingState(int state, IEnumerable<Item> tokenStack, Item item, Action action)
        {
            _builder.AppendFormat("state={0,-3} la={1,-4} {2,-10} {3}", state, item, action,
                                  string.Join(", ", tokenStack.Select(TokenInfo))).AppendLine();
        }

        public string TokenInfo(Item item)
        {
            return TokenInfo(item, false);
        }
        public string TokenInfo(Item item, bool detailed)
        {
            var info = new StringBuilder(20);
            TokenInfo(item, info, detailed);
            return info.ToString();
        }

        private void TokenInfo(Item item, StringBuilder info, bool detailed = false)
        {
            if (info.Length > 70)
            {
                info.Append("...");
                return;
            }

            var name = GetTokenName(item.ID);
            if (_parser.NonTerminals.Contains(item.ID))
            {
                switch (item.ContentType)
                {
                    case ContentType.Reduction:
                        var reduction = item.Reduction;
                        var production = _parser.Productions[reduction.Production];
                        info.AppendFormat("{0}->{1}[", name, string.Concat(production.Right.Select(GetTokenName)));
                        for (var i = 0; i < reduction.Children.Count; i++)
                        {
                            if (i > 0)
                            {
                                info.Append(' ');
                            }
                            TokenInfo(reduction.Children[i], info);
                        }
                        info.Append(']');
                        break;
                    case ContentType.Nested:
                        info.AppendFormat("{0}->[", name);
                        TokenInfo(item.Nested, info);
                        info.Append(']');
                        break;
                }
            }
            else if (detailed)
            {
                info.AppendFormat("[{0}] {1} \"{2}\"", item.ID, name, item);
            }
            else
            {
                info.Append(item);
            }
        }
// ReSharper restore MemberCanBePrivate.Global
    }
}
