using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class LispTrustedSourceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "dwg-lisp-policy-" + Guid.NewGuid().ToString("N"));

        public LispTrustedSourceTests() => Directory.CreateDirectory(directory);

        public void Dispose()
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }

        [Fact]
        public void Capture_hashes_original_bytes_and_keeps_the_exact_source_after_file_changes()
        {
            const string original = "; comment\r\n(defun c:ТЕСТ () \"first\")\r\n";
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(original)).ToArray();
            var file = Write(bytes);
            var snapshot = LispExecutionPolicy.Capture(file);
            using var sha = SHA256.Create();
            Assert.Equal(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""), snapshot.Sha256);
            Assert.Equal(Path.GetFullPath(file), snapshot.File);
            Assert.Equal(original, snapshot.Source);

            File.WriteAllText(file, "(startapp \"changed-on-disk\")", new UTF8Encoding(false));
            var expression = LispExecutionPolicy.BuildExpression(snapshot, "c:ТЕСТ", null, "DWGCOMPLETE", "nonce");
            Assert.Contains(LispExecutionPolicy.Quote("(progn\n" + original + "\n)"), expression);
            Assert.DoesNotContain("changed-on-disk", expression);
            Assert.NotEqual(snapshot.Sha256, LispExecutionPolicy.Capture(file).Sha256);
        }

        [Theory]
        [InlineData("utf8")]
        [InlineData("utf8-bom")]
        [InlineData("utf16-le")]
        [InlineData("utf16-be")]
        public void Capture_accepts_strict_supported_encodings(string format)
        {
            const string source = "(defun привет () \"Текст 😀\")\r\n";
            Encoding encoding = format == "utf16-le" ? new UnicodeEncoding(false, true, true)
                : format == "utf16-be" ? new UnicodeEncoding(true, true, true)
                : new UTF8Encoding(format == "utf8-bom", true);
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray();
            Assert.Equal(source, LispExecutionPolicy.Capture(Write(bytes)).Source);
        }

        [Theory]
        [InlineData(new byte[] { 0xC3, 0x28 })]
        [InlineData(new byte[] { 0xFF, 0xFE, 0x41 })]
        [InlineData(new byte[] { 0xFE, 0xFF, 0xD8, 0x00 })]
        [InlineData(new byte[] { 0x41, 0x00 })]
        [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x00 })]
        public void Capture_rejects_malformed_encodings_or_nul(byte[] bytes)
        {
            Assert.Throws<ArgumentException>(() => LispExecutionPolicy.Capture(Write(bytes)));
        }

        [Fact]
        public void Capture_enforces_the_byte_limit_before_decoding()
        {
            var bytes = Enumerable.Repeat((byte)' ', LispExecutionPolicy.MaxSourceBytes).ToArray();
            Assert.Equal(bytes.Length, LispExecutionPolicy.Capture(Write(bytes)).Source.Length);
            Assert.Throws<ArgumentException>(() => LispExecutionPolicy.Capture(Write(bytes.Concat(new byte[] { 32 }).ToArray())));
        }

        [Theory]
        [InlineData("relative.lsp")]
        [InlineData(@"C:relative.lsp")]
        [InlineData(@"\root-relative.lsp")]
        [InlineData(@"\\server\share\test.lsp")]
        [InlineData(@"\\?\C:\test.lsp")]
        [InlineData(@"C:\test.lsp:payload")]
        [InlineData(@"C:\test.lsp:payload.lsp")]
        [InlineData(@"C:\test.fas")]
        [InlineData(@"Ж:\test.lsp")]
        [InlineData("https://example.com/test.lsp")]
        public void Capture_rejects_unsupported_paths_without_reading_them(string file)
        {
            Assert.Throws<ArgumentException>(() => LispExecutionPolicy.Capture(file));
        }

        [Fact]
        public void Missing_file_errors_do_not_disclose_the_file_path()
        {
            var path = Path.Combine(directory, "private-password=probe.lsp");
            var error = Assert.Throws<InvalidOperationException>(() => LispExecutionPolicy.Capture(path));
            Assert.DoesNotContain(directory, error.Message);
            Assert.DoesNotContain("probe", error.Message);
        }

        [Theory]
        [InlineData("c:ТЕСТ-1")]
        [InlineData("_helper")]
        [InlineData("PROJECT:entry")]
        public void Entry_names_are_single_symbols(string command)
            => Assert.Null(LispExecutionPolicy.ValidateCommand(command));

        [Theory]
        [InlineData("")]
        [InlineData("1entry")]
        [InlineData("(entry)")]
        [InlineData("entry other")]
        [InlineData("entry\n(startapp \"x\")")]
        [InlineData("entry;comment")]
        [InlineData("entry\"quoted")]
        public void Entry_names_reject_forms_and_command_injection(string command)
            => Assert.NotNull(LispExecutionPolicy.ValidateCommand(command));

        [Fact]
        public void Hash_validation_requires_exact_hex_length()
        {
            Assert.True(LispExecutionPolicy.IsSha256(new string('A', 64)));
            Assert.True(LispExecutionPolicy.IsSha256(new string('f', 64)));
            Assert.False(LispExecutionPolicy.IsSha256(new string('a', 63)));
            Assert.False(LispExecutionPolicy.IsSha256(new string('a', 65)));
            Assert.False(LispExecutionPolicy.IsSha256(new string('g', 64)));
            Assert.False(LispExecutionPolicy.IsSha256(null));
        }

        [Fact]
        public void Quotes_preserve_text_and_escape_controls_for_autolisp()
        {
            Assert.Equal("\"a\\\"b\\\\c\\r\\n\\t\\001\\177\\200 Текст 😀\"",
                LispExecutionPolicy.Quote("a\"b\\c\r\n\t\u0001\u007f\u0080 Текст 😀"));
            Assert.Throws<ArgumentException>(() => LispExecutionPolicy.Quote("before\0after"));
            Assert.Throws<ArgumentException>(() => LispExecutionPolicy.Quote("\uD800"));
            Assert.Throws<ArgumentException>(() => LispExecutionPolicy.Quote("\uDC00"));
        }

        [Fact]
        public void Arguments_remain_quoted_data_including_injection_text_nested_lists_and_dates()
        {
            const string injection = "\") (startapp \"should-never-run\") (\"";
            var arguments = new JArray(injection, true, false, JValue.CreateNull(), 3,
                new JArray("2026-10-09", new JArray("(eval code)", 2.5)));
            var expression = Build(arguments.ToString(Formatting.None));
            Assert.Contains("(apply (read \"c:ENTRY\") '(" + LispExecutionPolicy.Quote(injection)
                + " T nil nil 3 (\"2026-10-09\" (\"(eval code)\" 2.5))))", expression);
            Assert.Contains("(DWGCOMPLETE \"nonce\" (if (vl-catch-all-error-p _dwg_mcp_result) 0 1)", expression);
            Assert.Contains("(substr _dwg_mcp_text 1 8192)", expression);
        }

        [Fact]
        public void Numeric_literals_use_invariant_culture_and_large_integers_become_reals()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                var expression = Build("[1.25,2147483648,-2147483649,1e20]");
                Assert.Contains("'(1.25 2147483648.0 -2147483649.0 1E+20)", expression);
                Assert.DoesNotContain("1,25", expression);
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("[{}]")]
        [InlineData("[undefined]")]
        [InlineData("[NaN]")]
        [InlineData("[Infinity]")]
        [InlineData("[-Infinity]")]
        [InlineData("[1e400]")]
        [InlineData("[\"\\u0000\"]")]
        [InlineData("[1] [2]")]
        [InlineData("[1,")]
        public void Arguments_reject_unsupported_or_malformed_data(string arguments)
            => Assert.Throws<ArgumentException>(() => Build(arguments));

        [Fact]
        public void Arguments_enforce_total_nodes_depth_and_text_bounds()
        {
            var oneHundred = "[" + string.Join(",", Enumerable.Repeat("null", 100)) + "]";
            Assert.NotEmpty(Build(oneHundred));
            Assert.Throws<ArgumentException>(() => Build("[" + oneHundred + "]"));
            Assert.NotEmpty(Build(new string('[', 8) + "null" + new string(']', 8)));
            Assert.Throws<ArgumentException>(() => Build(new string('[', 9) + "null" + new string(']', 9)));
            Assert.Throws<ArgumentException>(() => Build("[\"" + new string('a', LispExecutionPolicy.MaxArgumentsJsonChars) + "\"]"));
            Assert.Throws<ArgumentException>(() => Build(new string(' ', LispExecutionPolicy.MaxArgumentsJsonChars + 1)));
        }

        [Fact]
        public void Wrapper_rejects_callback_or_nonce_injection_and_accepts_empty_arguments()
        {
            var snapshot = LispExecutionPolicy.Capture(Write(Encoding.UTF8.GetBytes("(defun c:ENTRY () nil)")));
            Assert.Throws<ArgumentException>(() => LispExecutionPolicy.BuildExpression(snapshot, "c:ENTRY", null, "CALLBACK) (evil", "nonce"));
            Assert.Throws<ArgumentException>(() => LispExecutionPolicy.BuildExpression(snapshot, "c:ENTRY", null, "DWGCOMPLETE", "nonce\0bad"));
            Assert.Contains("(apply (read \"c:ENTRY\") '())", LispExecutionPolicy.BuildExpression(snapshot, "c:ENTRY", null, "DWGCOMPLETE", "nonce"));
        }

        private string Build(string arguments)
        {
            var snapshot = LispExecutionPolicy.Capture(Write(Encoding.UTF8.GetBytes("(defun c:ENTRY () nil)")));
            return LispExecutionPolicy.BuildExpression(snapshot, "c:ENTRY", arguments, "DWGCOMPLETE", "nonce");
        }

        private string Write(byte[] bytes)
        {
            var file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".lsp");
            File.WriteAllBytes(file, bytes);
            return file;
        }
    }
}
