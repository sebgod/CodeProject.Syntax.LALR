using System.Collections.Generic;

namespace CodeProject.Syntax.LALR.Tests
{
    class PrintableList<T> : List<T>
    {
        public override string ToString()
        {
            return "[" + string.Join(", ", this) + "]";
        }
    }
}
