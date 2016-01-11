using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct GroupRx : IRx
    {
        private readonly IRx[] _items;
        private readonly Multiplicity _multiplicity;

        public GroupRx(Multiplicity multiplicity, params IRx[] items)
        {
            if (items == null)
            {
                throw new ArgumentNullException("items");
            }
            if (items.Length == 0)
            {
                throw new ArgumentException("Items should not be empty!", "items");
            }

            _items = items;
            _multiplicity = multiplicity;
        }

        public string Pattern
        {
            get
            {
                return _items.Length == 1
                    ? _items[0].Pattern + _multiplicity
                    : string.Format("({0}){1}", string.Concat(_items.Select(p => p.Pattern)) + _multiplicity.Pattern);
            }
        }

        public override string ToString()
        {
            return Pattern;
        }
    }
}
