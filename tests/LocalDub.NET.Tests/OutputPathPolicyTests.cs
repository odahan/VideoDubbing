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
}
