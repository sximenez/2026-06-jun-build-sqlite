namespace sqlite_inspector.Format;

readonly record struct DbHeader(ushort PageSize, uint PageCount, string Encoding);
