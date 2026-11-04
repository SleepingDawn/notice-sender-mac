using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NoticeSender.Automation;

/// <summary>Generated bot-test payloads only. Production generation uses Create; legacy rendering is for saved fixtures.</summary>
public static class SyntheticBotImage
{
    public const int Width = 320, Height = 160, GridX = 8, GridY = 24, GridCellSize = 8, GridSide = 16;

    public static byte[] Create(string stamp)
    {
        ValidateStamp(stamp);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(stamp));
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var ink = new SolidBrush(Color.FromArgb(24, 43, 65)))
        using (var accent = new SolidBrush(Color.FromArgb(26, 101, 130)))
        using (var black = new SolidBrush(Color.Black))
        using (var white = new SolidBrush(Color.White))
        using (var font = new Font(FontFamily.GenericSansSerif, 11))
        {
            graphics.Clear(Color.AliceBlue); graphics.FillRectangle(accent, 0, 0, Width, 18);
            for (int bit = 0; bit < GridSide * GridSide; bit++)
            {
                bool dark = (digest[bit / 8] & (1 << (7 - bit % 8))) != 0;
                graphics.FillRectangle(dark ? black : white, GridX + bit % GridSide * GridCellSize,
                    GridY + bit / GridSide * GridCellSize, GridCellSize, GridCellSize);
            }
            graphics.DrawString("NoticeSender\nsynthetic bot test", font, ink, new RectangleF(144, 24, 164, 40));
            graphics.DrawString(stamp, font, ink, new RectangleF(144, 70, 164, 42));
            graphics.DrawString("No student data", font, ink, new RectangleF(144, 128, 164, 24));
        }
        using var png = new MemoryStream(); bitmap.Save(png, ImageFormat.Png); return png.ToArray();
    }

    public static byte[] CreateLegacy(string stamp)
    {
        ValidateStamp(stamp);
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var ink = new SolidBrush(Color.FromArgb(24, 43, 65)))
        using (var accent = new SolidBrush(Color.FromArgb(26, 101, 130)))
        using (var font = new Font(FontFamily.GenericSansSerif, 11))
        {
            graphics.Clear(Color.AliceBlue); graphics.FillRectangle(accent, 0, 0, bitmap.Width, 18);
            graphics.DrawString("NoticeSender synthetic bot test\n\n" + stamp + "\n\nNo student data", font, ink, new RectangleF(12, 30, 296, 120));
        }
        using var png = new MemoryStream(); bitmap.Save(png, ImageFormat.Png); return png.ToArray();
    }
    private static void ValidateStamp(string stamp)
    {
        if (stamp == null || !Regex.IsMatch(stamp, @"\A\d{8}-\d{6}-\d{3}-[0-9a-f]{8}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("고유한 합성 시험 식별자가 필요합니다.", nameof(stamp));
    }
}
