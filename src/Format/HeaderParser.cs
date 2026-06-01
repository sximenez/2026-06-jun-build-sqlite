using System.Text;
using sqlite_inspector.IO;

namespace sqlite_inspector.Format;

class HeaderParser
{
    private const string MagicString = "SQLite format 3\0";

    public Result<DbHeader> Parse(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return Result<DbHeader>.Fail($"File not found: {filePath}");
        }

        using BinaryReader reader = new BinaryReader(File.OpenRead(filePath));
        byte[] header = reader.ReadBytes(100);

        string magic = Encoding.ASCII.GetString(header, 0, 16);
        if (magic != MagicString)
        {
            return Result<DbHeader>.Fail($"Invalid SQLite file: {filePath}");
        }

        ushort pageSize = BinaryHelpers.ReadBigEndianUInt16(header, 16);
        uint pageCount = BinaryHelpers.ReadBigEndianUInt32(header, 28);
        uint encodingCode = BinaryHelpers.ReadBigEndianUInt32(header, 56);
        string encoding = encodingCode switch
        {
            1 => "UTF-8",
            2 => "UTF-16le",
            3 => "UTF-16be",
            _ => "Unknown"
        };

        return Result<DbHeader>.Ok(new DbHeader
        {
            PageSize = pageSize,
            PageCount = pageCount,
            Encoding = encoding
        });
    }
}
