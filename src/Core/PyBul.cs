using System.Diagnostics;

namespace HekzOntRace.Core;

/// <summary>Python yorumlayıcısını bulur ve doğrular (yazma yarışı süreci için).</summary>
internal static class PyBul
{
    public static string Bul(string tercih)
    {
        // tercih edilen yol önce
        if (Denetle(tercih)) return tercih!;
        // yaygın adlar
        foreach (var ad in new[] { "python3", "python", "py" })
            if (Denetle(ad)) return ad;
        return "python3";
    }

    private static bool Denetle(string ad)
    {
        if (string.IsNullOrEmpty(ad)) return false;
        try
        {
            var psi = new ProcessStartInfo(ad, "-c \"print(1)\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var cikti = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return cikti == "1";
        }
        catch { return false; }
    }
}
