namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public interface IRx
    {
        string Pattern { get; }
    }

    public interface ISingleCharRx : IRx
    {
        // only a marker interface
    }

    public interface IClassRx : ISingleCharRx
    {
        string PatternWithoutBrackets { get; }
    }
}