using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR
{
    public struct AsyncEnumerableWrapper : IAsyncIterator<Token>
    {
        private readonly IEnumerator<Token> _tokens;

        public AsyncEnumerableWrapper(IEnumerable<Token> tokens)
        {
            _tokens = tokens.GetEnumerator();
        }

        public void Dispose()
        {
            _tokens.Dispose();
        }

        public Task<Token> CurrentAsync()
        {
            return Task.FromResult(_tokens.Current);
        }

        public Task<bool> MoveNextAsync()
        {
            return Task.FromResult(_tokens.MoveNext());
        }

        public void Reset()
        {
            throw new InvalidOperationException("Resetting is not supported!");
        }

        public bool SupportsResetting { get { return false; } }
    }
}
