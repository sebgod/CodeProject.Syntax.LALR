using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public class AsyncLATokenIterator : IAsyncLAIterator<Token>
    {
        private readonly IAsyncIterator<Token> _inputSource;
        private Token _lookAhead;

        public AsyncLATokenIterator(IAsyncIterator<Token> inputSource)
        {
            _inputSource = inputSource;
        }

        public async Task<Token> LookAheadAsync()
        {
            return _lookAhead ?? (_lookAhead = await _inputSource.MoveNextAsync() ? await CurrentAsync() : Token.EOF);
        }

        public async Task<Token> CurrentAsync()
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
