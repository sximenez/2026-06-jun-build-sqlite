# sqlite-inspector
 
A read-only C# CLI that parses SQLite `.db` files natively — no SQLite library, no abstraction.
 
Built stage by stage to understand how SQLite actually works: file format, B-trees, varints, indexes.
 
---
 
## Main commands
 
```bash
$ sqlite-inspector describe sample.db
$ sqlite-inspector query sample.db patients --where "name=Dupont"
$ sqlite-inspector stats sample.db patients
```
 
---
 
## Stack
 
- C# (.NET 8), console app
- XUnit for tests
- `BinaryReader` for all binary parsing
- No SQLite libraries

---
 
## Architecture
 
The stages are **cumulative layers**.
 
Each stage adds one class to a shared layer. 

The commands (`describe`, `query`, `stats`) are the only real vertical slices — they compose the layers at the top.
 
```
sqlite-inspector/
├── src/
│   ├── Program.cs
│   ├── Result.cs
│   ├── IO/
│   │   ├── PageReader.cs
│   │   └── BinaryHelpers.cs
│   ├── Format/
│   │   ├── HeaderParser.cs
│   │   ├── PageHeaderParser.cs
│   │   └── VarintReader.cs
│   ├── BTree/
│   │   ├── BTreeWalker.cs
│   │   └── RecordDecoder.cs
│   ├── Schema/
│   │   └── SchemaReader.cs
│   └── Query/
│       ├── QueryRequest.cs
│       ├── QueryHandler.cs
│       └── QueryResponse.cs
└── tests/
    └── sqlite-inspector.Tests/
```
 
**Vertical slices appear at the command boundary only.**
 
Everything below is shared infrastructure, built layer by layer across the stages.
 
---
 
## Stages
 
---
 
### Stage 1 — File Header
 
#### Theory
 
Every SQLite file starts with a 100-byte header.
 
The first 16 bytes are a magic string: `SQLite format 3\000`.
 
This lets any program confirm the file is a valid SQLite database before reading further.
 
The header also stores the page size — the unit of I/O that all subsequent reading is based on.
 
| Offset | Size | Meaning |
|--------|------|---------|
| 0 | 16 | Magic string |
| 16 | 2 | Page size (big-endian) |
| 28 | 4 | Page count |
| 56 | 4 | Text encoding (1=UTF-8, 2=UTF-16LE, 3=UTF-16BE) |
 
##### Why big-endian?

Many early Computer Science decisions were made by mathematicians.

In this case, big-endian encoding reflects **mathematical, human convention**:

You write `4096`, you store `0x10`, then `0x00`, and so on.

Little-endian is hardware, **machine-first**.

Engineers optimized encoding so that the machine could process incrementally (better for compute):

You write `4096`, you store `0x00`, then `0x10`, etc.

SQLite kept big-endian as main encoding because file format is optimized for comparison more than for compute.

#### Practice

In this stage, we implement `HeaderParser` to read the file header and extract page size, number of pages and encoding.

The Result pattern allows to fail fast.

The `BinaryHelpers` class provides helper methods to translate little-endian into big-endian (not default).

We return a `DbHeader` object with the relevant information.

```csharp
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
```

```csharp
// Program.cs

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
```

```terminal
C:\Workspaces\2026-06-jun-build-sqlite [main] > dotnet run --project .\src\sqlite-inspector.csproj describe .\test.db
Page size: 4096 | Pages: 3 | Encoding: UTF-8
```