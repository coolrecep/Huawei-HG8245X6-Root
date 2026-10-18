// Backup.cs — ayarları yedekleme ve geri yükleme.
//
// İki yol var:
//  1) CİHAZ DOSYASI: cihazın kendi "tüm ayarları kaydet" dosyası (cfgfiledown.cgi /
//     cfgfileupload.cgi). En kapsamlı yedek; geri yükleme cihaz tarafından yapılır.
//  2) PROFİL (JSON): yalnızca seçilen kategoriler okunur ve kaydedilir; geri yüklemede
//     her ayar tek tek cihaza yazılır ve doğrulanır. Seçmeli/şeffaf yöntem budur.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HekzOntRace.Core;

/// <summary>Yedekleme/geri yükleme kapsamı — hangi ayar grupları dahil edilsin.</summary>
public sealed class BackupKapsam
{
    public bool Wifi { get; set; } = true;
    public bool Dns { get; set; } = true;
    public bool Tr069 { get; set; } = true;
    public bool GuvenlikAlg { get; set; } = true;
    public bool WanBilgi { get; set; } = true;
    public bool CihazBilgi { get; set; } = true;

    public bool HicBiriSecili =>
        !Wifi && !Dns && !Tr069 && !GuvenlikAlg && !WanBilgi && !CihazBilgi;
}

/// <summary>Profilden tek bir WLAN bandının kaydı.</summary>
public sealed class WifiKaydi
{
    public string Instance { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Band { get; set; } = "";
    public string Ssid { get; set; } = "";
    public string Psk { get; set; } = "";
    public bool Hidden { get; set; }
}

/// <summary>Tek bir WAN bağlantısının kaydı.</summary>
public sealed class WanKaydi
{
    public string Domain { get; set; } = "";
    public string Ad { get; set; } = "";
    public string Durum { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Mtu { get; set; } = "";
    public string PppoeKullanici { get; set; } = "";
    public string Uptime { get; set; } = "";
}

/// <summary>Yedek profili — JSON olarak diske yazılır.</summary>
public sealed class YedekProfili
{
    public int Surum { get; set; } = 1;
    public string Uygulama { get; set; } = "HekzOntRace v2.2";
    public string CihazAdresi { get; set; } = "";
    public string CihazModeli { get; set; } = "";
    public string SeriNo { get; set; } = "";
    public string Yazilim { get; set; } = "";
    /// <summary>Yedek alındığı andaki zaman damgası (yerel saat metni).</summary>
    public string Tarih { get; set; } = "";
    public BackupKapsam Kapsam { get; set; } = new();

    // --- WiFi ---
    public List<WifiKaydi> Wifi { get; set; } = new();

    // --- DNS ---
    public string DnsBirincil { get; set; } = "";
    public string DnsIkincil { get; set; } = "";
    public bool DnsLanTarafi { get; set; }
    public string WanDomain { get; set; } = "";

    // --- TR-069 ---
    public bool Tr069Cwmp { get; set; }
    public bool Tr069Periyodik { get; set; }
    public string Tr069AcsUrl { get; set; } = "";

    // --- Güvenlik / ALG ---
    public string GuvenlikSeviyesi { get; set; } = "";
    public Dictionary<string, bool> Alg { get; set; } = new();

    // --- WAN bilgisi ---
    public List<WanKaydi> Wan { get; set; } = new();

    // --- Cihaz bilgisi ---
    public string Model { get; set; } = "";
    public string Donanim { get; set; } = "";
    public string YazilimSurumu { get; set; } = "";
    public string Mac { get; set; } = "";

    [JsonIgnore]
    public string DosyaYolu { get; set; } = "";
}

public static class BackupYardimci
{
    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Okunan cihaz bilgisinden, seçilen kapsama göre bir yedek profili üretir.</summary>
    public static YedekProfili ProfilOlustur(ModemInfo mi, BackupKapsam kapsam, string ip)
    {
        var p = new YedekProfili
        {
            CihazAdresi = ip,
            Tarih = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Kapsam = kapsam
        };

        if (kapsam.CihazBilgi)
        {
            p.Model = mi.DeviceName;
            p.Donanim = mi.HwVersion;
            p.YazilimSurumu = mi.SwVersion;
            p.Mac = mi.Mac;
            p.SeriNo = mi.Serial;
            p.Yazilim = mi.SwVersion;
            p.CihazModeli = mi.DeviceName;
        }

        if (kapsam.Wifi)
            foreach (WifiBand b in mi.Bands)
                p.Wifi.Add(new WifiKaydi
                {
                    Instance = b.Instance,
                    Domain = b.Domain,
                    Band = b.Band,
                    Ssid = b.Ssid,
                    Psk = b.Psk,
                    Hidden = b.Hidden
                });

        if (kapsam.Dns)
        {
            p.DnsBirincil = mi.DnsPrimary;
            p.DnsIkincil = mi.DnsSecondary;
            p.DnsLanTarafi = mi.DnsFromLan;
            p.WanDomain = mi.WanDomain;
        }

        if (kapsam.Tr069)
        {
            p.Tr069Cwmp = mi.CwmpEnabled;
            p.Tr069Periyodik = mi.CwmpPeriodicInform;
            p.Tr069AcsUrl = mi.AcsUrl;
        }

        if (kapsam.GuvenlikAlg)
        {
            p.GuvenlikSeviyesi = mi.FirewallLevel;
            p.Alg = new Dictionary<string, bool>(mi.Alg);
        }

        if (kapsam.WanBilgi && mi.WanName.Length > 0)
            p.Wan.Add(new WanKaydi
            {
                Domain = mi.WanDomain,
                Ad = mi.WanName,
                Durum = mi.WanStatus,
                Ip = mi.WanIp,
                Mtu = mi.WanMtu,
                PppoeKullanici = mi.PppoeUser,
                Uptime = mi.PppoeUptime
            });

        return p;
    }

    /// <summary>Profili okunabilir bir JSON dosyasına yazar.</summary>
    public static async Task<string> ProfilKaydetAsync(YedekProfili profil, string klasor, CancellationToken ct)
    {
        Directory.CreateDirectory(klasor);
        string ad = $"hekz_yedek_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string yol = Path.Combine(klasor, ad);
        string json = JsonSerializer.Serialize(profil, JsonOpt);
        await File.WriteAllTextAsync(yol, json, ct);
        profil.DosyaYolu = yol;
        return yol;
    }

    /// <summary>JSON profili dosyadan okur. Geçersizse null döner ve sebebi verir.</summary>
    public static YedekProfili ProfilOku(string yol, out string hata)
    {
        hata = "";
        try
        {
            string json = File.ReadAllText(yol);
            YedekProfili p = JsonSerializer.Deserialize<YedekProfili>(json, JsonOpt);
            if (p == null) { hata = "Dosya okunamadı (boş veya geçersiz JSON)."; return null; }
            if (p.Surum <= 0) { hata = "Bu dosya bir HekzOntRace yedek profili değil."; return null; }
            p.DosyaYolu = yol;
            return p;
        }
        catch (JsonException ex) { hata = "JSON ayrıştırılamadı: " + ex.Message; return null; }
        catch (Exception ex) { hata = "Dosya okunamadı: " + ex.Message; return null; }
    }

    /// <summary>Profil içeriğinin kısa özeti (kullanıcıya göstermek için).</summary>
    public static string Ozet(YedekProfili p)
    {
        var s = new List<string>();
        if (p.Kapsam.Wifi && p.Wifi.Count > 0)
        {
            string bantlar = string.Join(", ", p.Wifi.Select(w => w.Band));
            s.Add(p.Wifi.Count + " WiFi bandı (" + bantlar + ")");
        }
        if (p.Kapsam.Dns) s.Add("DNS: " + Bos(p.DnsBirincil) + "/" + Bos(p.DnsIkincil));
        if (p.Kapsam.Tr069) s.Add("TR-069 CWMP: " + (p.Tr069Cwmp ? "AÇIK" : "kapalı"));
        if (p.Kapsam.GuvenlikAlg) s.Add("Güvenlik: " + Bos(p.GuvenlikSeviyesi) + ", " + p.Alg.Count + " ALG");
        if (p.Kapsam.WanBilgi && p.Wan.Count > 0) s.Add(p.Wan.Count + " WAN bağlantısı");
        if (p.Kapsam.CihazBilgi) s.Add("Cihaz: " + Bos(p.Model) + " / " + Bos(p.SeriNo));
        return s.Count == 0 ? "(boş profil)" : string.Join(" | ", s);
    }

    private static string Bos(string s) => string.IsNullOrEmpty(s) ? "-" : s;
}

/// <summary>Profildeki ayarları cihaza geri yükler ve her adımı doğrular.</summary>
public static class ProfilGeriYukleme
{
    /// <summary>
    /// Profildeki seçili ayarları cihaza uygular. Dönen liste: her adım için (başarılı, mesaj).
    /// </summary>
    public static async Task<List<(bool ok, string msg)>> UygulaAsync(
        OntWeb web, YedekProfili profil, BackupKapsam kapsam, CancellationToken ct)
    {
        var sonuclar = new List<(bool ok, string msg)>();

        // --- WiFi: SSID + görünürlük + parola ---
        if (kapsam.Wifi && profil.Wifi.Count > 0)
        {
            ModemInfo guncel = await ModemInfoReader.FetchAllAsync(web, ct, null);
            foreach (WifiKaydi kayit in profil.Wifi)
            {
                if (kayit.Ssid.Length == 0) continue;
                WifiBand bant = guncel.Bands.FirstOrDefault(b => b.Domain == kayit.Domain)
                              ?? guncel.Bands.FirstOrDefault(b => b.Instance == kayit.Instance);
                if (bant == null)
                {
                    sonuclar.Add((false, $"[WiFi] {kayit.Band}: cihazda karşılık gelen bant bulunamadı"));
                    continue;
                }
                var r = await ModemInfoWriter.SetSsidAsync(web, bant, kayit.Ssid, !kayit.Hidden,
                                                           string.IsNullOrEmpty(kayit.Psk) ? null : kayit.Psk, ct);
                sonuclar.Add((r.ok, "[WiFi] " + r.msg));
            }
        }

        // --- DNS ---
        if (kapsam.Dns && !string.IsNullOrEmpty(profil.DnsBirincil))
        {
            // Profil hangi taraftan alındıysa oraya yaz.
            if (profil.DnsLanTarafi)
            {
                var r = await ModemInfoWriter.SetLanDnsAsync(web, profil.DnsBirincil, profil.DnsIkincil, ct);
                sonuclar.Add((r.ok, "[DNS/LAN] " + r.msg));
            }
            else if (!string.IsNullOrEmpty(profil.WanDomain))
            {
                var r = await ModemInfoWriter.SetWanDnsAsync(web, profil.WanDomain, profil.DnsBirincil, profil.DnsIkincil, ct);
                sonuclar.Add((r.ok, "[DNS/WAN] " + r.msg));
            }
            else
            {
                var r = await ModemInfoWriter.SetLanDnsAsync(web, profil.DnsBirincil, profil.DnsIkincil, ct);
                sonuclar.Add((r.ok, "[DNS] " + r.msg));
            }
        }

        // --- TR-069 ---
        if (kapsam.Tr069)
        {
            var r = await ModemInfoWriter.SetTr069Async(web, profil.Tr069Cwmp, profil.Tr069Periyodik, ct);
            sonuclar.Add((r.ok, "[TR-069] " + r.msg));
        }

        // --- Güvenlik duvarı ---
        if (kapsam.GuvenlikAlg && !string.IsNullOrEmpty(profil.GuvenlikSeviyesi))
        {
            string seviye = SeviyeKodu(profil.GuvenlikSeviyesi);
            if (seviye.Length == 0)
                sonuclar.Add((false, $"[Güvenlik] Seviye yorumlanamadı: {profil.GuvenlikSeviyesi}"));
            else
            {
                var r = await ModemInfoWriter.SetFirewallLevelAsync(web, seviye, ct);
                sonuclar.Add((r.ok, "[Güvenlik] " + r.msg));
            }
        }

        // --- ALG ---
        if (kapsam.GuvenlikAlg && profil.Alg.Count > 0)
        {
            var r = await ModemInfoWriter.SetAlgAsync(web, profil.Alg, ct);
            sonuclar.Add((r.ok, "[ALG] " + r.msg));
        }

        return sonuclar;
    }

    /// <summary>Okunabilir seviye metnini cihazın beklediği koda çevirir.</summary>
    private static string SeviyeKodu(string metin)
    {
        string m = metin.ToLowerInvariant();
        if (m.Contains("kapalı") || m.Contains("disable") || m.Contains("off")) return "Disable";
        if (m.Contains("düşük") || m.Contains("low")) return "Low";
        if (m.Contains("yüksek") || m.Contains("high")) return "High";
        if (m.Contains("katı") || m.Contains("strict")) return "Strict";
        return metin;   // zaten kod olabilir
    }
}
