using sqlite_inspector.Format;

namespace sqlite_inspector.IO;

class PageReader
{
    public byte[] ReadPage(string filePath, uint pageNumber, DbHeader header)
    {
        using BinaryReader reader = new BinaryReader(File.OpenRead(filePath));
        long offset = (pageNumber - 1) * header.PageSize;
        reader.BaseStream.Seek(offset, SeekOrigin.Begin);
        return reader.ReadBytes(header.PageSize);
    }
}
