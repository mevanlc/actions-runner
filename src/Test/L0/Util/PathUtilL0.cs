using GitHub.Runner.Sdk;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace GitHub.Runner.Common.Tests.Util
{
    public sealed class PathUtilL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCanonicalPath_ReturnsPath_WhenDirectoryDoesNotExist()
        {
            var fakePath = Path.Combine(Path.GetTempPath(), "nonexistent_" + Path.GetRandomFileName());
            var result = PathUtil.GetCanonicalPath(fakePath);
            Assert.Equal(fakePath, result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCanonicalPath_ReturnsPath_WhenNull()
        {
            Assert.Null(PathUtil.GetCanonicalPath(null));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCanonicalPath_ReturnsEmpty_WhenEmpty()
        {
            Assert.Equal(string.Empty, PathUtil.GetCanonicalPath(string.Empty));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCanonicalPath_ReturnsValidPath_ForExistingDirectory()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "pathutil_test_" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(tempDir);
                var result = PathUtil.GetCanonicalPath(tempDir);
                Assert.NotNull(result);
                Assert.True(Directory.Exists(result));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir);
                }
            }
        }

#if OS_WINDOWS
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCanonicalPath_NormalizesDriveLetter_OnWindows()
        {
            // The temp directory should always have an uppercase drive letter
            // when resolved through GetFinalPathNameByHandle
            var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

            // Force lowercase drive letter
            var lowerCased = char.ToLower(tempDir[0]) + tempDir.Substring(1);

            var result = PathUtil.GetCanonicalPath(lowerCased);

            // The canonical path should have an uppercase drive letter
            Assert.True(char.IsUpper(result[0]),
                $"Expected uppercase drive letter but got: {result}");
        }
#endif
    }
}
