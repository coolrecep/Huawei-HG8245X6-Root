using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace HekzOntRace;

/// <summary>
/// Log seviyesi. Kullanıcı "Yalnız önemli" ya da "Yalnız sorunlar" seçtiğinde
/// hangi satırların görüneceğini belirler.
/// </summary>
public enum LogSeviye
{
    /// <summary>Alt basamak: "okundu", "yazıldı", "hazır" gibi ilerleme satırları.</summary>
    Ayrinti,
    /// <summary>Adım tamamlandı: "✓ ...", "OK", "<adım> ... tamam".</summary>
    Bilgi,
    /// <summary>Dikkat: atlandı, doğrulanamadı, beklenmeyen durum.</summary>
    Uyari,
    /// <summary>Engel: hata, başarısız, reddedildi.</summary>
    Hata
}

/// <summary>
/// Arayüzdeki log panelini yönetir: renkli seviyeler, filtreleme, kaydetme, kopyalama.
/// Renkler seviyeye göre atanır, böylece log göz gezdirilerek okunabilir.
/// </summary>
public sealed class LogYonetici
{
    private readonly RichTextBox _kutu;
    private readonly ComboBox _filtre;
    private readonly CheckBox _otoKaydir;
    private readonly CheckBox _zaman;
    private readonly Func<TextBlock> _ozetAl;

    // Filtre degisince ayni satirlari yeniden cizmek icin ham kayit tutulur.
    private readonly List<(LogSeviye seviye, string metin, string zamanli)> _kayit = new();
    private static readonly Regex ZamanKalibi = new(@"^\[\d{2}:\d{2}:\d{2}\]\s*", RegexOptions.Compiled);

    private static readonly Brush RenkAyrinti = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB5));
    private static readonly Brush RenkBilgi = new SolidColorBrush(Color.FromRgb(0x3F, 0xC3, 0x8A));
    private static readonly Brush RenkUyari = new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x4C));
    private static readonly Brush RenkHata = new SolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x6A));
    private static readonly Brush RenkNötr = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly Brush RenkBaslik = new SolidColorBrush(Color.FromRgb(0x6F, 0xB4, 0xF2));

    private int _gizlenenAyrinti, _gizlenenBilgi;

    public LogYonetici(RichTextBox kutu, ComboBox filtre, CheckBox otoKaydir, CheckBox zaman, Func<TextBlock> ozetAl)
    {
        _kutu = kutu;
        _filtre = filtre;
        _otoKaydir = otoKaydir;
        _zaman = zaman;
        _ozetAl = ozetAl;
        _kutu.Document.PageWidth = 4000;   // yatay kaydirma yerine sarma
        _kutu.Document.LineHeight = 15;
    }

    /// <summary>Zaman damgası zaten metinde var mı?</summary>
    private static bool ZamanVar(string m) => ZamanKalibi.IsMatch(m);

    /// <summary>Mesajın içeriğinden seviyeyi tahmin eder.</summary>
    public static LogSeviye SeviyeBul(string m)
    {
        string s = m.TrimStart();

        // Engel
        if (s.Contains("HATA", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("BAŞARISIZ", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("başarısız", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("reddedildi", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("kapanmadı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("uygulanamadı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("yüklenemedi", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("✗") || s.Contains("✘"))
            return LogSeviye.Hata;

        // Dikkat
        if (s.Contains("⚠") || s.Contains("uyarı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("okunamadı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("doğrulanamadı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("atlandı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("eşleşmedi", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("desteklemiyor", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("yok —") || s.Contains("izin vermiyor", StringComparison.OrdinalIgnoreCase))
            return LogSeviye.Uyari;

        string govde = ZamanKalibi.Replace(s, "");

        // Alt basamak: tekrar eden kurulum gürültüsü.
        // Bunlar "Yalnız önemli" filtresinde gizlenir; kaydedilen logda hepsi durur.
        if (govde.StartsWith("temizlendi", StringComparison.OrdinalIgnoreCase) ||
            govde.StartsWith("temizlenemedi", StringComparison.OrdinalIgnoreCase) ||
            govde.StartsWith("eski dosya", StringComparison.OrdinalIgnoreCase) ||
            govde.StartsWith("real.bin", StringComparison.OrdinalIgnoreCase) ||
            govde.StartsWith("lnk.bin", StringComparison.OrdinalIgnoreCase) ||
            govde.Equals("Script: Python", StringComparison.OrdinalIgnoreCase) ||
            govde.StartsWith("SFTP bağlantısı", StringComparison.OrdinalIgnoreCase) ||
            govde.StartsWith("  2.", StringComparison.Ordinal) ||
            govde.StartsWith("  ", StringComparison.Ordinal))
            return LogSeviye.Ayrinti;

        // Bilgi: başlıklar ve başarılı adımlar
        if (s.StartsWith("===", StringComparison.Ordinal) || s.Contains("✓") ||
            govde.Contains("OK", StringComparison.Ordinal) ||
            govde.Contains("tamamlandı", StringComparison.OrdinalIgnoreCase) ||
            govde.Contains("hazır", StringComparison.OrdinalIgnoreCase))
            return LogSeviye.Bilgi;

        return LogSeviye.Bilgi;
    }

    private static Brush Renk(LogSeviye s, string metin)
    {
        if (metin.StartsWith("===", StringComparison.Ordinal)) return RenkBaslik;
        return s switch
        {
            LogSeviye.Hata => RenkHata,
            LogSeviye.Uyari => RenkUyari,
            LogSeviye.Bilgi => RenkBilgi,
            _ => RenkAyrinti
        };
    }

    /// <summary>Bir satırı loga ekler (seviye otomatik belirlenir).</summary>
    public void Ekle(string mesaj, LogSeviye? zorla = null)
    {
        string ham = Core.LogMaskeleyici.Temizle(mesaj ?? "");
        string zamanli;
        if (_zaman.IsChecked == true && !ZamanVar(ham))
            zamanli = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + ham;
        else
            zamanli = ham;

        LogSeviye seviye = zorla ?? SeviyeBul(ham);
        _kayit.Add((seviye, ham, zamanli));

        // Filtreye uymuyorsa ekrana yazma, sayacı artır.
        int filtre = _filtre.SelectedIndex;
        bool gorunur =
            filtre == 0 ||
            (filtre == 1 && seviye >= LogSeviye.Bilgi) ||
            (filtre == 2 && seviye >= LogSeviye.Uyari);

        if (gorunur) ParcaEkle(zamanli, seviye);
        else if (seviye == LogSeviye.Ayrinti) _gizlenenAyrinti++;
        else _gizlenenBilgi++;

        OzetGuncelle();
    }

    private void ParcaEkle(string metin, LogSeviye seviye)
    {
        var p = new Paragraph(new Run(metin))
        {
            Foreground = Renk(seviye, metin),
            Margin = new Thickness(0),
            LineHeight = 15
        };
        _kutu.Document.Blocks.Add(p);
        if (_otoKaydir.IsChecked == true) _kutu.ScrollToEnd();
    }

    private void OzetGuncelle()
    {
        int hata = _kayit.Count(x => x.seviye == LogSeviye.Hata);
        int uyari = _kayit.Count(x => x.seviye == LogSeviye.Uyari);
        int toplam = _kayit.Count;
        int gizli = _gizlenenAyrinti + _gizlenenBilgi;

        var sb = new StringBuilder();
        sb.Append($"{toplam} satır");
        if (uyari > 0) sb.Append($" · {uyari} uyarı");
        if (hata > 0) sb.Append($" · {hata} HATA");
        if (gizli > 0) sb.Append($" · {gizli} gizli");
        if (hata == 0 && uyari == 0) sb.Append(" · sorun yok");

        try { _ozetAl()?.Let(t => t.Text = sb.ToString()); } catch { }
    }

    /// <summary>Filtre değişince tüm kaydı yeniden çizer.</summary>
    public void YenidenCiz()
    {
        _gizlenenAyrinti = 0; _gizlenenBilgi = 0;
        _kutu.Document.Blocks.Clear();

        int filtre = _filtre.SelectedIndex;
        foreach (var (seviye, _, zamanli) in _kayit)
        {
            bool gorunur =
                filtre == 0 ||
                (filtre == 1 && seviye >= LogSeviye.Bilgi) ||
                (filtre == 2 && seviye >= LogSeviye.Uyari);
            if (gorunur) ParcaEkle(zamanli, seviye);
            else if (seviye == LogSeviye.Ayrinti) _gizlenenAyrinti++;
            else _gizlenenBilgi++;
        }
        OzetGuncelle();
    }

    public void Temizle()
    {
        _kayit.Clear();
        _gizlenenAyrinti = 0; _gizlenenBilgi = 0;
        _kutu.Document.Blocks.Clear();
        OzetGuncelle();
    }

    /// <summary>Logu Masaüstü\HekzONT\altına zaman damgalı .txt olarak kaydeder.</summary>
    public string Kaydet()
    {
        string klasor = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "HekzONT", "loglar");
        Directory.CreateDirectory(klasor);
        string yol = Path.Combine(klasor, $"hekz_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        var sb = new StringBuilder();
        sb.AppendLine("# HekzOntRace v2 — çalıştırma günlüğü");
        sb.AppendLine("# Tarih: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();
        foreach (var (seviye, _, zamanli) in _kayit)
            sb.AppendLine(zamanli);

        File.WriteAllText(yol, sb.ToString(), new UTF8Encoding(true));  // BOM: Notepad'de Türkçe doğru görünür
        return yol;
    }

    /// <summary>Tüm logu (filtreden bağımsız) panoya kopyalar.</summary>
    public void Kopyala()
    {
        try
        {
            string metin = string.Join(Environment.NewLine, _kayit.Select(x => x.zamanli));
            Clipboard.SetText(metin);
        }
        catch { }
    }

    /// <summary>Zaman damgası düğmesi değiştiğinde tüm kaydı yeniden yazar.</summary>
    public void ZamanDegisti()
    {
        for (int i = 0; i < _kayit.Count; i++)
        {
            var (seviye, ham, _) = _kayit[i];
            string yeni = (_zaman.IsChecked == true && !ZamanVar(ham))
                ? "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + ham
                : ham;
            _kayit[i] = (seviye, ham, yeni);
        }
        YenidenCiz();
    }
}

/// <summary>Küçük yardımcı: null kontrolü ile kısa işlem.</summary>
internal static class Yardimci
{
    public static void Let<T>(this T deger, Action<T> islem) where T : class => islem(deger);
}
