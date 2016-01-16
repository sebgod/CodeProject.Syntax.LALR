using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    /// <summary>
    /// Implement according to: http://www.goldparser.org/doc/engine-pseudo/retrieve-token.htm
    /// and: http://blogs.msdn.com/b/haniatassi/archive/2008/10/23/writing-a-simple-scanner-in-.net.aspx
    /// </summary>
    public class Tokeniser : IAsyncIterator<Token>
    {
        public void Dispose()
        {
            throw new System.NotImplementedException();
        }

        public Task<Token> CurrentAsync()
        {
            throw new System.NotImplementedException();
        }

        public Task<bool> MoveNextAsync()
        {
            throw new System.NotImplementedException();
        }

        public void Reset()
        {
            throw new System.NotImplementedException();
        }

        public bool SupportsResetting { get; private set; }
    }
}