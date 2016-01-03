using System.Collections.Generic;

namespace CodeProject.Syntax.LALR
{
    public class Reduction
    {
        private readonly int _production;
        private readonly Token[] _children;

        public int Production { get { return _production; } }

        public IList<Token> Children { get { return _children; } }

        public Reduction(int production, Token[] children)
        {
            _production = production;
            _children = children;
        }
    }
}
