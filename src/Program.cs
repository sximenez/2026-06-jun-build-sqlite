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
    Result<DbHeader> headerResult = new HeaderParser().Parse(filePath);
    if (!headerResult.IsSuccess)
    {
        Console.Error.WriteLine(headerResult.Error);
        return 1;
    }

    DbHeader h = headerResult.Value;
    Console.WriteLine($"Page size: {h.PageSize} | Pages: {h.PageCount} | Encoding: {h.Encoding}");

    if (args.Length >= 4 && args[2] == "--page")
    {
        if (!int.TryParse(args[3], out int parsedPage) || parsedPage < 1)
        {
            Console.Error.WriteLine("Invalid page number. Must be a positive integer.");
            return 1;
        }

        byte[] pageBytes = new PageReader().ReadPage(filePath, (uint)parsedPage, h);
        Result<PageHeader> pageResult = new PageHeaderParser().Parse(pageBytes, parsedPage == 1);
        if (!pageResult.IsSuccess)
        {
            Console.Error.WriteLine(pageResult.Error);
            return 1;
        }

        PageHeader p = pageResult.Value;
        Console.WriteLine($"Page type: 0x{p.PageType:X2} | Cells: {p.CellCount} | Content offset: {p.CellContentOffset}");
    }
}

return 0;