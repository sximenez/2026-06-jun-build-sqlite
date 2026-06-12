namespace sqlite_inspector.Format;

readonly record struct PageHeader(byte PageType, ushort CellCount, ushort CellContentOffset);
