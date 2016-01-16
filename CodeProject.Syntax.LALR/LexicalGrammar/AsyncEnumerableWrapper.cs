using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct AsyncEnumerableWrapper<T> : IAsyncIterator<T>
    {
        private readonly IEnumerator<T> _enumerator;

        public AsyncEnumerableWrapper(IEnumerable<T> enumerable)
        {
            _enumerator = enumerable.GetEnumerator();
        }

        public void Dispose()
        {
            _enumerator.Dispose();
        }

        public Task<T> CurrentAsync()
        {
            return Task.FromResult(_enumerator.Current);
        }

        public Task<bool> MoveNextAsync()
        {
            return Task.FromResult(_enumerator.MoveNext());
        }

        public void Reset()
        {
            throw new InvalidOperationException("Resetting is not supported!");
        }

        public bool SupportsResetting { get { return false; } }
    }

    public static class AsyncEnumerableWrapperEx
    {
        public static IAsyncIterator<T> AsAsync<T>(this IEnumerable<T> @this)
        {
            return new AsyncEnumerableWrapper<T>(@this);
        }
    }
}
