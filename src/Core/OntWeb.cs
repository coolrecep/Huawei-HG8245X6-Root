using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HekzOntRace.Core;

public sealed class OntWeb : IDisposable
{
    public readonly HttpClient Http;
    public readonly string Base;
    private readonly CookieContainer _cookies = new CookieContainer();

    public OntWeb(string ip)
    {
        Base = "http://" + ip;
        HttpClientHandler handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            AllowAutoRedirect = false,
            UseCookies = true
        };
        Http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    private static string Clean(string s) => s.TrimStart('\ufeff').Trim();

    public void SetSidCookie(string sidValue)
    {
        foreach (Cookie cookie in _cookies.GetCookies(new Uri(Base)))
        {
            cookie.Expired = true;
            cookie.Expires = DateTime.Now.AddDays(-1.0);
        }
        _cookies.Add(new Cookie("Cookie", sidValue, "/", new Uri(Base).Host));
    }

    public async Task<(bool OK, string Detay)> HttpLoginAsync(string user, string pass, CancellationToken ct)
    {
        try
        {
            foreach (Cookie cookie in _cookies.GetCookies(new Uri(Base)))
            {
                cookie.Expired = true;
                cookie.Expires = DateTime.Now.AddDays(-1.0);
            }
            string token = Clean(await (await Http.PostAsync(Base + "/asp/GetRandCount.asp", null, ct)).Content.ReadAsStringAsync(ct));
            if (string.IsNullOrWhiteSpace(token))
                return (false, "Token alınamadı");
            _cookies.Add(new Cookie("Cookie", "body:Language:turkish:id=-1", "/", new Uri(Base).Host));
            try { _cookies.Add(new Cookie("CookieHttp", "body:Language:turkish:id=-1", "/", new Uri(Base).Host)); } catch { }
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, Base + "/login.cgi")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["UserName"] = user,
                    ["PassWord"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(pass)),
                    ["Language"] = "turkish",
                    ["x.X_HW_Token"] = token
                })
            };
            req.Headers.Referrer = new Uri(Base + "/");
            req.Headers.Add("Origin", Base);
            HttpResponseMessage resp = await Http.SendAsync(req, ct);
            string text = await resp.Content.ReadAsStringAsync(ct);
            if (_cookies.GetCookies(new Uri(Base)).FirstOrDefault((Cookie c) => c.Value.Contains("sid=")) != null)
                return (true, "OK (sid)");
            if (text.Contains("top.location.replace") || text.Contains("index.asp"))
            {
                string page = await (await Http.GetAsync(Base + "/html/ssmp/deviceinfo/deviceinfo.asp", ct)).Content.ReadAsStringAsync(ct);
                if (!page.Contains("login.asp") && !page.Contains("txt_Username"))
                    return (true, "OK (JS redirect)");
                return (false, "JS redirect alındı ama oturum kurulamadı");
            }
            int statusCode = (int)resp.StatusCode;
            if (statusCode is >= 301 and <= 302 or 307)
                return (true, "OK (HTTP redirect)");
            string input = Regex.Replace(text, "<[^>]*>", " ");
            input = Regex.Replace(input, "\\s+", " ").Trim();
            if (input.Length > 0)
                return (false, "Yanıt: " + input.Substring(0, Math.Min(150, input.Length)));
            return (false, "Bilinmeyen yanıt");
        }
        catch (Exception ex)
        {
            return (false, "Bağlantı hatası: " + ex.Message);
        }
    }

    public async Task<(bool OK, string SidCookie, string Detay)> RawLoginAsync(string user, string pass, CancellationToken ct)
    {
        try
        {
            string token = Clean(await (await Http.PostAsync(Base + "/asp/GetRandCount.asp", null, ct)).Content.ReadAsStringAsync(ct));
            if (string.IsNullOrWhiteSpace(token))
                return (false, "", "Token alınamadı — cihaz web arayüzü açık mı?");
            string host = new Uri(Base).Host;
            int port = 80;
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(pass));
            string body = $"UserName={Uri.EscapeDataString(user)}&PassWord={Uri.EscapeDataString(b64)}&Language=turkish&x.X_HW_Token={Uri.EscapeDataString(token)}";
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            using TcpClient client = new TcpClient(host, port);
            client.ReceiveTimeout = 10000;
            string header = $"POST /login.cgi HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\nContent-Length: {bodyBytes.Length}\r\nCache-Control: max-age=0\r\nAccept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\r\nOrigin: {Base}\r\nUpgrade-Insecure-Requests: 1\r\nUser-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36\r\nAccept-Language: tr-TR,tr;q=0.9\r\nReferer: {Base}/\r\nAccept-Encoding: identity\r\nCookie: Cookie=body:Language:turkish:id=-1\r\nContent-Type: application/x-www-form-urlencoded\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(headerBytes, ct);
            await stream.WriteAsync(bodyBytes, ct);
            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[8192];
            try
            {
                await Task.WhenAny(Task.Run(async delegate
                {
                    int n;
                    while ((n = await stream.ReadAsync(buf, ct)) > 0)
                        ms.Write(buf, 0, n);
                }, ct), Task.Delay(10000, ct));
            }
            catch { }
            string text2 = Encoding.UTF8.GetString(ms.ToArray());
            if (text2.Length == 0)
                return (false, "", "Sunucu yanıt vermedi (10sn timeout) — modem hangi IP'de?");
            Match match = Regex.Match(text2, "Set-Cookie:\\s*Cookie=(sid=[^;\\r\\n]+)");
            if (match.Success)
                return (true, match.Groups[1].Value, "OK");
            string statusLine = text2.Split('\r')[0];
            if (text2.Contains("top.location.replace") || text2.Contains("index.asp"))
            {
                Match m2 = Regex.Match(text2, "Set-Cookie:[^\\r\\n]*sid=([^;\\r\\n]+)");
                if (m2.Success)
                    return (true, "sid=" + m2.Groups[1].Value, "OK (JS redirect + sid)");
                return (true, "", "OK (JS redirect)");
            }
            if (statusLine.Contains("302") || statusLine.Contains("301"))
                return (true, "", "OK (redirect: " + statusLine + ")");
            int idx = text2.IndexOf("\r\n\r\n");
            string input = (idx < 0) ? text2 : text2.Substring(idx + 4, text2.Length - idx - 4);
            string input2 = Regex.Replace(Regex.Replace(input, "<[^>]*>", " "), "\\s+", " ").Trim();
            return (false, "", "[" + statusLine + "] " + input2.Substring(0, Math.Min(150, input2.Length)));
        }
        catch (Exception ex)
        {
            return (false, "", "Bağlantı hatası: " + ex.Message);
        }
    }

    public async Task<bool> LoginAsync(string user, string pass, CancellationToken ct)
    {
        return (await HttpLoginAsync(user, pass, ct)).Item1;
    }

    /// <summary>
    /// Cihazın kullandığı gerçek token ucu: /html/ssmp/common/GetRandToken.asp
    /// (Türkçe web arayüzünde doğrulandı; yanıt 64 karakterlik hex'tir.)
    /// </summary>
    public async Task<string> TokenAsync(CancellationToken ct)
    {
        try
        {
            HttpResponseMessage r = await Http.GetAsync(Base + "/html/ssmp/common/GetRandToken.asp", ct);
            if (r.IsSuccessStatusCode)
            {
                string t = Clean(await r.Content.ReadAsStringAsync(ct));
                if (t.Length > 0 && !t.StartsWith("<") && t.Length <= 128) return t;
            }
        }
        catch { }
        return "";
    }

    public async Task<string> GetPageAsync(string path, CancellationToken ct)
    {
        return await (await Http.GetAsync(Base + "/" + path.TrimStart('/'), ct)).Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Sayfayı durum koduyla birlikte döndürür (hata fırlatmaz).
    /// Sekmelerin neden boş kaldığını logda gösterebilmek için gerekli:
    /// bu cihaz yetkisiz/mesnet olmayan isteklerde 404 ya da 403 döner.
    /// </summary>
    public async Task<(int Status, string Body)> GetPageExAsync(string path, CancellationToken ct)
    {
        try
        {
            HttpResponseMessage r = await Http.GetAsync(Base + "/" + path.TrimStart('/'), ct);
            string body = await r.Content.ReadAsStringAsync(ct);
            return ((int)r.StatusCode, body);
        }
        catch (Exception ex)
        {
            return (-1, "[istek hatası] " + ex.Message);
        }
    }

    /// <summary>Oturumun hâlâ geçerli olup olmadığını kontrol eder.</summary>
    public async Task<bool> SessionAliveAsync(CancellationToken ct)
    {
        (int st, string body) = await GetPageExAsync("html/ssmp/deviceinfo/deviceinfo.asp", ct);
        return st == 200 && body.Length > 100;
    }

    /// <summary>Cihaz web arayüzüne başarıyla giriş yapıldı mı (sid çerezi var mı)?</summary>
    public bool HasSession()
    {
        try
        {
            foreach (Cookie c in _cookies.GetCookies(new Uri(Base)))
                if (c.Value.Contains("sid=") || c.Name == "CookieHttp") return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Cihazın "tüm ayarları kaydet" dosyasını indirir (cfgfiledown.cgi).
    /// Yanıtın configuration dosyası olup olmadığını basitçe denetler; hata sayfası dönerse null.
    /// </summary>
    public async Task<byte[]> DownloadConfigBackupAsync(CancellationToken ct)
    {
        try
        {
            await Http.GetAsync(Base + "/html/ssmp/common/StartFileLoad.asp", ct);
        }
        catch { }

        string tok = await TokenAsync(ct);
        if (tok.Length == 0) return null;

        var req = new HttpRequestMessage(HttpMethod.Post, Base + "/cfgfiledown.cgi?&RequestFile=html/ssmp/cfgfile/cfgfile.asp")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["x.X_HW_Token"] = tok
            })
        };
        req.Headers.Referrer = new Uri(Base + "/html/ssmp/cfgfile/cfgfile.asp");
        HttpResponseMessage resp = await Http.SendAsync(req, ct);
        byte[] data = await resp.Content.ReadAsByteArrayAsync(ct);

        // Hata sayfası / boş yanıt kontrolü: Huawei yapılandırma dosyaları ikilidir ve "DOCTYPE" ile başlamaz.
        if (data.Length < 512) return null;
        string bas = Encoding.ASCII.GetString(data, 0, Math.Min(256, data.Length));
        if (bas.Contains("DOCTYPE") || bas.Contains("<html")) return null;
        return data;
    }

    /// <summary>
    /// Kaydedilmiş yapılandırma dosyasını cihaza geri yükler (cfgfileupload.cgi, multipart).
    /// Dönen durum: başarılıysa true. Cihaz yanıtını da döndürür.
    /// </summary>
    public async Task<(bool ok, string detay)> UploadConfigRestoreAsync(byte[] dosya, string dosyaAdi, CancellationToken ct)
    {
        try
        {
            string tok = await TokenAsync(ct);

            var boundary = "----HekzBoundary" + DateTime.Now.Ticks.ToString("x");
            var sb = new StringBuilder();
            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Disposition: form-data; name=\"onttoken\"\r\n\r\n");
            sb.Append(tok).Append("\r\n");
            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Disposition: form-data; name=\"browse\"; filename=").Append('"').Append(dosyaAdi).Append('"').Append("\r\n");
            sb.Append("Content-Type: application/octet-stream\r\n\r\n");
            byte[] head = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");

            var ms = new MemoryStream();
            ms.Write(head, 0, head.Length);
            ms.Write(dosya, 0, dosya.Length);
            ms.Write(tail, 0, tail.Length);

            var req = new HttpRequestMessage(HttpMethod.Post, Base + "/cfgfileupload.cgi?RequestFile=html/ssmp/reset/reset.asp&FileType=config")
            {
                Content = new ByteArrayContent(ms.ToArray())
            };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/form-data") { Parameters = { new System.Net.Http.Headers.NameValueHeaderValue("boundary", boundary) } };
            req.Headers.Referrer = new Uri(Base + "/html/ssmp/cfgfile/cfgfile.asp");

            HttpResponseMessage resp = await Http.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            bool gorunum = body.Contains("reset") || body.Contains("success") || body.Contains("ok", StringComparison.OrdinalIgnoreCase);
            return (((int)resp.StatusCode is >= 200 and < 400), $"HTTP {(int)resp.StatusCode} — {body.Replace('\n', ' ').Trim().Substring(0, Math.Min(160, body.Trim().Length))}");
        }
        catch (Exception ex)
        {
            return (false, "Hata: " + ex.Message);
        }
    }

    public async Task<(int Status, string Body)> PostFormAsync(string path, string body, string referer, CancellationToken ct)
    {
        HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, Base + "/" + path.TrimStart('/'))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        req.Headers.Referrer = new Uri(referer);
        HttpResponseMessage resp = await Http.SendAsync(req, ct);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
    }

    public static string Unescape(string s)
    {
        return Regex.Replace(s, "\\\\x([0-9a-fA-F]{2})", (Match m) => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
    }

    public void Dispose()
    {
        Http.Dispose();
    }
}
