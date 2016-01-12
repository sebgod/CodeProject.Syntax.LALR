namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public interface IRx
    {
        string Pattern { get; }
    }

    public interface ISingleCharRx : IRx
    {
        string PatternInsideClass { get; }
    }

    public interface IClassRx : ISingleCharRx
    {
        bool Positive { get; }
    }
}