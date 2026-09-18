using LocalDub.Utils;

namespace LocalDub.NET.Tests;

public sealed class OutputPathPolicyTests
{
    [Fact]
    public void AddsEnglishSuffixWithoutChangingExtension()
    {
        var result = OutputPathPolicy.GetEnglishVideoPath(Path.Combine("C:\\videos", "demo.final.mp4"));

        Assert.Equal(Path.Combine("C:\\videos", "demo.final-EN.mp4"), result);
    }

    [Fact]
    public void RejectsSourceAsOutput()
    {
        var path = Path.GetFullPath("video.mp4");
        Assert.Throws<InvalidOperationException>(() => OutputPathPolicy.EnsureWritableOutput(path, path, false));
    }

    [Fact]
    public void RejectsExistingOutputWithoutOverwrite()
    {
        var input = Path.GetFullPath("video.mp4");
        var output = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");
        File.WriteAllText(output, string.Empty);
        try
        {
            Assert.Throws<IOException>(() => OutputPathPolicy.EnsureWritableOutput(input, output, overwrite: false));
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void AllowsExistingOutputWithOverwrite()
    {
        var input = Path.GetFullPath("video.mp4");
        var output = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");
        File.WriteAllText(output, string.Empty);
        try
        {
            var exception = Record.Exception(() => OutputPathPolicy.EnsureWritableOutput(input, output, overwrite: true));
            Assert.Null(exception);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
