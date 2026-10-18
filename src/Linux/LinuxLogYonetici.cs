using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HekzOntRace.Core;

namespace HekzOntRace.Linux;

public enum LogSeviye
{
    Ayrinti,
    Bilgi,
    Uyari,
    Hata
}

public sealed class LinuxLogYonetici
{
    private readonly StackPanel _panel;
    private readonly ScrollViewer _scroll;
    private readonly ComboBox _filtre;
    private readonly CheckBox _otoKaydir;
    private readonly CheckBox _zaman;
    private readonly Func<TextBlock> _ozetAl;
    private readonly Window _owner;

    private readonly List<(LogSeviye seviye, string metin, string zamanli)> _kayit = new();
    private static readonly Regex ZamanKalibi = new(@"^\[\d{2}:\d{2}:\d{2}\]\s*", RegexOptions.Compiled);

    private static readonly IBrush RenkAyrinti = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB5));
    private static readonly IBrush RenkBilgi   = new SolidColorBrush(Color.FromRgb(0x3F, 0xC3, 0x8A));
    private static readonly IBrush RenkUyari   = new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x4C));
    private static readonly IBrush RenkHata    = new SolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x6A));
    private static readonly IBrush RenkBaslik  = new SolidColorBrush(Color.FromRgb(0x6F, 0xB4, 0xF2));

    private int _gizlenenAyrinti, _gizlenenBilgi;

    public LinuxLogYonetici(
        StackPanel panel,
        ScrollViewer scroll,
        ComboBox filtre,
        CheckBox otoKaydir,
        CheckBox zaman,
        Func<TextBlock> ozetAl,
        Window owner)
    {
        _panel = panel;
        _scroll = scroll;
        _filtre = filtre;
        _otoKaydir = otoKaydir;
        _zaman = zaman;
        _ozetAl = ozetAl;
        _owner = owner;
    }

    private static bool ZamanVar(string m) => ZamanKalibi.IsMatch(m);

    public static LogSeviye SeviyeBul(string m)
    {
        string s = m.TrimStart();

        if (s.Contains("HATA", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("BAŞARISIZ", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("başarısız", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("reddedildi", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("kapanmadı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("uygulanamadı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("yüklenemedi", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("✗") || s.Contains("✘"))
            return LogSeviye.Hata;

        if (s.Contains("⚠") || s.Contains("uyarı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("okunamadı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("doğrulanamadı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("atlandı", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("eşleşmedi", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("desteklemiyor", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("yok —") || s.Contains("izin vermiyor", StringComparison.OrdinalIgnoreCase))
            return LogSeviye.Uyari;

        string govde = ZamanKalibi.Replace(s, "");

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

        return LogSeviye.Bilgi;
    }

    private static IBrush Renk(LogSeviye s, string metin)
    {
        if (metin.StartsWith("===", StringComparison.Ordinal)) return RenkBaslik;
        return s switch
        {
            LogSeviye.Hata  => RenkHata,
            LogSeviye.Uyari => RenkUyari,
            LogSeviye.Bilgi => RenkBilgi,
            _               => RenkAyrinti
        };
    }

    public void Ekle(string mesaj, LogSeviye? zorla = null)
    {
        string ham = LogMaskeleyici.Temizle(mesaj ?? "");
        string zamanli = (_zaman.IsChecked == true && !ZamanVar(ham))
            ? "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + ham
            : ham;

        LogSeviye seviye = zorla ?? SeviyeBul(ham);
        _kayit.Add((seviye, ham, zamanli));

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
        var tb = new SelectableTextBlock
        {
            Text = metin,
            Foreground = Renk(seviye, metin),
            FontFamily = new FontFamily("Monospace, DejaVu Sans Mono, Liberation Mono, Consolas"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 1)
        };
        _panel.Children.Add(tb);
        if (_otoKaydir.IsChecked == true)
        {
            _scroll.ScrollToEnd();
        }
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

        try
        {
            var t = _ozetAl();
            if (t != null) t.Text = sb.ToString();
        }
        catch { }
    }

    public void YenidenCiz()
    {
        _gizlenenAyrinti = 0;
        _gizlenenBilgi = 0;
        _panel.Children.Clear();

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
        _gizlenenAyrinti = 0;
        _gizlenenBilgi = 0;
        _panel.Children.Clear();
        OzetGuncelle();
    }

    public string Kaydet()
    {
        string klasor = Path.Combine(Engine.MasaustuDizini(), "HekzONT", "loglar");
        Directory.CreateDirectory(klasor);
        string yol = Path.Combine(klasor, $"hekz_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        var sb = new StringBuilder();
        sb.AppendLine("# HekzOntRace v2 — çalıştırma günlüğü");
        sb.AppendLine("# Tarih: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();
        foreach (var (_, _, zamanli) in _kayit)
            sb.AppendLine(zamanli);

        File.WriteAllText(yol, sb.ToString(), new UTF8Encoding(true));
        return yol;
    }

    public async Task KopyalaAsync()
    {
        try
        {
            string metin = string.Join(Environment.NewLine, _kayit.Select(x => x.zamanli));
            if (_owner.Clipboard != null)
            {
                await _owner.Clipboard.SetTextAsync(metin);
            }
        }
        catch { }
    }

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
