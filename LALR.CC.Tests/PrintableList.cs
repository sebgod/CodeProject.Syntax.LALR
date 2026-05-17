using System.Collections.Generic;

namespace LALR.CC.Tests;

internal class PrintableList<T> : List<T>
{
    public PrintableList()
    {
        // calls base constructor
    }

    public PrintableList(int count)
        : base(count)
    {
        // calls base constructor
    }

    public PrintableList(IEnumerable<T> collection)
        : base(collection)
    {
        // calls base constructor
    }

    public override string ToString() => "[" + string.Join(", ", this) + "]";
}
