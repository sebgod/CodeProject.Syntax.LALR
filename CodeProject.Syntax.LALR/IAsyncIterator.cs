using System;
using System.Collections;
using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR
{
    /// <summary>
    /// An async iterator, with a similar interface as <seealso cref="IEnumerator"/>
    /// </summary>
    /// <typeparam name="T">Item type to be iterated</typeparam>
    public interface IAsyncIterator<T> : IDisposable
    {
        /// <summary>
        /// Asynchronously returns the last consumed token <seealso cref="IEnumerator.Current"/>
        /// </summary>
        /// <returns>The current token that has been consumed</returns>
        Task<T> CurrentAsync();

        /// <summary>
        /// Asynchronously advances the iterator to the next item.<seealso cref="IEnumerator.MoveNext"/>
        /// </summary>
        /// <returns>True if the iterator advanced successfully</returns>
        Task<bool> MoveNextAsync();

        /// <summary>
        /// Resets the iterator <seealso cref="IEnumerator.Reset"/> if supported <see cref="SupportsResetting"/>
        /// Should throw an <see cref="InvalidOperationException"/> if resetting is not supported
        /// </summary>
        void Reset();

        /// <summary>
        /// True if this implementation supports resetting <see cref="Reset"/>
        /// </summary>
        bool SupportsResetting { get; }
    }
}