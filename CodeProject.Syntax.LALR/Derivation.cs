namespace CodeProject.Syntax.LALR;

/// <summary>
/// Describes how to resolve ambiguities within a precedence group
/// </summary>
public enum Derivation : byte
{
    None,
    LeftMost,
    RightMost,
}
