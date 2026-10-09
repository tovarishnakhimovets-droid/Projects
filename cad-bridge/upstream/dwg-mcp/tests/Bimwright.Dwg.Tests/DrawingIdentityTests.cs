using Bimwright.Dwg.Plugin.Drawing;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DrawingIdentityTests
    {
        private const string Fingerprint = "66355b08-d5a0-4664-8cd9-9d64377bb934";

        [Theory]
        [InlineData("acad.dwt", false)]
        [InlineData("C:\\Templates\\acad.dwt", true)]
        [InlineData("Чертеж1.dwg", false)]
        [InlineData("C:\\Projects\\draft.dwg", false)]
        [InlineData("C:draft.dwg", true)]
        [InlineData("\\draft.dwg", true)]
        public void Template_or_untitled_drawing_has_no_saved_identity(string name, bool titled)
            => Assert.Null(DrawingIdentityPolicy.SavedDocumentPath(name, titled));

        [Fact]
        public void Full_path_and_fingerprint_both_must_match()
        {
            const string path = "C:\\Projects\\НВ.dwg";
            Assert.Null(DrawingIdentityPolicy.Validate(path, Fingerprint, path.ToUpperInvariant(), Fingerprint));
            Assert.Contains("document", DrawingIdentityPolicy.Validate(path, Fingerprint, "C:\\Other\\НВ.dwg", Fingerprint));
            Assert.Contains("fingerprint", DrawingIdentityPolicy.Validate(path, Fingerprint, path, "28e07857-3754-4b9a-bd9c-273911f83da2"));
            Assert.Contains("saved", DrawingIdentityPolicy.Validate(null, Fingerprint, path, Fingerprint));
            Assert.Contains("GUID", DrawingIdentityPolicy.Validate(path, Fingerprint, path, "not-a-guid"));
        }

        [Fact]
        public void Network_share_paths_accept_both_separator_styles()
        {
            var windows = DrawingIdentityPolicy.NormalizeDwgPath("\\\\server\\share\\НВ.dwg");
            var forward = DrawingIdentityPolicy.NormalizeDwgPath("//server/share/НВ.dwg");
            Assert.NotNull(windows);
            Assert.Equal(windows, forward);
            Assert.Null(DrawingIdentityPolicy.Validate(windows, Fingerprint, "//server/share/НВ.dwg", Fingerprint));
        }

        [Fact]
        public void Save_refuses_untitled_readonly_or_another_open_document()
        {
            const string path = "C:\\Projects\\НВ.dwg";
            Assert.Contains("confirm", DrawingSavePolicy.Preflight(path, false, null, false, null));
            Assert.Contains("not been saved", DrawingSavePolicy.Preflight(null, false, null, true, null));
            Assert.Contains("read-only", DrawingSavePolicy.Preflight(path, true, null, true, null));
            Assert.Contains("another", DrawingSavePolicy.Preflight(path, false, "C:\\Projects\\v7.dwg", true,
                new[] { "C:\\Projects\\V7.dwg" }));
            Assert.Contains("another", DrawingSavePolicy.Preflight(path, false, null, true,
                new[] { "C:\\Projects\\НВ.dwg" }));
            Assert.Null(DrawingSavePolicy.Preflight(path, false, null, true, null));
            Assert.Null(DrawingSavePolicy.Preflight(null, false, path, false, null));
            Assert.Null(DrawingSavePolicy.Preflight(path, true, "C:\\Projects\\copy.dwg", false, null));
        }
    }
}
