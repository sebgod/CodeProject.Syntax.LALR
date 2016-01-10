using System;
using System.Collections;
using System.Linq;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public enum ContentType : byte
    {
        Empty,
        Reduction,
        Nested,
        Leaf
    }

    public struct TokenCategory : IEquatable<TokenCategory>
    {
        private readonly int _id;

        private readonly string _name;

        public int ID { get { return _id; } }

        public string Name { get { return _name; } }

        public TokenCategory(int id, string name)
        {
            _id = id;
            _name = name;
        }

        public override bool Equals(object obj)
        {
            return obj is TokenCategory && Equals((TokenCategory)obj);
        }

        public override int GetHashCode()
        {
            return _id;
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", ID, Name);
        }

        public bool Equals(TokenCategory other)
        {
            return _id == other.ID;
        }
    }

    public class Token : IEquatable<Token>
    {
        public static readonly Token EOF = new Token(-1, "$");

        private readonly int _id;
        private readonly object _content;
        private readonly ContentType _contentType;

        public int ID { get { return _id; } }

        public object Content { get { return _content; } }

        public Reduction Reduction { get { return (Reduction) _content; } }

        public Token Nested { get { return (Token) _content; } }

        public int State { get; set; }

        public ContentType ContentType { get { return _contentType; } }

        public Token(int id, object content)
        {
            _id = id;
            _content = content;
            _contentType = DetermineContentType(_content);

            State = ContentType == ContentType.Reduction && Reduction.Children.Any(p => p.IsError) ? -1 : id;
        }

        public static ContentType DetermineContentType(object content)
        {
            if (content == null)
            {
                return ContentType.Empty;
            }
            if (content is Token)
            {
                return ContentType.Nested;
            }
            if (content is Reduction)
            {
                return ContentType.Reduction;
            }
            return ContentType.Leaf;
        }

        public bool IsError
        {
            get
            {
                if (State < 0)
                {
                    return true;
                }

                var isError = false;
                switch (_contentType)
                {
                    case ContentType.Nested:
                        isError = ((Token) _content).IsError;
                        break;

                    case ContentType.Reduction:
                        isError = Reduction.Children.Any(p => p.IsError);
                        break;
                }
                if (isError)
                {
                    State = -1;
                }
                return isError;
            }
        }

        public bool Equals(Token other)
        {
            return ID == other.ID;
        }

        public override int GetHashCode()
        {
            return ID;
        }

        public override bool Equals(object obj)
        {
            return obj is Token && Equals((Token) obj);
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
