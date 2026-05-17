using System.Threading.Tasks;

namespace LALR.CC;

/// <summary>
/// An async lookahead (1) iterator
/// </summary>
/// <typeparam name="T">Item type to be iterated</typeparam>
public interface IAsyncLAIterator<T> : IAsyncIterator<T>
{
    /// <summary>
    /// Asynchronously returns the next item which is not yet consumed
    /// </summary>
    /// <returns>Lookahead item</returns>
    Task<T> LookAheadAsync();
}
