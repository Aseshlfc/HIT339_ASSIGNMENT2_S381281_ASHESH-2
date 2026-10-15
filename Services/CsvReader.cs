using System.Text;

namespace LibraryManagementSystem.Services;

/// <summary>One record read from a CSV file and the line it started on (1-based).</summary>
public record CsvRecord(int Line, string[] Fields);

public class CsvFormatException : Exception
{
    public int Line { get; }
    public CsvFormatException(string message, int line) : base(message) => Line = line;
}

/// <summary>
/// Small RFC 4180 reader: quoted fields, doubled quotes ("") inside them, commas and
/// line breaks inside quotes, and CRLF, LF or CR line endings. Lines with nothing on
/// them are skipped. A leading byte order mark is ignored (Excel adds one).
/// </summary>
public static class CsvReader
{
    public static List<CsvRecord> Parse(string text)
    {
        var records = new List<CsvRecord>();
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;
        var recordHasContent = false;   // false for a line with nothing on it
        var line = 1;
        var recordStartLine = 1;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldWasQuoted = false;
        }

        void EndRecord()
        {
            EndField();
            if (recordHasContent) records.Add(new CsvRecord(recordStartLine, fields.ToArray()));
            fields = new List<string>();
            recordHasContent = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else
                {
                    if (c == '\n') line++;
                    else if (c == '\r' && !(i + 1 < text.Length && text[i + 1] == '\n')) line++;
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0 && !fieldWasQuoted:
                    inQuotes = true;
                    fieldWasQuoted = true;
                    recordHasContent = true;
                    break;
                case ',':
                    EndField();
                    recordHasContent = true;
                    break;
                case '\r':
                case '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRecord();
                    line++;
                    recordStartLine = line;
                    break;
                default:
                    field.Append(c);
                    recordHasContent = true;
                    break;
            }
        }

        if (inQuotes)
            throw new CsvFormatException("A quoted value is missing its closing quote.", recordStartLine);

        // Last record, when the file does not end with a line break.
        if (recordHasContent) EndRecord();

        return records;
    }
}
