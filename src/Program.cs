using sqlite_inspector;
using sqlite_inspector.Format;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: sqlite-inspector describe <file.db>");
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
}

return 0;