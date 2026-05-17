using System;

namespace LALR.CC;

/// <summary>
/// An item required for an LR0 Parser Construction
/// </summary>
public readonly struct LR0Item(int production, int position) : IEquatable<LR0Item>
{
    public int Production { get; } = production;

    public int Position { get; } = position;

    public override bool Equals(object obj)
    {
        return obj is LR0Item item && Equals(item);
    }

    public override int GetHashCode()
    {
        return (Production << 16) ^ Position;
    }

    public bool Equals(LR0Item item)
    {
        return Production == item.Production && Position == item.Position;
    }

    public static bool operator ==(LR0Item left, LR0Item right) => left.Equals(right);

    public static bool operator !=(LR0Item left, LR0Item right) => !left.Equals(right);

    public override string ToString()
    {
        return $"production={Production} position={Position}";
    }
}

/// <summary>
/// An item required for an LR1 Parser Construction
/// </summary>
public readonly struct LR1Item(int lr0ItemID, int lookAhead) : IEquatable<LR1Item>
{
    public int LR0ItemID { get; } = lr0ItemID;

    public int LookAhead { get; } = lookAhead;

    public override bool Equals(object obj)
    {
        return obj is LR1Item item && Equals(item);
    }

    public override int GetHashCode()
    {
        return (LR0ItemID << 16) ^ LookAhead;
    }

    public bool Equals(LR1Item item)
    {
        return LR0ItemID == item.LR0ItemID && LookAhead == item.LookAhead;
    }

    public static bool operator ==(LR1Item left, LR1Item right) => left.Equals(right);

    public static bool operator !=(LR1Item left, LR1Item right) => !left.Equals(right);

    public override string ToString()
    {
        return $"LR0#={LR0ItemID} lookAhead={LookAhead}";
    }
}

public readonly struct LALRPropogation(int lr0TargetItem, int lr0TargetState) : IEquatable<LALRPropogation>
{
    public int LR0TargetState { get; } = lr0TargetState;

    public int LR0TargetItem { get; } = lr0TargetItem;

    public override bool Equals(object obj)
    {
        return obj is LALRPropogation prop && Equals(prop);
    }

    public override int GetHashCode()
    {
        return (LR0TargetState << 16) ^ LR0TargetItem;
    }

    public bool Equals(LALRPropogation other)
    {
        return LR0TargetItem == other.LR0TargetItem && LR0TargetState == other.LR0TargetState;
    }

    public static bool operator ==(LALRPropogation left, LALRPropogation right) => left.Equals(right);

    public static bool operator !=(LALRPropogation left, LALRPropogation right) => !left.Equals(right);

    public override string ToString()
    {
        return $"LR0 target={LR0TargetItem} state={LR0TargetState}";
    }
}
