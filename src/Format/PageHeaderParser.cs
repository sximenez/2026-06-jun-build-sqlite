using sqlite_inspector.IO;

namespace sqlite_inspector.Format;

class PageHeaderParser
{
	private const byte LeafTablePage	 = 0x0D;   
	private const byte LeafIndexPage	 = 0x0A;
	private const byte InteriorTablePage = 0x05;	
	private const byte InteriorIndexPage = 0x02; 

	public Result<PageHeader> Parse(byte[] pageBytes, bool isFirstPage)
	{
		int offset = isFirstPage ? 100 : 0;

		byte pageType = pageBytes[offset];
		if (pageType != LeafTablePage     &&
            pageType != InteriorTablePage &&
            pageType != LeafIndexPage     &&
            pageType != InteriorIndexPage)
        {
            return Result<PageHeader>.Fail($"Unknown page type: 0x{pageType:X2}");
        }

        ushort cellCount          = BinaryHelpers.ReadBigEndianUInt16(pageBytes, offset + 3);
        ushort cellContentOffset  = BinaryHelpers.ReadBigEndianUInt16(pageBytes, offset + 5);

        return Result<PageHeader>.Ok(new PageHeader(pageType, cellCount, cellContentOffset));
	}
}
