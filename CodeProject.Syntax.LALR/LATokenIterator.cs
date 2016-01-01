using System.Collections;
using System.Collections.Generic;

namespace CodeProject.Syntax.LALR
{
    public class LATokenIterator : IEnumerator<Token>
    {
        private readonly IEnumerator<Token> _tokens;

        public LATokenIterator(IEnumerable<Token> tokens)
        {
            _tokens = tokens.GetEnumerator();
        }

        private Token _lookAhead;

        public Token LookAhead
        {
            get { return _lookAhead ?? (_lookAhead = _tokens.MoveNext() ? Current : Token.EOF); }
        }

        public bool MoveNext()
        {
            if (_lookAhead != null)
            {
                _lookAhead = null;
                return true;
            }
            return _tokens.MoveNext();
        }

        public void Reset()
        {
            _lookAhead = null;
            _tokens.Reset();
        }

        public Token Current { get { return _tokens.Current; } }

        object IEnumerator.Current
        {
            get { return Current; }
        }

        public void Dispose()
        {
            _lookAhead = null;
            _tokens.Dispose();
        }
    }
}
