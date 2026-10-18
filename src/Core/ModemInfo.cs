// ModemInfo.cs — cihazdan bilgi okuyan ve cihazda değişiklik yapan yardımcı.
//
// Sayfa yolları ve alan adları gerçek cihazdan (Huawei LG8245X6-50 / HG8245X6,
// CfgMode = TURKCELL2) canlı olarak doğrulandı. Önceki sürüm var olmayan
// sayfaları (html/bbsp/wlan/wlanbasic.asp, html/bbsp/dns/dns.asp, ...) ve
// tutmayan "adlı alan" regexlerini kullanıyordu; bu yüzden sekmeler boş
// geliyor ve kaydetme çalışmıyordu.
//
// Önemli: bu cihaz verisini HTML içinde KONUMSAL JavaScript dizileri olarak
// yayınlar:  new stDeviceInfo("domain","seri","hw","sw","model",...)
// Bu yüzden değerler adla değil sırayla okunur.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HekzOntRace.Core;

public sealed class WifiBand
{
    public string Instance = "";
    public string Domain = "";
    public string Ssid = "";
    public string Channel = "";
    public string Band = "";
    public string Standard = "";
    public bool Hidden;
    public string Security = "";
    public string Encryption = "";
    public bool Enabled;
    public string Psk = "";
}

public sealed class ModemInfo
{
    // Cihaz
    public string DeviceName = "";
    public string HwVersion = "";
    public string SwVersion = "";
    public string Serial = "";
    public string Uptime = "";
    public string Mac = "";
    public string LoaderVersion = "";

    // PON / optik durumu (bu cihazda güç değerleri yayınlanmıyor; durum okunabiliyor)
    public string PonMode = "";        // "ge" = Ethernet/GE uplink, "gpon"/"epon" = optik
    public string OntDurumu = "";      // ONLINE / OFFLINE
    public string OptikTest = "";      // OK / Failed / NoCheck
    public string OltEslesme = "";     // ontMatchOLTStatus

    // Optik güç değerleri (bu cihaz yayınlamıyor)
    public string OpticalRxPower = "";
    public string OpticalTxPower = "";
    public string OpticalVoltage = "";
    public string OpticalTemperature = "";
    public string OpticalBias = "";

    // WAN / PPPoE
    public string WanDomain = "";
    public string WanName = "";
    public string WanStatus = "";
    public string WanIp = "";
    public string WanMtu = "";
    public string PppoeUser = "";
    public string PppoeUptime = "";

    // DNS
    public string DnsPrimary = "";
    public string DnsSecondary = "";
    public bool DnsFromLan;          // true: LAN/DHCP tarafından, false: WAN'dan

    // Güvenlik duvarı
    public string FirewallLevel = "";

    // ALG
    public Dictionary<string, bool> Alg = new();

    // TR-069
    public bool CwmpEnabled;
    public bool CwmpPeriodicInform;
    public string AcsUrl = "";

    // WiFi
    public List<WifiBand> Bands = new();

    // Okuma sırasında oluşan notlar (boş sekme yerine gerçek sebep göstermek için)
    public List<string> Notes = new();
}

public static class ModemInfoReader
{
    // --- Gerçek (doğrulanmış) sayfa yolları ---
    public const string PDeviceInfo = "html/ssmp/deviceinfo/deviceinfo.asp";
    public const string PWlan       = "html/amp/wlanbasic/WlanBasic.asp";
    public const string PAlg        = "html/bbsp/alg/alg.asp";
    public const string PFirewall   = "html/bbsp/firewalllevel/firewalllevel.asp";
    public const string PTr069      = "html/ssmp/tr069/tr069.asp";
    public const string PDhcp       = "html/bbsp/dhcpservercfg/dhcp2.asp";
    public const string PWanCache   = "html/bbsp/common/wan_list_cache_wan.asp";
    public const string POntState   = "html/bbsp/common/ontstate.asp";
    public const string POpticTest  = "html/bbsp/maintenance/getEquipTestResultsmart.asp";

    public static async Task<ModemInfo> FetchAllAsync(OntWeb web, CancellationToken ct, Action<string> log = null)
    {
        ModemInfo mi = new ModemInfo();
        void L(string m) { try { log?.Invoke(m); } catch { } }

        await Pull(web, ct, mi, L, PDeviceInfo, "Cihaz",   ParseDeviceInfo);
        await Pull(web, ct, mi, L, PWanCache,  "WAN",      ParseWan);
        await Pull(web, ct, mi, L, PDhcp,      "DNS",      ParseDns);
        await Pull(web, ct, mi, L, PWlan,      "WiFi",     ParseWifi);
        await Pull(web, ct, mi, L, PFirewall,  "Güvenlik", ParseFirewall);
        await Pull(web, ct, mi, L, PAlg,       "ALG",      ParseAlg);
        await Pull(web, ct, mi, L, PTr069,     "TR-069",   ParseTr069);

        await OptikDurumOku(web, ct, mi, L);

        if (mi.Bands.Count == 0) mi.Notes.Add("WiFi bantları okunamadı.");
        return mi;
    }

    /// <summary>
    /// Optik güç değerleri bu cihaz yazılımında yayınlanmıyor; bunun yerine okunabilen
    /// PON modu / ONT durumu / optik tanılama sonucunu doldurur ve nedenini not eder.
    /// Kanıt: ana sayfanın kullandığı getOpticTxRx.asp 404 döner ve cihazın kendi
    /// değişkeni opticInfo='--' der; ontstate.asp ise PonMode='ge' bildirir.
    /// </summary>
    private static async Task OptikDurumOku(OntWeb web, CancellationToken ct, ModemInfo mi, Action<string> log)
    {
        // PON modu + ONT durumu
        try
        {
            (int st, string body) = await web.GetPageExAsync(POntState, ct);
            if (st == 200)
            {
                string html = OntWeb.Unescape(body);
                Match pm = Regex.Match(html, @"var\s+PonMode\s*=\s*'([^']*)'");
                if (pm.Success)
                {
                    string mod = pm.Groups[1].Value.ToUpperInvariant();
                    mi.PonMode = mod == "GE" ? "GE (Ethernet yukarı bağlantı)" : mod;
                }
                // ONT durumu: cihazın kendi mantığı,
                //   PON kaydı O5/O5AUTH ise ONLINE; aksi halde OFFLINE döner.
                // DİKKAT: ontstate.asp içindeki düz "ONLINE" metni, cihazın kodundaki
                // son ELSE ("else { OntState = \"ONLINE\" }") dalıdır; gerçek kayıt kanıtı DEĞİLDİR.
                // Gerçek PON kayıt kanıtı: cihazın kodunda O5/O5AUTH karşılaştırması var mı?
                // (Verbatim string içinde tırnak kaçışı sorun çıkardığı için düz arama kullanılıyor.)
                bool o5Var = Regex.IsMatch(html, @"GponOntStateInfo\[\.?\.*0\.?\.*\]\s*\.\s*Status")
                          && html.IndexOf("O5", StringComparison.OrdinalIgnoreCase) >= 0;
                Match reg = Regex.Match(html, @"RegStatus");
                string regBilgi = reg.Success ? "cihaz sayfasında RegStatus alanı var, değeri ayrıca okunamıyor" : "";

                if (mi.PonMode.StartsWith("GE", StringComparison.Ordinal))
                {
                    // Bu modda PON optik katmanı kullanılmıyor.
                    mi.OntDurumu = "GE modunda (PON katmanı yok)";
                }
                else if (o5Var)
                {
                    mi.OntDurumu = "ONLINE (O5)";
                }
                else
                {
                    mi.OntDurumu = "OFFLINE";
                }
                if (regBilgi.Length > 0) mi.OntDurumu += $" — {regBilgi}";
            }
        }
        catch (Exception ex) { log("[Optik] PON durumu okunamadı: " + ex.Message); }

        // Optik tanılama sonucu
        try
        {
            (int st, string body) = await web.GetPageExAsync(POpticTest, ct);
            if (st == 200)
            {
                Match m = Regex.Match(OntWeb.Unescape(body), @"new\s+SelfTestResultClass\(([^)]*)\)");
                if (m.Success)
                {
                    string[] p = m.Groups[1].Value.Split(',');
                    // 5. alan = OpticResult (domain, SD5113, Wifi, ExtLsw, Codec, Optic, ...)
                    if (p.Length > 5) mi.OptikTest = p[5].Trim().Trim('"');
                }
            }
        }
        catch { }

        // OLT eşleşme durumu (deviceinfo)
        try
        {
            (int st, string body) = await web.GetPageExAsync(PDeviceInfo, ct);
            if (st == 200)
            {
                string html = OntWeb.Unescape(body);
                Match ol = Regex.Match(html, @"var\s+ontMatchOLTStatus\s*=\s*'([^']*)'");
                if (ol.Success) mi.OltEslesme = ol.Groups[1].Value == "1" ? "eşleşti" : "eşleşmedi";
                Match oi = Regex.Match(html, @"var\s+opticInfo\s*=\s*'([^']*)'");
                if (oi.Success && (oi.Groups[1].Value == "--" || oi.Groups[1].Value.Length == 0))
                {
                    // Optik bilgi webde yok, duplicate not eklemiyoruz
                }
            }
        }
        catch { }

        string uplinkDurum = mi.PonMode.StartsWith("GE", StringComparison.OrdinalIgnoreCase)
            ? "Ethernet WAN (Harici ONT)"
            : mi.PonMode;
        log($"[Optik / Hat] Uplink Modu: {uplinkDurum} | ONT Durumu: {Bos0(mi.OntDurumu)}");
        if (mi.PonMode.StartsWith("GE", StringComparison.OrdinalIgnoreCase))
        {
            log("[Optik / Hat] Dahili optik güç değerleri (RX/TX) harici ONT modunda web arayüzünde sunulmamaktadır.");
        }
        else
        {
            log($"[Optik / Hat] Tanılama: {Bos0(mi.OptikTest)} | OLT Eşleşme: {Bos0(mi.OltEslesme)}");
        }
    }

    private static string Bos0(string s) => string.IsNullOrEmpty(s) ? "-" : s;

    private static async Task Pull(OntWeb web, CancellationToken ct, ModemInfo mi, Action<string> log,
                                   string path, string label, Action<ModemInfo, string> parse)
    {
        try
        {
            (int status, string body) = await web.GetPageExAsync(path, ct);
            if (status != 200 || body.Length < 80)
            {
                string neden = status == 403 ? "Yetki yetersiz veya oturum kapandı" : "HTTP " + status;
                log($"[{label}] Bilgiler alınamadı ({neden})");
                mi.Notes.Add($"{label}: modül okunamadı ({neden})");
                return;
            }
            parse(mi, body);
            string aciklama = label switch
            {
                "Cihaz" => "Sistem ve donanım bilgileri okundu ✓",
                "WAN" => "WAN ve internet bağlantı profilleri okundu ✓",
                "DNS" => "DHCP ve yerel ağ DNS yapılandırması okundu ✓",
                "WiFi" => "Kablosuz ağ (2.4G / 5G) parametreleri okundu ✓",
                "Güvenlik" => "Güvenlik duvarı kuralları okundu ✓",
                "ALG" => "Uygulama geçitleri (ALG) protokolleri okundu ✓",
                "TR-069" => "Uzaktan yönetim (TR-069 ACS) ayarları okundu ✓",
                _ => $"{label} modülü okundu ✓"
            };
            log($"[{label}] {aciklama}");
        }
        catch (Exception ex)
        {
            log($"[{label}] Okuma hatası: {ex.Message}");
            mi.Notes.Add($"{label}: {ex.Message}");
        }
    }

    // ---------- JS bağlamından konumsal argüman çıkarma ----------

    /// <summary>new funcName( ... ) çağrısının parantez içini döndürür (tırnak farkında).</summary>
    internal static string CallArgs(string html, string funcName)
    {
        int i = html.IndexOf("new " + funcName + "(", StringComparison.Ordinal);
        if (i < 0) return null;
        int start = i + ("new " + funcName + "(").Length;
        int depth = 1;
        bool inStr = false;
        for (int j = start; j < html.Length; j++)
        {
            char c = html[j];
            if (inStr)
            {
                if (c == '\\') { j++; continue; }
                if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; continue; }
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) return html.Substring(start, j - start);
            }
        }
        return null;
    }

    /// <summary>Aynı çağrının tüm örneklerini döndürür (ör. iki WLAN kaydı).</summary>
    private static List<string> AllCallArgs(string html, string funcName)
    {
        List<string> list = new List<string>();
        int pos = 0;
        string needle = "new " + funcName + "(";
        while (true)
        {
            int i = html.IndexOf(needle, pos, StringComparison.Ordinal);
            if (i < 0) break;
            string sub = html.Substring(i);
            string args = CallArgs(sub, funcName);
            if (args == null) break;
            list.Add(args);
            pos = i + needle.Length;
        }
        return list;
    }

    /// <summary>Argüman metnini konumsal parçalara böler ve \x2e gibi kaçışları çözer.</summary>
    internal static List<string> SplitArgs(string args)
    {
        List<string> list = new List<string>();
        if (args == null) return list;
        StringBuilder sb = new StringBuilder();
        bool inStr = false;
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (inStr)
            {
                if (c == '\\' && i + 1 < args.Length) { sb.Append(c); sb.Append(args[++i]); continue; }
                if (c == '"') { inStr = false; continue; }
                sb.Append(c);
            }
            else
            {
                if (c == '"') { inStr = true; continue; }
                if (c == ',') { list.Add(OntWeb.Unescape(sb.ToString())); sb.Clear(); continue; }
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            }
        }
        list.Add(OntWeb.Unescape(sb.ToString()));
        return list;
    }

    private static string At(List<string> a, int i) => (a.Count > i) ? a[i].Trim() : "";

    // ---------- Ayrıştırıcılar ----------

    private static void ParseDeviceInfo(ModemInfo mi, string html)
    {
        List<string> a = SplitArgs(CallArgs(html, "stDeviceInfo"));
        // stDeviceInfo(domain, SerialNumber, HardwareVersion, SoftwareVersion, ModelName, VendorID, ReleaseTime, Mac, ...)
        mi.Serial     = At(a, 1);
        mi.HwVersion  = At(a, 2);
        mi.SwVersion  = At(a, 3);
        mi.DeviceName = At(a, 4);
        mi.Mac        = At(a, 7);

        Match up = Regex.Match(html, @"var\s+dev_uptime\s*=\s*'(\d+)'");
        if (up.Success && long.TryParse(up.Groups[1].Value, out long sn))
            mi.Uptime = SureyeCevir(sn);
    }

    private static string SureyeCevir(long saniye)
    {
        TimeSpan t = TimeSpan.FromSeconds(saniye);
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} gün {t.Hours} sa {t.Minutes} dk";
        if (t.TotalHours >= 1) return $"{t.Hours} sa {t.Minutes} dk";
        return $"{t.Minutes} dk {t.Seconds} sn";
    }

    private static void ParseWan(ModemInfo mi, string html)
    {
        // new WanPPP(domain, ..., Status(5), ..., Name(8), ..., ConnectionStatus(12),
        //            Mode(13), IPAddress(14), Gateway(15), ..., dnsstr(18), Username(19),
        //            ..., MaxMRUSize(37), ..., Uptime(40), ...)
        List<string> a = SplitArgs(CallArgs(html, "WanPPP"));
        if (a.Count < 20)
        {
            a = SplitArgs(CallArgs(html, "WanIP"));
            if (a.Count < 16) return;
        }
        mi.WanDomain  = At(a, 0);
        mi.WanName    = At(a, 8);
        string cs     = At(a, 12);
        mi.WanStatus  = cs.Length > 0 ? cs : At(a, 5);
        mi.WanIp      = At(a, 14);
        mi.WanMtu     = At(a, 37);
        mi.PppoeUser  = At(a, 19);
        mi.PppoeUptime = At(a, 40);

        string dns = At(a, 18);
        if (dns.Length > 0)
        {
            string[] p = dns.Split(',');
            mi.DnsPrimary   = p.Length > 0 ? p[0].Trim() : "";
            mi.DnsSecondary = p.Length > 1 ? p[1].Trim() : "";
            mi.DnsFromLan = false;
        }
    }

    private static void ParseDns(ModemInfo mi, string html)
    {
        // new dhcpmainst(domain, enable, start, end, lease, l2relay, ..., MainDNS(16),
        //                Option125(17), DNSServers(18), OptionEnable(19))
        List<string> a = SplitArgs(CallArgs(html, "dhcpmainst"));
        if (a.Count < 19) return;
        string raw = At(a, 16);
        if (raw.Length == 0) raw = At(a, 18);
        if (raw.Length == 0) return;   // LAN tarafı boş → WAN DNS geçerli kalır

        string[] p = raw.Split(',');
        mi.DnsPrimary   = p.Length > 0 ? p[0].Trim() : "";
        mi.DnsSecondary = p.Length > 1 ? p[1].Trim() : "";
        mi.DnsFromLan = true;
    }

    private static void ParseWifi(ModemInfo mi, string html)
    {
        // new stWlanWifi(domain, name, enable, ssid, mode, channel, power, country, autoCh, width)
        foreach (string args in AllCallArgs(html, "stWlanWifi"))
        {
            List<string> a = SplitArgs(args);
            if (a.Count < 5) continue;
            WifiBand b = new WifiBand
            {
                Domain    = At(a, 0),
                Enabled   = At(a, 2) == "1",
                Ssid      = At(a, 3),
                Standard  = At(a, 4),
                Channel   = At(a, 5)
            };
            b.Instance = InstanceBul(b.Domain);
            b.Band = (b.Instance == "5") ? "5G" : "2.4G";
            if (b.Ssid.Length > 0) mi.Bands.Add(b);
        }

        // new stWlan(domain, name, enable, ssid, wlHide(4), ..., BeaconType(7), ..., X_HW_WPAand11iEncryptionModes(16), ...)
        foreach (string args in AllCallArgs(html, "stWlan"))
        {
            List<string> a = SplitArgs(args);
            if (a.Count < 17) continue;
            string inst = InstanceBul(At(a, 0));
            WifiBand b = mi.Bands.FirstOrDefault(x => x.Instance == inst);
            if (b == null) { b = new WifiBand { Instance = inst, Band = (inst == "5") ? "5G" : "2.4G" }; mi.Bands.Add(b); }
            // wlHide veri alanı "SSID yayınlanıyor mu" anlamına gelir (1 = yayında/görünür)
            b.Hidden     = At(a, 4) != "1";
            b.Security   = At(a, 7);
            b.Encryption = At(a, 16);
        }

        // new stPreSharedKey("...WLANConfiguration.N.PreSharedKey.1","<parola>","")
        foreach (string args in AllCallArgs(html, "stPreSharedKey"))
        {
            List<string> a = SplitArgs(args);
            if (a.Count < 2) continue;
            string inst = InstanceBul(At(a, 0));
            WifiBand b = mi.Bands.FirstOrDefault(x => x.Instance == inst);
            if (b != null && b.Psk.Length == 0) b.Psk = At(a, 1);
        }
    }

    private static string InstanceBul(string domain)
    {
        Match m = Regex.Match(domain ?? "", @"WLANConfiguration\.(\d+)");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static void ParseFirewall(ModemInfo mi, string html)
    {
        string lvl    = Regex.Match(html, @"var\s+FltsecLevelx\s*=\s*'([^']*)'").Groups[1].Value;
        string tr181  = Regex.Match(html, @"var\s+Tr181FltsecLevelx\s*=\s*'([^']*)'").Groups[1].Value;
        string enable = Regex.Match(html, @"var\s+Tr181FltsecEnable\s*=\s*'([^']*)'").Groups[1].Value;
        string adv    = OntWeb.Unescape(Regex.Match(html, @"var\s+AdvancedLevelValue\s*=\s*'([^']*)'").Groups[1].Value);

        if (enable == "0" || lvl.Equals("Disable", StringComparison.OrdinalIgnoreCase))
        { mi.FirewallLevel = "Kapalı (Disable)"; return; }

        string seviye;
        if (adv.EndsWith(".Level.1", StringComparison.OrdinalIgnoreCase)) seviye = "Düşük (Low)";
        else if (adv.EndsWith(".Level.2", StringComparison.OrdinalIgnoreCase)) seviye = "Yüksek (High)";
        else if (adv.Equals("strict", StringComparison.OrdinalIgnoreCase)) seviye = "Katı (Strict)";
        else if (lvl.Length > 0) seviye = lvl;
        else seviye = tr181;
        mi.FirewallLevel = seviye.Length > 0 ? seviye : "(okunamadı)";
    }

    private static void ParseAlg(ModemInfo mi, string html)
    {
        // new stAlg(domain, FTPEnable(1), TFTPEnable(2), H323Enable(3), PptpEnable(4),
        //           L2TPForward(5), IPSecForward(6), SipEnable(7), RTSPEnable(8), RTCPEnable(9), RTCPPort(10))
        string[] adlar = { "FTP", "TFTP", "H323", "PPTP", "L2TP", "IPSec", "SIP", "RTSP", "RTCP" };
        List<string> a = SplitArgs(CallArgs(html, "stAlg"));
        if (a.Count < 10) return;
        for (int i = 0; i < adlar.Length; i++)
            mi.Alg[adlar[i]] = At(a, i + 1) == "1";
    }

    private static void ParseTr069(ModemInfo mi, string html)
    {
        // new stCWMP(domain, EnableCWMP(1), PeriodicInformEnable(2), Interval(3), Time(4), URL(5), ...)
        List<string> a = SplitArgs(CallArgs(html, "stCWMP"));
        if (a.Count < 6) return;
        mi.CwmpEnabled        = At(a, 1) == "1";
        mi.CwmpPeriodicInform = At(a, 2) == "1";
        mi.AcsUrl             = At(a, 5);
    }
}

public static class ModemInfoWriter
{
    // ---------- DNS ----------

    /// <summary>LAN/DHCP tarafındaki DNS'i değiştirir ve sonucu cihazdan geri okuyarak doğrular.</summary>
    public static async Task<(bool ok, string msg)> SetLanDnsAsync(OntWeb web, string dns1, string dns2, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dns1))
            return (false, "Birincil DNS boş.");

        string liste = string.IsNullOrWhiteSpace(dns2) ? dns1.Trim() : dns1.Trim() + "," + dns2.Trim();
        string tok = await web.TokenAsync(ct);
        if (tok.Length == 0) return (false, "Cihaz token'ı alınamadı (oturum düşmüş olabilir).");

        // Cihazın kendi dhcp2.asp sayfasının kullandığı yol:
        //   set.cgi?z=InternetGatewayDevice.LANDevice.1.LANHostConfigManagement
        string body = "z.X_HW_DNSList=" + liste + "&x.X_HW_Token=" + Uri.EscapeDataString(tok);
        await web.PostFormAsync(
            "set.cgi?z=InternetGatewayDevice.LANDevice.1.LANHostConfigManagement&RequestFile=html/bbsp/dhcpservercfg/dhcp2.asp",
            body, web.Base + "/" + ModemInfoReader.PDhcp, ct);

        // Doğrulama: sayfayı geri oku
        await Task.Delay(2000, ct);
        (int st, string page) = await web.GetPageExAsync(ModemInfoReader.PDhcp, ct);
        if (st == 200)
        {
            List<string> a = ModemInfoReader.SplitArgs(ModemInfoReader.CallArgs(page, "dhcpmainst"));
            string raw = a.Count > 16 ? a[16] : "";
            if (raw.Length == 0 && a.Count > 18) raw = a[18];
            if (raw.Replace(" ", "") == liste.Replace(" ", ""))
                return (true, $"✓ Uygulandı ve doğrulandı: {liste}");
        }
        return (false, $"⚠ Gönderildi ama doğrulanamadı — cihaz değeri geri almadı olabilir. İstenen: {liste}");
    }

    /// <summary>WAN (PPPoE) tarafındaki DNS'i değiştirir. Modem domaini gerekir.</summary>
    public static async Task<(bool ok, string msg)> SetWanDnsAsync(OntWeb web, string wanDomain, string dns1, string dns2, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(wanDomain)) return (false, "WAN arayüzü bulunamadı.");
        string liste = string.IsNullOrWhiteSpace(dns2) ? dns1.Trim() : dns1.Trim() + "," + dns2.Trim();
        string tok = await web.TokenAsync(ct);
        if (tok.Length == 0) return (false, "Cihaz token'ı alınamadı.");

        string body = "x.DNSServers=" + liste + "&x.X_HW_Token=" + Uri.EscapeDataString(tok);
        await web.PostFormAsync(
            "set.cgi?x=" + wanDomain + "&RequestFile=html/bbsp/common/wan_list_info.asp",
            body, web.Base + "/html/bbsp/waninfo/waninfo.asp", ct);

        await Task.Delay(2000, ct);
        (int st, string page) = await web.GetPageExAsync(ModemInfoReader.PWanCache, ct);
        if (st == 200 && page.Contains(liste.Replace(" ", "")))
            return (true, $"✓ WAN DNS uygulandı: {liste}");
        return (false, $"⚠ WAN DNS doğrulanamadı — istenen: {liste}");
    }

    // ---------- WiFi ----------

    /// <summary>Tek bandın SSID'sini ve görünürlüğünü ayarlar ve doğrular.</summary>
    public static async Task<(bool ok, string msg)> SetSsidAsync(OntWeb web, WifiBand band, string ssid, bool visible, string psk, CancellationToken ct)
    {
        if (band == null || band.Domain.Length == 0) return (false, "Bant bilgisi yok.");
        string tok = await web.TokenAsync(ct);
        if (tok.Length == 0) return (false, "Cihaz token'ı alınamadı.");

        // Cihazın kendi sayfası: set.cgi?y=<wlanDomain>&k=<wlanDomain>.PreSharedKey.1
        string url = "set.cgi?y=" + band.Domain;
        string body =
            "y.Enable=1" +
            "&y.SSID=" + Uri.EscapeDataString(ssid) +
            "&y.SSIDAdvertisementEnabled=" + (visible ? "1" : "0");
        if (!string.IsNullOrEmpty(psk))
        {
            url += "&k=" + band.Domain + ".PreSharedKey.1";
            body += "&k.PreSharedKey=" + Uri.EscapeDataString(psk);
        }
        body += "&x.X_HW_Token=" + Uri.EscapeDataString(tok);

        await web.PostFormAsync(
            url + "&RequestFile=" + ModemInfoReader.PWlan,
            body, web.Base + "/" + ModemInfoReader.PWlan, ct);

        await Task.Delay(2500, ct);
        (int st, string page) = await web.GetPageExAsync(ModemInfoReader.PWlan, ct);
        if (st == 200)
        {
            // Cihaz özel karakterleri \x5f gibi kaçışla yayınlar; her iki biçimi de kontrol et.
            string aranan = ssid.Replace("_", "\\x5f").Replace(".", "\\x2e");
            bool ssidOk = page.Contains(aranan) || page.Contains(ssid);
            bool pskOk = string.IsNullOrEmpty(psk);
            if (!pskOk)
            {
                string pk = psk.Replace("_", "\\x5f").Replace(".", "\\x2e").Replace("@", "\\x40").Replace(":", "\\x3a");
                pskOk = page.Contains(pk) || page.Contains(psk);
            }
            if (ssidOk && pskOk)
                return (true, $"✓ {band.Band}: SSID='{ssid}', görünür={(visible ? "evet" : "hayır")}{(string.IsNullOrEmpty(psk) ? "" : ", parola uygulandı")}");
        }
        return (false, $"⚠ {band.Band}: istek gönderildi ama doğrulanamadı (SSID='{ssid}')");
    }

    /// <summary>2.4G ve 5G'yi ayrı SSID yapar; 5G'ye _5G eki ekler ve ikisini de görünür yapar.</summary>
    public static async Task<(bool ok, string msg)> EnableDualSsidAsync(OntWeb web, ModemInfo mi, CancellationToken ct)
    {
        if (mi == null || mi.Bands.Count < 2) return (false, "İki bant bulunamadı — WiFi sayfası okunamadı.");

        WifiBand b24 = mi.Bands.FirstOrDefault(b => b.Band == "2.4G");
        WifiBand b5  = mi.Bands.FirstOrDefault(b => b.Band == "5G");
        if (b24 == null || b5 == null) return (false, "2.4G/5G bantları ayırt edilemedi.");

        string temel = b24.Ssid.EndsWith("_5G", StringComparison.OrdinalIgnoreCase)
                     ? b24.Ssid.Substring(0, b24.Ssid.Length - 3) : b24.Ssid;
        if (temel.Length == 0) return (false, "2.4G SSID okunamadı.");

        (bool ok24, string m24) = await SetSsidAsync(web, b24, temel, true, null, ct);
        (bool ok5,  string m5)  = await SetSsidAsync(web, b5,  temel + "_5G", true, null, ct);
        return (ok24 && ok5, $"2.4G='{temel}' {(ok24 ? "✓" : "✗")} | 5G='{temel}_5G' {(ok5 ? "✓" : "✗")}");
    }

    // ---------- ALG ----------

    /// <summary>ALG imzalarını toplu yazar ve doğrular. Sözlükte olmayan anahtarlar mevcut değerinde bırakılır.</summary>
    public static async Task<(bool ok, string msg)> SetAlgAsync(OntWeb web, Dictionary<string, bool> istenen, CancellationToken ct)
    {
        if (istenen == null || istenen.Count == 0) return (false, "ALG verisi yok.");

        (int st, string sayfa) = await web.GetPageExAsync(ModemInfoReader.PAlg, ct);
        if (st != 200) return (false, $"ALG sayfası açılamadı (HTTP {st})");

        // Cihazın kendi alan adları:
        //   x.FtpEnable, x.TftpEnable, x.H323Enable, x.SipEnable, x.RTSPEnable,
        //   x.PptpEnable, x.L2TPForward, x.IPSecForward, x.RTCPEnable, x.RTCPPort
        List<string> mevcut = ModemInfoReader.SplitArgs(ModemInfoReader.CallArgs(sayfa, "stAlg"));
        if (mevcut.Count < 10) return (false, "ALG değerleri okunamadı.");

        var alanlar = new (string Ad, string Alan, int Sira)[]
        {
            ("FTP",   "x.FtpEnable",    1),
            ("TFTP",  "x.TftpEnable",   2),
            ("H323",  "x.H323Enable",   3),
            ("SIP",   "x.SipEnable",    7),
            ("RTSP",  "x.RTSPEnable",   8),
            ("PPTP",  "x.PptpEnable",   4),
            ("L2TP",  "x.L2TPForward",  5),
            ("IPSec", "x.IPSecForward", 6),
            ("RTCP",  "x.RTCPEnable",   9),
        };

        var parcalar = new List<string>();
        var ozet = new List<string>();
        foreach (var f in alanlar)
        {
            bool deger = istenen.TryGetValue(f.Ad, out bool v) ? v : mevcut[f.Sira] == "1";
            parcalar.Add(f.Alan + "=" + (deger ? "1" : "0"));
            ozet.Add($"{f.Ad}:{(deger ? "1" : "0")}");
        }
        parcalar.Add("x.RTCPPort=" + (mevcut.Count > 10 && mevcut[10].Length > 0 ? mevcut[10] : "0"));

        string tok = await web.TokenAsync(ct);
        if (tok.Length == 0) return (false, "Cihaz token'ı alınamadı.");
        parcalar.Add("x.X_HW_Token=" + Uri.EscapeDataString(tok));

        await web.PostFormAsync(
            "set.cgi?x=InternetGatewayDevice.X_HW_ALG&RequestFile=" + ModemInfoReader.PAlg,
            string.Join("&", parcalar), web.Base + "/" + ModemInfoReader.PAlg, ct);

        await Task.Delay(2000, ct);

        // Doğrulama
        (int st2, string sonra) = await web.GetPageExAsync(ModemInfoReader.PAlg, ct);
        if (st2 == 200)
        {
            List<string> s = ModemInfoReader.SplitArgs(ModemInfoReader.CallArgs(sonra, "stAlg"));
            bool tamam = true;
            foreach (var f in alanlar)
            {
                bool istek = istenen.TryGetValue(f.Ad, out bool v) ? v : mevcut[f.Sira] == "1";
                if (s.Count > f.Sira && (s[f.Sira] == "1") != istek) { tamam = false; break; }
            }
            return (tamam, (tamam ? "✓ Uygulandı ve doğrulandı — " : "⚠ Doğrulanamadı — ") + string.Join(" ", ozet));
        }
        return (false, "⚠ Gönderildi ama durum okunamadı.");
    }

    // ---------- TR-069 ----------

    /// <summary>TR-069 (CWMP) uzaktan yönetimini kapatır ve sonucu cihazdan doğrular.</summary>
    public static async Task<(bool ok, string msg)> SetTr069Async(OntWeb web, bool cwmpAcik, bool periyodikAcik, CancellationToken ct)
    {
        string tok = await web.TokenAsync(ct);
        if (tok.Length == 0) return (false, "Cihaz token'ı alınamadı.");

        // Cihazın kendi sayfası: set.cgi?x=InternetGatewayDevice.ManagementServer
        string body =
            "x.EnableCWMP=" + (cwmpAcik ? "1" : "0") +
            "&x.PeriodicInformEnable=" + (periyodikAcik ? "1" : "0") +
            "&x.X_HW_Token=" + Uri.EscapeDataString(tok);

        await web.PostFormAsync(
            "set.cgi?x=InternetGatewayDevice.ManagementServer&RequestFile=" + ModemInfoReader.PTr069,
            body, web.Base + "/" + ModemInfoReader.PTr069, ct);

        await Task.Delay(2500, ct);

        // Doğrulama: stCWMP(...) ilk alan = EnableCWMP
        (int st, string page) = await web.GetPageExAsync(ModemInfoReader.PTr069, ct);
        if (st == 200)
        {
            List<string> a = ModemInfoReader.SplitArgs(ModemInfoReader.CallArgs(page, "stCWMP"));
            if (a.Count > 2)
            {
                bool cwmp = a[1] == "1", per = a[2] == "1";
                if (cwmp == cwmpAcik && per == periyodikAcik)
                    return (true, $"✓ TR-069: CWMP {(cwmp ? "AÇIK" : "kapalı")}, periyodik bildirim {(per ? "AÇIK" : "kapalı")}");
                return (false, $"⚠ Cihaz değeri koruyor: CWMP {(cwmp ? "AÇIK" : "kapalı")}, periyodik {(per ? "AÇIK" : "kapalı")} (operatör kilidi olabilir)");
            }
        }
        return (false, "⚠ İstek gönderildi ama durum okunamadı.");
    }

    // ---------- Güvenlik duvarı ----------

    /// <summary>Güvenlik duvarı seviyesi: Disable / Low / High / Strict</summary>
    public static async Task<(bool ok, string msg)> SetFirewallLevelAsync(OntWeb web, string seviye, CancellationToken ct)
    {
        string tok = await web.TokenAsync(ct);
        if (tok.Length == 0) return (false, "Cihaz token'ı alınamadı.");

        string enable, adv;
        switch ((seviye ?? "").ToLowerInvariant())
        {
            case "disable": enable = "0"; adv = ""; break;
            case "low":     enable = "1"; adv = "InternetGatewayDevice.X_HW_Security.Firewall.Level.1"; break;
            case "high":    enable = "1"; adv = "InternetGatewayDevice.X_HW_Security.Firewall.Level.2"; break;
            case "strict":  enable = "1"; adv = "strict"; break;
            default: return (false, "Bilinmeyen seviye: " + seviye);
        }

        string body = "x.Enable=" + enable + "&x.AdvancedLevel=" + Uri.EscapeDataString(adv) + "&x.X_HW_Token=" + Uri.EscapeDataString(tok);
        await web.PostFormAsync(
            "set.cgi?x=InternetGatewayDevice.X_HW_Security.Firewall&RequestFile=" + ModemInfoReader.PFirewall,
            body, web.Base + "/" + ModemInfoReader.PFirewall, ct);
        await Task.Delay(2000, ct);
        return (true, $"✓ Güvenlik duvarı seviyesi gönderildi: {seviye}");
    }

    // ---------- Yardımcılar ----------
}
