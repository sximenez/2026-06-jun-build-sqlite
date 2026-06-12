using sqlite_inspector;
using sqlite_inspector.Format;
using sqlite_inspector.IO;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: sqlite-inspector describe <file.db> [--page N]");
    return 1;
}

string command = args[0];
string filePath = args[1];

if (command == "describe")
{
    Result<DbHeader> result = new HeaderParser().Parse(filePath);
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine(result.Error);
        return 1;
    }
    DbHeader h = result.Value;
    Console.WriteLine($"Page size: {h.PageSize} | Pages: {h.PageCount} | Encoding: {h.Encoding}");

    uint pageNumber = 1;
    if (args.Length >= 4 && args[2] == "--page" && uint.TryParse(args[3], out uint parsedPage))
    {
        pageNumber = parsedPage;
    }

    byte[] pageBytes = new PageReader().ReadPage(filePath, pageNumber, h);
    Result<PageHeader> pageResult = new PageHeaderParser().Parse(pageBytes, pageNumber == 1);

    if (!pageResult.IsSuccess)
    {
        Console.Error.WriteLine(pageResult.Error);
        return 1;
    }

    PageHeader p = pageResult.Value;
    Console.WriteLine($"Page type: 0x{p.PageType:X2} | Cells: {p.CellCount} | Content offset: {p.CellContentOffset}");
}

return 0;
