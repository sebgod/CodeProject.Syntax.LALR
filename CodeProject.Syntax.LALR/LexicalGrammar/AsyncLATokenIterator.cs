using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public class AsyncLATokenIterator : IAsyncLAIterator<Item>
    {
        private readonly IAsyncIterator<Item> _inputSource;
        private Item _lookAhead;

        public AsyncLATokenIterator(IAsyncIterator<Item> inputSource)
        {
            _inputSource = inputSource;
        }

        public async Task<Item> LookAheadAsync()
        {
            return _lookAhead ?? (_lookAhead = await _inputSource.MoveNextAsync() ? await CurrentAsync() : Item.EOF);
        }

        public async Task<Item> CurrentAsync()
        {
            return await _inputSource.CurrentAsync();
        }

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

        public bool SupportsResetting
        {
            get { return _inputSource.SupportsResetting; }
        }

        public void Dispose()
        {
            _lookAhead = null;
            _inputSource.Dispose();
        }
    }
}
