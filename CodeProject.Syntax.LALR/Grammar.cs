using System;
using System.Collections.Generic;

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
    public class Production
    {
        private readonly int _left;
        private readonly int[] _right;
        private readonly Func<int, Token[], object> _rewriter;

        public int Left { get { return _left; } }
        public int[] Right { get { return _right; } }

        public Production(int left, params int[] right)
            : this(left, null, right)
        {
            // calls Production(int left, Func<Token[], object> rewriter, params int[] right)
        }

        public Production(int left, Func<int, Token[], object> rewriter, params int[] right)
        {
            _left = left;
            _right = right;
            _rewriter = rewriter ?? DefaultReduction;
        }

        public object Rewrite(Token[] children)
        {
            return _rewriter(Left, children) ?? DefaultReduction(Left, children);
        }

        private static Reduction DefaultReduction(int production, Token[] children)
        {
            return new Reduction(production, children);
        }
    };

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
        private readonly string[] _tokens;
        private readonly PrecedenceGroup[] _precedenceGroups;

        public string[] Tokens { get { return _tokens; } }

        public PrecedenceGroup[] PrecedenceGroups { get { return _precedenceGroups; } }

        public Grammar(string[] tokens, params PrecedenceGroup[] precedenceGroups)
        {
            _tokens = tokens;
            _precedenceGroups = precedenceGroups;
        }
    };
}

