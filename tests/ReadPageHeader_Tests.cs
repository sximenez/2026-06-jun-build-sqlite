using sqlite_inspector.Format;

namespace sqlite_inspector.Tests;

public class PageHeaderParserTests
{
    private static byte[] BuildPageBytes(byte pageType, ushort cellCount, bool isFirstPage)
    {
        byte[] page = new byte[4096];
        int offset = isFirstPage ? 100 : 0;
        page[offset]     = pageType;
        page[offset + 3] = (byte)(cellCount >> 8);
        page[offset + 4] = (byte)(cellCount & 0xFF);
        return page;
    }

    [Fact]
    public void Parse_UnknownPageType_ReturnsFail()
    {
        byte[] page = BuildPageBytes(0xFF, 0, false);
        Result<PageHeader> result = new PageHeaderParser().Parse(page, false);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parse_LeafTablePage_ReturnsCellCount()
    {
        byte[] page = BuildPageBytes(0x0D, 4, false);
        Result<PageHeader> result = new PageHeaderParser().Parse(page, false);
        Assert.True(result.IsSuccess);
        Assert.Equal(0x0D, result.Value.PageType);
        Assert.Equal((ushort)4, result.Value.CellCount);
    }

    [Fact]
    public void Parse_FirstPage_ReadsFromOffset100()
    {
        byte[] page = BuildPageBytes(0x0D, 7, true);
        Result<PageHeader> result = new PageHeaderParser().Parse(page, true);
        Assert.True(result.IsSuccess);
        Assert.Equal((ushort)7, result.Value.CellCount);
    }
}
