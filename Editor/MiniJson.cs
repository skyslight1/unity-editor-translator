using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EditorTranslator
{
    // Minimal dependency-free JSON parser/serializer.
    // Values map to: Dictionary<string,object> / List<object> / string / double / bool / null.
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var p = new Parser(json);
            var v = p.ParseValue();
            p.SkipWs();
            return v;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder(256);
            Write(sb, value);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string s) { WriteString(sb, s); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (value is double d) { sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (value is float f) { sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (value is int i) { sb.Append(i.ToString(CultureInfo.InvariantCulture)); return; }
            if (value is long l) { sb.Append(l.ToString(CultureInfo.InvariantCulture)); return; }
            if (value is IDictionary dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Write(sb, e.Value);
                }
                sb.Append('}');
                return;
            }
            if (value is IEnumerable enu)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in enu)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, value.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; }

            public void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

            char Peek() { SkipWs(); return _i < _s.Length ? _s[_i] : '\0'; }

            void Expect(char c)
            {
                if (Peek() != c) throw new FormatException("JSON: expected '" + c + "' at " + _i);
                _i++;
            }

            public object ParseValue()
            {
                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': _i += 4; return true;
                    case 'f': _i += 5; return false;
                    case 'n': _i += 4; return null;
                    default: return ParseNumber();
                }
            }

            object ParseObject()
            {
                var o = new Dictionary<string, object>();
                Expect('{');
                if (Peek() == '}') { _i++; return o; }
                while (true)
                {
                    SkipWs();
                    string key = ParseString();
                    Expect(':');
                    o[key] = ParseValue();
                    char c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == '}') { _i++; break; }
                    throw new FormatException("JSON: expected ',' or '}' at " + _i);
                }
                return o;
            }

            object ParseArray()
            {
                var a = new List<object>();
                Expect('[');
                if (Peek() == ']') { _i++; return a; }
                while (true)
                {
                    a.Add(ParseValue());
                    char c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == ']') { _i++; break; }
                    throw new FormatException("JSON: expected ',' or ']' at " + _i);
                }
                return a;
            }

            string ParseString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        char e = _s[_i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u':
                                sb.Append((char)Convert.ToInt32(_s.Substring(_i, 4), 16));
                                _i += 4;
                                break;
                            default: throw new FormatException("JSON: bad escape \\" + e);
                        }
                    }
                    else sb.Append(c);
                }
                throw new FormatException("JSON: unterminated string");
            }

            object ParseNumber()
            {
                int start = _i;
                if (Peek() == '-') _i++;
                while (_i < _s.Length && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                string tok = _s.Substring(start, _i - start);
                if (tok.Length == 0) throw new FormatException("JSON: bad number at " + start);
                return double.Parse(tok, CultureInfo.InvariantCulture);
            }
        }
    }
}
