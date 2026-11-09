using KulyCopy.Core;
using Xunit;

namespace KulyCopy.Core.Tests;

public class DataUtilTests
{
    [Fact]
    public void Hash_prefers_text_and_ignores_other_formats()
    {
        var a = new Dictionary<string, byte[]> { [Mime.Text] = DataUtil.Bytes("same") };
        var b = new Dictionary<string, byte[]>
        {
            [Mime.Text] = DataUtil.Bytes("same"),
            [Mime.Html] = DataUtil.Bytes("<b>same</b>"),
            [Mime.WindowTitle] = DataUtil.Bytes("Notepad"),
        };
        Assert.Equal(DataUtil.ComputeHash(a), DataUtil.ComputeHash(b));
        Assert.StartsWith("t:", DataUtil.ComputeHash(a));
    }

    [Fact]
    public void Hash_distinguishes_images_and_text()
    {
        var text = new Dictionary<string, byte[]> { [Mime.Text] = new byte[] { 1, 2, 3 } };
        var image = new Dictionary<string, byte[]> { [Mime.Png] = new byte[] { 1, 2, 3 } };
        Assert.NotEqual(DataUtil.ComputeHash(text), DataUtil.ComputeHash(image));
        Assert.StartsWith("i:", DataUtil.ComputeHash(image));
    }

    [Fact]
    public void Hash_of_unknown_formats_is_order_independent()
    {
        var a = new Dictionary<string, byte[]> { ["x/a"] = new byte[] { 1 }, ["x/b"] = new byte[] { 2 } };
        var b = new Dictionary<string, byte[]> { ["x/b"] = new byte[] { 2 }, ["x/a"] = new byte[] { 1 } };
        Assert.Equal(DataUtil.ComputeHash(a), DataUtil.ComputeHash(b));
    }

    [Fact]
    public void UriList_roundtrips_paths_with_spaces()
    {
        var paths = new[] { @"C:\Some Folder\file one.txt", @"D:\x\y.png" };
        var list = DataUtil.PathsToUriList(paths);
        Assert.Contains("file:///C:/Some%20Folder/file%20one.txt", list);
        Assert.Equal(paths, DataUtil.UriListToPaths(list).ToArray());
    }

    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("text/html", true)]
    [InlineData("application/json", true)]
    [InlineData("image/png", false)]
    [InlineData("application/octet-stream", false)]
    public void IsTextMime_classifies(string mime, bool expected)
    {
        Assert.Equal(expected, DataUtil.IsTextMime(mime));
    }

    [Fact]
    public void Clone_is_deep()
    {
        var original = DataUtil.FromText("abc");
        var clone = DataUtil.Clone(original);
        clone[Mime.Text][0] = (byte)'z';
        Assert.Equal("abc", DataUtil.GetText(original));
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(3 * 1024 * 1024, "3 MB")]
    public void FormatSize_is_human_readable(long bytes, string expected)
    {
        Assert.Equal(expected, DataUtil.FormatSize(bytes));
    }
}
