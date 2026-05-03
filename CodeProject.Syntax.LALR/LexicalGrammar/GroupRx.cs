using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeProject.Syntax.LALR.LexicalGrammar;

public readonly struct GroupRx : IRx
{
    private readonly IRx[] _items;
    private readonly Multiplicity _multiplicity;

    public GroupRx(Multiplicity multiplicity, params IRx[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Length == 0)
        {
            throw new ArgumentException("Items should not be empty!", nameof(items));
        }

        _items = items;
        _multiplicity = multiplicity;
    }

    internal IReadOnlyList<IRx> Items => _items;
    internal Multiplicity Multiplicity => _multiplicity;

    public string Pattern => _items.Length == 1 && (_items[0] is ISingleCharRx || _multiplicity == Multiplicity.Once)
        ? _items[0].Pattern + _multiplicity
        : $"({string.Concat(_items.Select(p => p.Pattern))}){_multiplicity.Pattern}";

    public override string ToString() => Pattern;
}
