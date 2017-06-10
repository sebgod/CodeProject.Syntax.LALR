using System;
using System.Collections;
using System.Linq;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public enum ContentType : byte
    {
        Reduction,
        Nested,
        Scalar
    }

    public class Item : IEquatable<Item>
    {
        public static readonly Item EOF = new Item(-1, "$");

        private readonly int _id;
        private readonly object _content;
        private readonly ContentType _contentType;
        private int _state;
        private bool? _inError;

        public int ID { get { return _id; } }

        public object Content { get { return _content; } }

        public Reduction Reduction { get { return (Reduction) _content; } }

        public Item Nested { get { return (Item) _content; } }

        public int State
        {
            get { return _state; }
            set
            {
                _state = value;
                _inError = null;
            }
        }

        public ContentType ContentType { get { return _contentType; } }

        public Item(int id, object content)
        {
            _id = id;
            _content = content;
            _contentType = DetermineContentType(_content);

            State = ContentType == ContentType.Reduction && Reduction.Children.Any(p => p.IsError) ? -1 : id;
        }

        private static ContentType DetermineContentType(object content)
        {
            if (content == null)
            {
                return ContentType.Scalar;
            }
            if (content is Item)
            {
                return ContentType.Nested;
            }
            if (content is Reduction)
            {
                return ContentType.Reduction;
            }
            return ContentType.Scalar;
        }

        public bool IsError
        {
            get
            {
                if (_inError.HasValue)
                {
                    return _inError.Value;
                }
                if (State < 0)
                {
                    return true;
                }

                _inError = false;
                switch (_contentType)
                {
                    case ContentType.Nested:
                        _inError = ((Item) _content).IsError;
                        break;

                    case ContentType.Reduction:
                        _inError = Reduction.Children.Any(p => p.IsError);
                        break;
                }
                if (_inError == true)
                {
                    State = -1;
                }
                return _inError.Value;
            }
        }

        public bool Equals(Item other)
        {
            return ID == other.ID;
        }

        public override int GetHashCode()
        {
            return ID;
        }

        public override bool Equals(object obj)
        {
            return obj is Item && Equals((Item) obj);
        }

        private static string ContentToString(object content)
        {
            if (content == null)
            {
                return string.Empty;
            }

            var stringContent = content as string;
            if (stringContent != null)
            {
                return stringContent;
            }

            var enumerable = content as IEnumerable;
            return enumerable != null
                       ? "[" + string.Join(" ", enumerable.Cast<object>().Select(ContentToString)) + "]"
                       : content.ToString();
        }

        public override string ToString()
        {
            return ContentToString(Content);
        }
    }
}
