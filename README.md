# sqlite-inspector
 
A read-only C# CLI that parses SQLite `.db` files natively — no SQLite library, no abstraction.
 
Built stage by stage to understand how SQLite works: file format, B-trees, varints, indexes.
 
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
│   │   ├── DbHeader.cs
│   │   ├── HeaderParser.cs
│   │   ├── PageHeader.cs
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

## Introduction

SQLite `.db` files are written in binary, i.e. machine language.

Hex is the human-readable representation of binary.

Ascii is the text representation of Hex.

```powershell
# To inspect a binary .db file
Format-Hex .\test.db | Select-Object -First 4
```

```
       Offset Bytes                                           Ascii
              00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F
       ------ ----------------------------------------------- -----
000000000000  53 51 4C 69 74 65 20 66 6F 72 6D 61 74 20 33 00 SQLite format 3
000000000010  10 00 01 01 00 40 20 20 00 00 00 07 00 00 00 03 ..@     .   .
000000000020  00 00 00 00 00 00 00 00 00 00 00 02 00 00 00 04          .   .
000000000030  00 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00          .
```

Three columns:

**Offset** — byte position from the start of the file. Each line is 16 bytes.

**Bytes** — the raw hex values. Each pair is one byte. `53` = the letter `S`, `10 00` = page size 4096 in big-endian.

**Ascii** — the printeable interpretation of those same bytes. Non-printeable bytes (numbers, flags) show as `·` or `?`.

The first row is immediately readable: `SQLite format 3` — that is the magic string, bytes 0–15.

Row two starts at offset `0x10` (byte 16): `10 00` — the page size, big-endian for `0x1000` = 4096.

Every SQLite file starts with a 100-byte header containing metadata about itself.

It ends at offset `0x64`, everything after that reserved for structured binary data.

```
0-100: File header
100-4096: Page 1 (root B-tree page)
4096-8192: Page 2 (B-tree page)
8192-12288: Page 3 (B-tree page)
```

### Seed Data

Using the sqlite3 CLI, we create a small database to work with, containing some sample data:

```sql
sqlite3 ~/test.db "
CREATE TABLE patients (id INTEGER PRIMARY KEY, name TEXT, dob TEXT, gender TEXT);
CREATE TABLE appointments (id INTEGER PRIMARY KEY, patient_id INTEGER, date TEXT, notes TEXT);
INSERT INTO patients VALUES (1, 'Dupont', '1982-04-12', 'M');
INSERT INTO patients VALUES (2, 'Martin', '1975-11-03', 'F');
INSERT INTO patients VALUES (3, 'Bernard', '1990-07-22', 'M');
INSERT INTO appointments VALUES (1, 1, '2024-01-15', 'Routine checkup');
INSERT INTO appointments VALUES (2, 2, '2024-01-16', 'Follow-up');
"
```

```
sqlite3 ~/test.db "PRAGMA page_size; PRAGMA page_count; SELECT * FROM patients;"
```

```
╭───────────╮
│ page_size │
╞═══════════╡
│      4096 │
╰───────────╯
╭────────────╮
│ page_count │
╞════════════╡
│          3 │
╰────────────╯
╭────┬─────────┬────────────┬────────╮
│ id │  name   │    dob     │ gender │
╞════╪═════════╪════════════╪════════╡
│  1 │ Dupont  │ 1982-04-12 │ M      │
│  2 │ Martin  │ 1975-11-03 │ F      │
│  3 │ Bernard │ 1990-07-22 │ M      │
╰────┴─────────┴────────────┴────────╯
```
 
## Stages
 
---
 
### Stage 1 — File Header
 
#### Theory

The SQLite file header is 100 bytes long and contains metadata about the database file.
 
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

---

### Stage 2 — Page Header

#### Theory

SQLite divides the file into fixed-size pages — every read and write operates on one page at a time.

Page 1 is always the **root B-tree page**: it holds `sqlite_schema`, the table that describes all other tables.

A B-tree is a tree data structure that keeps data sorted and allows searches, sequential access, insertions, and deletions in logarithmic time.

Example of B-tree structure:

```
        [ 30 ]
       /      \
   [10, 20]  [40, 50]
```

Every B-tree page starts with a page header that tells you its type and how many cells it contains.

| Offset | Size | Meaning |
|--------|------|---------|
| 0 | 1 | Page type |
| 3 | 2 | Cell count (big-endian) |
| 5 | 2 | Cell content offset (big-endian) |

Example in B-tree page header:

```
[ 0x0D ] [ 0x00 0x02 ] [ 0x00 0x28 ]
```

##### Page types

| Code | Meaning |
|------|---------|
| `0x0D` | Leaf table page — holds actual row data |
| `0x05` | Interior table page — holds child page pointers |
| `0x0A` | Leaf index page |
| `0x02` | Interior index page |

##### Why does page 1 start at byte 100?

The 100-byte database header occupies the start of page 1.

So the page header for page 1 starts at byte 100, not byte 0.

**All other pages start at byte 0 of their page.**

`PageReader` always seeks to the page start — `PageHeaderParser` handles the offset. Single responsibility.

#### Practice

In this stage, we implement `PageReader` to seek to any page by number, and `PageHeaderParser` to parse the page header bytes.

`PageReader` computes the seek offset as `(pageNumber - 1) * pageSize` — page numbers are 1-based in SQLite.

`PageHeaderParser` takes a `bool isFirstPage` flag to apply the 100-byte offset when needed.

We return a `PageHeader` record with the relevant information.

```csharp
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
```

```csharp
class PageHeaderParser
{
    private const byte LeafTablePage     = 0x0D;
    private const byte InteriorTablePage = 0x05;
    private const byte LeafIndexPage     = 0x0A;
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

        ushort cellCount         = BinaryHelpers.ReadBigEndianUInt16(pageBytes, offset + 3);
        ushort cellContentOffset = BinaryHelpers.ReadBigEndianUInt16(pageBytes, offset + 5);

        return Result<PageHeader>.Ok(new PageHeader(pageType, cellCount, cellContentOffset));
    }
}
```

```csharp
// Program.cs

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
```

```terminal
C:\Workspaces\2026-06-jun-build-sqlite [main] > dotnet run --project .\src\sqlite-inspector.csproj describe .\test.db --page 1
Page size: 4096 | Pages: 3 | Encoding: UTF-8
Page type: 0x0D | Cells: 2 | Content offset: 3852
```
