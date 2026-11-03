using System.Text;

namespace NoticeSender.Core;
public static class SheetParser
{
    public static List<List<string>> ParseDelimited(string text, char delimiter = '\t')
    {
        List<List<string>> rows = [];
        List<string> row = [];
        var field = new StringBuilder();
        bool quoted = false;
        text = text.TrimStart('\uFEFF');
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (quoted || field.Length == 0)
                    quoted = !quoted;
                else
                    field.Append(c);
            }
            else if (c == delimiter && !quoted)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((c == '\r' || c == '\n') && !quoted)
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(field.ToString());
                rows.Add(row);
                row = [];
                field.Clear();
            }
            else
                field.Append(c);
        }

        if (quoted)
            throw new FormatException("닫히지 않은 따옴표가 있습니다.");
        row.Add(field.ToString());
        if (row.Any(x => x.Length > 0) || rows.Count == 0)
            rows.Add(row);
        return rows;
    }
}
