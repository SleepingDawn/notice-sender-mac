using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using NoticeSender.Automation;

internal static class SyntheticBotImageTests
{
    public static Task EncodesCompleteStampDigestAndLayout()
    {
        if (SyntheticBotImage.Width != 320 || SyntheticBotImage.Height != 160 || SyntheticBotImage.GridX != 8 || SyntheticBotImage.GridY != 24 || SyntheticBotImage.GridCellSize != 8 || SyntheticBotImage.GridSide != 16)
            throw new Exception("Shared synthetic image geometry changed.");
        foreach (string stamp in new[] { "20261104-200000-001-1234abcd", "20261104-200000-002-abcdef01" })
        {
            byte[] bytes = SyntheticBotImage.Create(stamp);
            if (!bytes.AsSpan().SequenceEqual(SyntheticBotImage.Create(stamp))) throw new Exception("The same stamp generated different payload bytes.");
            using var stream = new MemoryStream(bytes); using var bitmap = new Bitmap(stream);
            if (bitmap.Width != 320 || bitmap.Height != 160) throw new Exception("Synthetic image dimensions changed.");
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(stamp));
            for (int bit = 0; bit < 256; bit++)
            {
                int expected = ((digest[bit / 8] >> (7 - bit % 8)) & 1) == 1 ? Color.Black.ToArgb() : Color.White.ToArgb();
                int left = 8 + bit % 16 * 8, top = 24 + bit / 16 * 8;
                for (int y = top; y < top + 8; y++) for (int x = left; x < left + 8; x++)
                    if (bitmap.GetPixel(x, y).ToArgb() != expected) throw new Exception($"Synthetic digest grid bit {bit} is missing, altered or covered by text.");
            }
            for (int y = 0; y < 18; y++) for (int x = 0; x < 320; x++)
                if (bitmap.GetPixel(x, y).ToArgb() != Color.FromArgb(26, 101, 130).ToArgb()) throw new Exception("Synthetic image accent header changed.");
            if (bitmap.GetPixel(0, 159).ToArgb() != Color.AliceBlue.ToArgb()) throw new Exception("Synthetic image background changed.");
        }
        return Task.CompletedTask;
    }

    public static Task PolicyAcceptsExactNewBytesAndRejectsTamperAndLegacy()
    {
        using var f = new Fixture(); var policy = new SyntheticFileConfirmationPolicy("KREAM", (_, _, _) => { });
        byte[] bytes = SyntheticBotImage.Create(f.Stamp);
        if (!bytes.AsSpan().SequenceEqual(SyntheticFileConfirmationPolicy.ExpectedBytes(f.Stamp, Path.GetFileName(f.Path)))) throw new Exception("Policy and bot generator disagree on exact PNG bytes.");
        File.WriteAllBytes(f.Path, bytes);
        using (var lease = policy.Freeze(f.Target, new[] { f.Path }, () => 7u)) lease.RequireUnchanged();
        using (var input = new MemoryStream(bytes))
        using (var changed = new Bitmap(input))
        using (var output = new MemoryStream())
        {
            changed.SetPixel(12, 28, Color.Red); changed.Save(output, ImageFormat.Png); File.WriteAllBytes(f.Path, output.ToArray());
        }
        Reject(() => { using var lease = policy.Freeze(f.Target, new[] { f.Path }, () => 7u); });
        File.WriteAllBytes(f.Path, SyntheticBotImage.CreateLegacy(f.Stamp));
        Reject(() => { using var lease = policy.Freeze(f.Target, new[] { f.Path }, () => 7u); });
        return Task.CompletedTask;
    }

    public static Task LegacyFactoryRetainsSavedFixtureIdentity()
    {
        // Golden legacy fixture contains generated bot data only; no production stamp whitelist is involved.
        byte[] legacy = SyntheticBotImage.CreateLegacy("20261104-073747-618-9dde2351");
        string hash = Convert.ToHexString(SHA256.HashData(legacy));
        if (hash != "E8054D91AFF126B633270FBE9FFD6A9400E9E27590405626A10D3CDE0139C304")
            throw new Exception("Frozen legacy factory no longer matches the saved source bytes: " + hash);
        return Task.CompletedTask;
    }

    public static Task DifferentStampAndInvalidIdentityAreRejected()
    {
        using var first = new Fixture(); using var second = new Fixture();
        File.WriteAllBytes(second.Path, SyntheticBotImage.Create(first.Stamp));
        var policy = new SyntheticFileConfirmationPolicy("KREAM", (_, _, _) => { });
        Reject(() => { using var lease = policy.Freeze(second.Target, new[] { second.Path }, () => 7u); });
        foreach (string bad in new[] { "", "ordinary-image", "20261104-200000-001-ABCDEFGH", "20261104-200000-001-abcdef01\nprivate" })
        { Reject(() => SyntheticBotImage.Create(bad)); Reject(() => SyntheticBotImage.CreateLegacy(bad)); }
        return Task.CompletedTask;
    }
    private static void Reject(Action action)
    { try { action(); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return; } throw new Exception("An altered, old or unrelated synthetic image was accepted for sending."); }
    private sealed class Fixture : IDisposable
    {
        public string Stamp { get; } = "20261104-200000-001-" + Guid.NewGuid().ToString("N")[..8];
        private string DirectoryPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NoticeSender-BotSendTest-" + Stamp);
        public string Path => System.IO.Path.Combine(DirectoryPath, "NoticeSender-SYNTHETIC-" + Stamp + "-E-image.png");
        public ChatTarget Target { get; } = new(Environment.ProcessId, 123, "KREAM", 456);
        public Fixture() { Directory.CreateDirectory(DirectoryPath); }
        public void Dispose() { File.Delete(Path); Directory.Delete(DirectoryPath); }
    }
}
