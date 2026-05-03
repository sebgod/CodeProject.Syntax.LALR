using System;

namespace CodeProject.Syntax.LALR.LexicalGrammar;

public readonly struct SymbolName(int id, string name) : IEquatable<SymbolName>
{
    public int ID { get; } = id;

    public string Name { get; } = name;

    public override bool Equals(object obj)
    {
        return obj is SymbolName other && Equals(other);
    }

    public override int GetHashCode()
    {
        return ID;
    }

    public override string ToString()
    {
        return $"{ID}: {Name}";
    }

    public bool Equals(SymbolName other)
    {
        return ID == other.ID;
    }

    public static bool operator ==(SymbolName left, SymbolName right) => left.Equals(right);

    public static bool operator !=(SymbolName left, SymbolName right) => !left.Equals(right);
}
