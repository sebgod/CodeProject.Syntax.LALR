using System;

namespace CodeProject.Syntax.LALR;

/// <summary>
/// the type of action the parser will perform
/// </summary>
public enum ActionType : byte
{
    Error,
    ErrorRR,
    ErrorSR,
    Reduce,
    Shift,
}

/// <summary>
/// A parse table entry
/// </summary>
public readonly struct Action(ActionType actionType, int actionParameter) : IEquatable<Action>
{
    public ActionType ActionType { get; } = actionType;

    public int ActionParameter { get; } = actionParameter;

    public override bool Equals(object obj)
    {
        return obj is Action a && Equals(a);
    }

    public override int GetHashCode()
    {
        return ((int)ActionType << 24) ^ ActionParameter;
    }

    public bool Equals(Action action)
    {
        return ActionType == action.ActionType && ActionParameter == action.ActionParameter;
    }

    public static bool operator ==(Action left, Action right) => left.Equals(right);

    public static bool operator !=(Action left, Action right) => !left.Equals(right);

    public override string ToString()
    {
        return $"{ActionType} {ActionParameter}";
    }
}

/// <summary>
/// Directs the parser on which action to perform at a given state on a particular input
/// </summary>
public readonly struct ParseTable
{
    public Action[,] Actions { get; }

    /// <summary>
    /// Allocates a fresh <c>states × (tokens + 1)</c> action table, zero-initialised
    /// (every cell <c>(Error, 0)</c> until populated). The table-builder uses this
    /// path; <see cref="ParserTableBuilder"/> then writes each cell.
    /// </summary>
    public ParseTable(int states, int tokens)
    {
        Actions = new Action[states, tokens + 1];
    }

    /// <summary>
    /// Wraps a pre-populated action table — used by the Phase 5 / compiler-compiler
    /// path, where the source generator runs <see cref="ParserTableBuilder"/> at
    /// build time and emits the resulting <see cref="Action"/>[,] as a C# literal.
    /// The runtime parser then constructs a <see cref="Parser"/> with this table
    /// directly, skipping the table-build code (which the trimmer can drop).
    /// </summary>
    public ParseTable(Action[,] actions)
    {
        Actions = actions;
    }

    public int States => Actions.GetLength(0);

    public int Tokens => Actions.GetLength(1);
}
