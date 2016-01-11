using System.Collections.Generic;

namespace CodeProject.Syntax.LALR.Tests
{
    class PrintableList<T> : List<T>
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

        public override string ToString()
        {
            return "[" + string.Join(", ", this) + "]";
        }
    }
}
