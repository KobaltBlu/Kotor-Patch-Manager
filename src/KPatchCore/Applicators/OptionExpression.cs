namespace KPatchCore.Applicators;

/// <summary>
/// Minimal integer expression evaluator for computed patch options.
/// Supports identifiers, decimal/hex literals, + - * / % &amp; | ^ &lt;&lt; &gt;&gt;, unary -, and parentheses.
/// </summary>
internal static class OptionExpression
{
    public static bool TryEvaluate(
        string expression,
        IReadOnlyDictionary<string, int> variables,
        out int result,
        out string error)
    {
        result = 0;
        error = string.Empty;

        try
        {
            var parser = new Parser(expression, variables);
            result = parser.ParseExpression();
            parser.ExpectEnd();
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _src;
        private readonly IReadOnlyDictionary<string, int> _vars;
        private int _pos;

        public Parser(string src, IReadOnlyDictionary<string, int> vars)
        {
            _src = src;
            _vars = vars;
        }

        public int ParseExpression() => ParseBitwiseOr();

        public void ExpectEnd()
        {
            SkipWs();
            if (_pos < _src.Length)
                throw new FormatException($"Unexpected '{_src[_pos]}' in expression");
        }

        private int ParseBitwiseOr()
        {
            var left = ParseBitwiseXor();
            while (Match('|'))
                left |= ParseBitwiseXor();
            return left;
        }

        private int ParseBitwiseXor()
        {
            var left = ParseBitwiseAnd();
            while (Match('^'))
                left ^= ParseBitwiseAnd();
            return left;
        }

        private int ParseBitwiseAnd()
        {
            var left = ParseShift();
            while (Match('&'))
                left &= ParseShift();
            return left;
        }

        private int ParseShift()
        {
            var left = ParseAdd();
            while (true)
            {
                if (Match("<<"))
                    left <<= ParseAdd();
                else if (Match(">>"))
                    left >>= ParseAdd();
                else
                    break;
            }
            return left;
        }

        private int ParseAdd()
        {
            var left = ParseMul();
            while (true)
            {
                if (Match('+'))
                    left += ParseMul();
                else if (Match('-'))
                    left -= ParseMul();
                else
                    break;
            }
            return left;
        }

        private int ParseMul()
        {
            var left = ParseUnary();
            while (true)
            {
                if (Match('*'))
                    left *= ParseUnary();
                else if (Match('/'))
                {
                    var right = ParseUnary();
                    if (right == 0)
                        throw new FormatException("Division by zero");
                    left /= right;
                }
                else if (Match('%'))
                {
                    var right = ParseUnary();
                    if (right == 0)
                        throw new FormatException("Modulo by zero");
                    left %= right;
                }
                else
                    break;
            }
            return left;
        }

        private int ParseUnary()
        {
            if (Match('+'))
                return ParseUnary();
            if (Match('-'))
                return -ParseUnary();
            return ParsePrimary();
        }

        private int ParsePrimary()
        {
            SkipWs();
            if (_pos >= _src.Length)
                throw new FormatException("Unexpected end of expression");

            if (Match('('))
            {
                var value = ParseExpression();
                if (!Match(')'))
                    throw new FormatException("Expected ')'");
                return value;
            }

            if (char.IsDigit(_src[_pos]))
                return ParseNumber();

            if (char.IsLetter(_src[_pos]) || _src[_pos] == '_')
                return ParseIdentifier();

            throw new FormatException($"Unexpected '{_src[_pos]}' in expression");
        }

        private int ParseNumber()
        {
            SkipWs();
            var start = _pos;
            if (_pos + 1 < _src.Length && _src[_pos] == '0' &&
                (_src[_pos + 1] == 'x' || _src[_pos + 1] == 'X'))
            {
                _pos += 2;
                start = _pos;
                while (_pos < _src.Length && Uri.IsHexDigit(_src[_pos]))
                    _pos++;
                if (_pos == start)
                    throw new FormatException("Invalid hex literal");
                return Convert.ToInt32(_src[start.._pos], 16);
            }

            while (_pos < _src.Length && char.IsDigit(_src[_pos]))
                _pos++;
            return int.Parse(_src[start.._pos]);
        }

        private int ParseIdentifier()
        {
            SkipWs();
            var start = _pos;
            while (_pos < _src.Length &&
                   (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
                _pos++;

            var name = _src[start.._pos];
            if (!_vars.TryGetValue(name, out var value))
                throw new FormatException($"Unknown option '{name}' in expression");
            return value;
        }

        private bool Match(char c)
        {
            SkipWs();
            if (_pos < _src.Length && _src[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        private bool Match(string s)
        {
            SkipWs();
            if (_pos + s.Length <= _src.Length &&
                string.Compare(_src, _pos, s, 0, s.Length, StringComparison.Ordinal) == 0)
            {
                _pos += s.Length;
                return true;
            }
            return false;
        }

        private void SkipWs()
        {
            while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos]))
                _pos++;
        }
    }
}
