using KulyCopy.Core;
using Xunit;

namespace KulyCopy.Core.Tests;

public class CfHtmlTests
{
    [Fact]
    public void Build_then_extract_roundtrips_unicode_fragment()
    {
        const string fragment = "<b>Olá, mundo</b> — ação, coração 🚀";
        var cfHtml = CfHtml.Build(fragment);

        Assert.True(CfHtml.LooksLikeCfHtml(cfHtml));
        Assert.Equal(fragment, CfHtml.ExtractFragment(cfHtml));
    }

    [Fact]
    public void Build_offsets_point_to_html_and_fragment()
    {
        const string fragment = "<i>x</i>";
        var cfHtml = CfHtml.Build(fragment);
        var bytes = DataUtil.Utf8.GetBytes(cfHtml);

        int Offset(string key)
        {
            var line = cfHtml.Split("\r\n").First(l => l.StartsWith(key + ":"));
            return int.Parse(line[(key.Length + 1)..]);
        }

        var html = DataUtil.Utf8.GetString(bytes, Offset("StartHTML"), Offset("EndHTML") - Offset("StartHTML"));
        Assert.StartsWith("<html>", html);
        Assert.EndsWith("</html>", html);
        Assert.Contains("<!--StartFragment-->" + fragment + "<!--EndFragment-->", html);
    }

    [Fact]
    public void Build_keeps_existing_fragment_markers()
    {
        const string withMarkers = "<html><body><!--StartFragment--><p>a</p><!--EndFragment--></body></html>";
        var cfHtml = CfHtml.Build(withMarkers);
        Assert.Equal("<p>a</p>", CfHtml.ExtractFragment(cfHtml));
    }

    [Fact]
    public void Extract_returns_input_when_not_cfhtml()
    {
        const string plain = "<p>just html</p>";
        Assert.Equal(plain, CfHtml.ExtractFragment(plain));
    }

    [Fact]
    public void Extract_falls_back_when_offsets_are_broken()
    {
        const string broken = "Version:0.9\r\nStartHTML:0000000999\r\nEndHTML:0000009999\r\nStartFragment:0000000999\r\nEndFragment:0000009999\r\n<html><body><p>x</p></body></html>";
        Assert.Equal("<html><body><p>x</p></body></html>", CfHtml.ExtractFragment(broken));
    }
}
