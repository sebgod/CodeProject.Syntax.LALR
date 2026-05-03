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

    public ParseTable(int states, int tokens)
    {
        Actions = new Action[states, tokens + 1];
    }

    public int States => Actions.GetLength(0);

    public int Tokens => Actions.GetLength(1);
}
