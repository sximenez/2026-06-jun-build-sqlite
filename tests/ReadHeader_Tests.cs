using sqlite_inspector.Format;

namespace sqlite_inspector.Tests;

public class HeaderParserTests
{
    [Fact]
    public void Parse_FileNotFound_ReturnsFail()
    {
        // Act
        Result<DbHeader> result = new HeaderParser().Parse("nonexistent.db");

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parse_InvalidMagic_ReturnsFail()
    {
        // Arrange
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, new byte[100]);

        // Act
        Result<DbHeader> result = new HeaderParser().Parse(path);

        // Assert
        Assert.False(result.IsSuccess);
        File.Delete(path);
    }

    [Fact]
    public void Parse_ValidHeader_ReturnsPageSizeAndEncoding()
    {
        // Arrange
        byte[] header = new byte[100];
        System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(header, 0);
        header[16] = 0x10; header[17] = 0x00; // page size = 4096
        header[56] = 0x00; header[57] = 0x00; header[58] = 0x00; header[59] = 0x01; // UTF-8

        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, header);

        // Act
        Result<DbHeader> result = new HeaderParser().Parse(path);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal((ushort)4096, result.Value.PageSize);
        Assert.Equal("UTF-8", result.Value.Encoding);
        File.Delete(path);
    }
}