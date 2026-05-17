using System.Threading.Tasks;

namespace LALR.CC.LexicalGrammar;

public class AsyncLATokenIterator(IAsyncIterator<Item> inputSource) : IAsyncLAIterator<Item>
{
    private readonly IAsyncIterator<Item> _inputSource = inputSource;
    private Item _lookAhead;

    public async Task<Item> LookAheadAsync()
    {
        return _lookAhead ??= await _inputSource.MoveNextAsync() ? await CurrentAsync() : Item.EOF;
    }

    public async Task<Item> CurrentAsync() => await _inputSource.CurrentAsync();

    public async Task<bool> MoveNextAsync()
    {
        if (_lookAhead != null)
        {
            _lookAhead = null;
            return true;
        }
        return await _inputSource.MoveNextAsync();
    }

    public void Reset()
    {
        _lookAhead = null;
        _inputSource.Reset();
    }

    public bool SupportsResetting => _inputSource.SupportsResetting;

    public void Dispose()
    {
        _lookAhead = null;
        _inputSource.Dispose();
    }
}
