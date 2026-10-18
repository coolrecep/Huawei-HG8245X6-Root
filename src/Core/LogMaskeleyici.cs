using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HekzOntRace.Core;

/// <summary>
/// Log ve durum çubuğu (Phase) çıktılarındaki iç sistem dosya adlarını,
/// anlamsız teknik terimleri ve hassas kullanıcı adı/parola bilgilerini temizler ve maskeler.
/// </summary>
public static class LogMaskeleyici
{
    private static readonly object _lock = new();
    private static readonly HashSet<string> _gizliDegerler = new(StringComparer.Ordinal);

    // Otomatik üretilen geçici veya yeni şifre kalıbı (Hekz + rakamlar)
    private static readonly Regex HekzSifreKalibi = new(@"\bHekz\d{6,12}\b", RegexOptions.Compiled);

    // Geçici servis kullanıcı adları (hekz / hekzs)
    private static readonly Regex GeciciKullaniciKalibi = new(@"\bhekzs?\b", RegexOptions.Compiled);

    public static void GizliDegerEkle(string deger)
    {
        if (string.IsNullOrWhiteSpace(deger)) return;
        string d = deger.Trim();
        if (d.Length < 3) return;
        // "(okunamadı)", "(çözülemedi)" gibi yer tutucuları veya IP/kullanıcı adlarını maskeleme
        if (d.StartsWith("(") && d.EndsWith(")")) return;
        if (d.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
            d.Equals("sUser", StringComparison.OrdinalIgnoreCase) ||
            d.Equals("root", StringComparison.OrdinalIgnoreCase) ||
            d.Equals("user", StringComparison.OrdinalIgnoreCase))
            return;

        lock (_lock)
        {
            _gizliDegerler.Add(d);
        }
    }

    public static string Temizle(string metin)
    {
        if (string.IsNullOrEmpty(metin)) return metin;

        string s = metin.Replace("\r", "");

        // 1. İç dosya adları ve teknik terimlerin kullanıcı dostu karşılıklarla değiştirilmesi
        s = Regex.Replace(s, @"\s*\(\s*/?mnt/jffs2/customizepara\.txt\s*\)", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*\(\s*customizepara\.txt\s*\)", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"/?mnt/jffs2/customizepara\.txt", "fabrika parametre alanı", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"customizepara\.txt", "fabrika parametre bloğu", RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"\s*\(\s*/?mnt/jffs2/hw_ctree\.xml\s*\)", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*\(\s*hw_ctree\.xml\s*\)", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"/?mnt/jffs2/hw_ctree(?:_bak|_backup)?\.xml", "sistem yapılandırma veritabanı", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"hw_ctree(?:_web_orijinal|_web_duzenli|_orijinal|_dec|_\d{8}_\d{6})?\.(?:xml|bin)", "yapılandırma yedeği", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bhw_ctree\b", "sistem yapılandırması", RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"cfgfiledown\.cgi", "yapılandırma indirme servisi", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"cfgfileupload\.cgi", "yapılandırma yükleme servisi", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"html/ssmp/cfgfile/cfgfile\.asp|cfgfile\.asp", "yapılandırma servisi", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"html/ssmp/usbftp/usbhost\.asp|usbhost\.asp", "USB depolama servisi", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"html/ssmp/samba/sambasmart\.asp|sambasmart\.asp", "dosya paylaşım servisi", RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"\b(?:cur|real|lnk|lnk2|lnk3)\.bin\b", "geçici aktarım dosyası", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"cur/real/lnk\.bin", "geçici aktarım dosyaları", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"hekz_py_[a-f0-9]+\.bin", "geçici veri paketi", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"hekz_[a-f0-9]+\.py", "yardımcı betik", RegexOptions.IgnoreCase);

        // 2. Geçici kullanıcı adları ve otomatik üretilen şifrelerin gizlenmesi
        s = HekzSifreKalibi.Replace(s, "********");
        s = GeciciKullaniciKalibi.Replace(s, "********");

        // 3. Kayıtlı hassas şifrelerin (sUser, WiFi, PPPoE, Fabrika parolaları) maskelenmesi
        lock (_lock)
        {
            foreach (string gizli in _gizliDegerler)
            {
                if (gizli.Length >= 3 && s.Contains(gizli, StringComparison.Ordinal))
                {
                    s = s.Replace(gizli, "********", StringComparison.Ordinal);
                }
            }
        }

        return s;
    }
}
