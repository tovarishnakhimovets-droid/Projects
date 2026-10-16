using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    public sealed class LispSourceSnapshot
    {
        public LispSourceSnapshot(string file, string sha256, string source)
        {
            File = file;
            Sha256 = sha256;
            Source = source;
        }

        public string File { get; }
        public string Sha256 { get; }
        public string Source { get; }
    }

    /// <summary>
    /// Captures exact script bytes and builds data-only arguments for a trusted
    /// native execution wrapper. Approval belongs to the plugin session; this
    /// policy and a scanner verdict do not grant trust or provide a sandbox.
    /// </summary>
    public static class LispExecutionPolicy
    {
        public const string ErrorCode = "lisp_execution_blocked";
        public const string Refusal =
            "LISP execution is blocked: the exact file hash and entry function must be trusted "
            + "with MCPTRUSTLISP in this AutoCAD session. No LISP was queued or executed. "
            + "Static analysis does not authorize execution; MCPENABLECODE does not grant LISP trust.";

        public const int MaxSourceBytes = 64 * 1024;
        public const int MaxArgumentNodes = 100;
        public const int MaxArgumentDepth = 8;
        public const int MaxArgumentsJsonChars = 64 * 1024;
        public const int MaxResultChars = 8192;

        public static LispSourceSnapshot Capture(string file)
        {
            var fullPath = NormalizePath(file);
            byte[] bytes;
            try
            {
                // One bounded read supplies both the approval hash and the
                // source that is executed. Later path changes cannot swap it.
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > MaxSourceBytes)
                        throw new ArgumentException("LISP file exceeds the 64 KiB limit.");
                    var buffer = new byte[MaxSourceBytes + 1];
                    var count = 0;
                    while (count < buffer.Length)
                    {
                        var read = stream.Read(buffer, count, buffer.Length - count);
                        if (read == 0) break;
                        count += read;
                    }
                    if (count > MaxSourceBytes)
                        throw new ArgumentException("LISP file exceeds the 64 KiB limit.");
                    bytes = new byte[count];
                    Buffer.BlockCopy(buffer, 0, bytes, 0, count);
                }
            }
            catch (IOException ex) { throw new InvalidOperationException("Cannot read LISP file.", ex); }
            catch (UnauthorizedAccessException ex) { throw new InvalidOperationException("Cannot read LISP file.", ex); }

            string source;
            try
            {
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                    source = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
                else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                    source = new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
                else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                    source = new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
                else
                    source = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new ArgumentException("LISP file must use valid UTF-8 or BOM-marked UTF-16.", ex);
            }
            if (source.IndexOf('\0') >= 0)
                throw new ArgumentException("LISP source cannot contain NUL characters.");

            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            return new LispSourceSnapshot(fullPath, hash, source);
        }

        public static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var c in value)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F'))
                    return false;
            return true;
        }

        public static string ValidateCommand(string command)
        {
            if (string.IsNullOrEmpty(command) || command.Length > 128)
                return "entry function must be a simple LISP symbol of 1–128 characters";
            if (!char.IsLetter(command[0]) && command[0] != '_')
                return "entry function must start with a letter or underscore";
            foreach (var c in command)
                if (!char.IsLetterOrDigit(c) && c != ':' && c != '_' && c != '-')
                    return "entry function may contain only letters, digits, colon, underscore and hyphen";
            return null;
        }

        public static string Quote(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var quoted = new StringBuilder(value.Length + 2).Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\0') throw new ArgumentException("LISP strings cannot contain NUL characters.");
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (char.IsControl(c))
                            quoted.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
                        else if (char.IsSurrogate(c))
                        {
                            if (!char.IsHighSurrogate(c) || i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                                throw new ArgumentException("LISP strings must contain valid Unicode.");
                            quoted.Append(c).Append(value[++i]);
                        }
                        else quoted.Append(c);
                        break;
                }
            }
            return quoted.Append('"').ToString();
        }

        public static string BuildExpression(LispSourceSnapshot snapshot, string command,
            string argumentsJson, string callbackName, string nonce)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Source == null || snapshot.Source.Length > MaxSourceBytes)
                throw new ArgumentException("a bounded captured LISP source is required");
            var commandError = ValidateCommand(command);
            if (commandError != null) throw new ArgumentException(commandError);
            var callbackError = ValidateCommand(callbackName);
            if (callbackError != null) throw new ArgumentException("completion callback must be a simple LISP symbol");
            if (string.IsNullOrEmpty(nonce) || nonce.Length > 128)
                throw new ArgumentException("completion nonce must contain 1–128 characters");

            var arguments = ReadArguments(argumentsJson);
            var nodes = 0;
            var literals = new StringBuilder("(");
            foreach (var argument in arguments)
            {
                if (literals.Length > 1) literals.Append(' ');
                literals.Append(DataLiteral(argument, 1, ref nodes));
            }
            literals.Append(')');

            // Source and function name are escaped strings. Arguments are a
            // quoted data list, so JSON strings and nested lists cannot become
            // forms to evaluate. read is inside the catch, including syntax errors.
            return "(apply '(lambda (_dwg_mcp_result _dwg_mcp_text)\n"
                + " (setq _dwg_mcp_result (vl-catch-all-apply '(lambda ()\n"
                + "  (eval (read " + Quote("(progn\n" + snapshot.Source + "\n)") + "))\n"
                + "  (apply (read " + Quote(command) + ") '" + literals + ")) nil))\n"
                + " (setq _dwg_mcp_text (if (vl-catch-all-error-p _dwg_mcp_result)\n"
                + "  (vl-catch-all-error-message _dwg_mcp_result) (vl-princ-to-string _dwg_mcp_result)))\n"
                + " (" + callbackName + " " + Quote(nonce)
                + " (if (vl-catch-all-error-p _dwg_mcp_result) 0 1)"
                + " (substr _dwg_mcp_text 1 " + MaxResultChars.ToString(CultureInfo.InvariantCulture) + "))\n"
                + " (princ)) '(nil nil))";
        }

        private static string NormalizePath(string file)
        {
            const string error = "file must be a fully qualified local .lsp path without alternate data streams";
            if (string.IsNullOrWhiteSpace(file) || file.Length < 4
                || !((file[0] >= 'A' && file[0] <= 'Z') || (file[0] >= 'a' && file[0] <= 'z'))
                || file[1] != ':' || (file[2] != '\\' && file[2] != '/')
                || file.IndexOf(':', 2) >= 0 || file.IndexOf('\0') >= 0)
                throw new ArgumentException(error);
            try
            {
                var fullPath = Path.GetFullPath(file);
                if (!string.Equals(Path.GetExtension(fullPath), ".lsp", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(error);
                return fullPath;
            }
            catch (ArgumentException) { throw new ArgumentException(error); }
            catch (NotSupportedException) { throw new ArgumentException(error); }
            catch (PathTooLongException) { throw new ArgumentException(error); }
        }

        private static JArray ReadArguments(string argumentsJson)
        {
            if (string.IsNullOrEmpty(argumentsJson)) return new JArray();
            if (argumentsJson.Length > MaxArgumentsJsonChars)
                throw new ArgumentException("arguments_json exceeds the 65536-character limit");
            if (string.IsNullOrWhiteSpace(argumentsJson)) return new JArray();
            try
            {
                using (var text = new StringReader(argumentsJson))
                using (var reader = new JsonTextReader(text)
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = MaxArgumentDepth + 1
                })
                {
                    var token = JToken.ReadFrom(reader);
                    if (!(token is JArray array)) throw new ArgumentException("arguments_json must be a JSON array");
                    while (reader.Read())
                        if (reader.TokenType != JsonToken.Comment)
                            throw new ArgumentException("arguments_json must contain one JSON array");
                    return array;
                }
            }
            catch (JsonException ex) { throw new ArgumentException("arguments_json must be a valid bounded JSON array", ex); }
        }

        private static string DataLiteral(JToken token, int depth, ref int nodes)
        {
            if (++nodes > MaxArgumentNodes)
                throw new ArgumentException("arguments_json exceeds the 100-node limit");
            if (depth > MaxArgumentDepth)
                throw new ArgumentException("arguments_json exceeds the eight-level depth limit");
            switch (token.Type)
            {
                case JTokenType.Null: return "nil";
                case JTokenType.Boolean: return token.Value<bool>() ? "T" : "nil";
                case JTokenType.String: return Quote(token.Value<string>());
                case JTokenType.Integer:
                case JTokenType.Float:
                    var number = token.ToString(Formatting.None);
                    if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var finite)
                        || double.IsNaN(finite) || double.IsInfinity(finite))
                        throw new ArgumentException("arguments_json numbers must be finite");
                    if (token.Type == JTokenType.Integer && !int.TryParse(number, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out _)) number += ".0";
                    return number;
                case JTokenType.Array:
                    var list = new StringBuilder("(");
                    foreach (var child in (JArray)token)
                    {
                        if (list.Length > 1) list.Append(' ');
                        list.Append(DataLiteral(child, depth + 1, ref nodes));
                    }
                    return list.Append(')').ToString();
                default:
                    throw new ArgumentException("arguments_json permits only strings, finite numbers, booleans, null and arrays");
            }
        }
    }
}
