using CodeProject.Syntax.LALR.LexicalGrammar;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeProject.Syntax.LALR
{
    /// <summary>
    /// Describes how to resolve ambiguities within a precedence group
    /// </summary>
    public enum Derivation : byte
    {
        None,
        LeftMost,
        RightMost
    };

    /// <summary>
    /// A grammatical production
    /// </summary>
    public struct Production
    {
        private readonly int _left;
        private readonly int[] _right;
        private readonly Func<int, Item[], object> _rewriter;

        public int Left
        {
            get { return _left; }
        }

        public int[] Right
        {
            get { return _right; }
        }

        public Production(int left, params int[] right)
            : this(left, null, right)
        {
            // calls below
        }

        public Production(int left, Func<int, Item[], object> rewriter, params int[] right)
        {
            _left = left;
            _right = right;
            _rewriter = rewriter;
        }

        public object Rewrite(Item[] children)
        {
            return _rewriter != null ? _rewriter(Left, children) : null;;
        }
    }

    /// <summary>
    /// A reduced production with all leaf tokens and a reference to the reduced production
    /// </summary>
    public class Reduction
    {
        private readonly int _production;
        private readonly Item[] _children;

        /// <summary>
        /// Reference to the reduced production in the production table
        /// </summary>
        public int Production { get { return _production; } }

        public IList<Item> Children { get { return _children; } }

        public Reduction(int production, params Item[] children)
        {
            _production = production;
            _children = children;
        }
    }

    /// <summary>
    /// A collection of productions at a particular precedence
    /// </summary>
    public struct PrecedenceGroup
    {
        private readonly Production[] _productions;
        private readonly Derivation _derivation;

        public Derivation Derivation { get { return _derivation; } }
        public IEnumerable<Production> Productions { get { return _productions; } }

        public PrecedenceGroup(Derivation derivation, params Production[] productions)
        {
            _productions = productions;
            _derivation = derivation;
        }
    }

    /// <summary>
    /// All of the information required to make a Parser
    /// </summary>
    public struct Grammar
    {
        private readonly SymbolName[] _symbolNames;
        private readonly PrecedenceGroup[] _precedenceGroups;

        public SymbolName[] SymbolNames { get { return _symbolNames; } }

        public PrecedenceGroup[] PrecedenceGroups { get { return _precedenceGroups; } }

        public Grammar(string[] symbolNames, params PrecedenceGroup[] precedenceGroups)
            : this(AsSymbolNames(symbolNames), precedenceGroups)
        {
            // calls below
        }

        public Grammar(SymbolName[] symbolNames, params PrecedenceGroup[] precedenceGroups)
        {
            _symbolNames = symbolNames;
            _precedenceGroups = precedenceGroups;
        }

        private static SymbolName[] AsSymbolNames(params string[] symbolNames)
        {
            return symbolNames.Select((pName, pIndex) => new SymbolName(pIndex, pName)).ToArray();
        }
    };
}

