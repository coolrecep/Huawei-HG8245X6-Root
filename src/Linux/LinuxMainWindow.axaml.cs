using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HekzOntRace.Core;

namespace HekzOntRace.Linux;

public partial class LinuxMainWindow : Window
{
    private CancellationTokenSource _cts;
    private string _aktifKullanici = "";
    private string _sUserPass = "";
    private string _engineCfgXml = "";
    private string _blobSsid = "";
    private string _blobWifiSifre = "";
    private string _ip = "192.168.1.1";
    private ModemInfo _info;
    private LinuxLogYonetici _log;
    private DispatcherTimer _liteWaitTimer;
    private int _liteDotCount = 0;
    private bool _marqueeActive = false;

    private const string VarsayilanSifre = "superonline";
    private const string MaskeMetni = "••••••••••";
    private readonly Dictionary<TextBox, string> _gizliDegerler = new();
    private readonly HashSet<string> _acikAlanlar = new();

    private static readonly IBrush BrushFgDim    = new SolidColorBrush(Color.Parse("#8B93A7"));
    private static readonly IBrush BrushAccentHi = new SolidColorBrush(Color.Parse("#4A97EC"));
    private static readonly IBrush BrushWarn     = new SolidColorBrush(Color.Parse("#E8B44C"));
    private static readonly IBrush BrushOk       = new SolidColorBrush(Color.Parse("#3FB97A"));

    private BackupKapsam KapsamOku() => new BackupKapsam
    {
        Wifi        = CbWifi.IsChecked == true,
        Dns         = CbDns.IsChecked == true,
        Tr069       = CbTr069.IsChecked == true,
        GuvenlikAlg = CbGuvenlik.IsChecked == true,
        WanBilgi    = CbWan.IsChecked == true,
        CihazBilgi  = CbCihaz.IsChecked == true
    };

    public LinuxMainWindow()
    {
        InitializeComponent();
        _log = new LinuxLogYonetici(LogPanel, LogScroll, LogFiltre, LogOtoKaydir, LogZaman, () => LogOzet, this);
        LicenseTextBox.Text = LicenseInfo.GnuGplV2;
        InitAppMode();

        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateWindowStateVisuals();
            }
        };

        string path = Path.Combine(Path.GetTempPath(), "hekz_kurulum_ok");
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
            Dispatcher.UIThread.Post(() =>
            {
                LogLine("✓ Gereksinimler başarıyla kuruldu — BAŞLAT'a basabilirsin!");
                Phase("Hazır — gereksinimler tamam ✓");
            });
        }
    }

    private void SetSecretText(TextBox box, string value)
    {
        string v = value ?? "";
        _gizliDegerler[box] = v;
        if (string.IsNullOrEmpty(v))
        {
            box.Text = "";
            return;
        }
        box.Text = _acikAlanlar.Contains(box.Name ?? "") ? v : MaskeMetni;
    }

    private string GetSecretText(TextBox box)
    {
        return _gizliDegerler.TryGetValue(box, out var v) ? v : (box.Text ?? "");
    }

    private void ToggleSecret_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        bool simdiAcik = _acikAlanlar.Contains(tag);
        if (simdiAcik) _acikAlanlar.Remove(tag);
        else _acikAlanlar.Add(tag);
        bool yeniAcik = !simdiAcik;

        if (btn.Content is Viewbox vb && vb.Child is Avalonia.Controls.Shapes.Path path)
        {
            path.Fill = yeniAcik ? BrushAccentHi : BrushFgDim;
        }

        if (tag == "InputPass")
        {
            InputPass.PasswordChar = yeniAcik ? '\0' : '•';
        }
        else if (tag == "InputNewSuserPass")
        {
            InputNewSuserPass.PasswordChar = yeniAcik ? '\0' : '•';
        }
        else
        {
            TextBox hedef = tag switch
            {
                "ResWifi" => ResWifi,
                "ResAdmin" => ResAdmin,
                "ResSuser" => ResSuser,
                "ResPppoePass" => ResPppoePass,
                _ => null
            };
            if (hedef != null)
            {
                SetSecretText(hedef, GetSecretText(hedef));
            }
        }
    }

    private void TitleBar_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void TitleBar_DoubleTapped(object sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateWindowStateVisuals()
    {
        if (WindowState == WindowState.Maximized)
        {
            RootWindowBorder.BorderThickness = new Thickness(0);
            ToolTip.SetTip(MaxRestoreBtn, "Önceki Boyut");
            MaxRestoreIcon.Data = StreamGeometry.Parse("M 2.5,0.5 L 9.5,0.5 L 9.5,7.5 M 0.5,2.5 L 7.5,2.5 L 7.5,9.5 L 0.5,9.5 Z");
        }
        else
        {
            RootWindowBorder.BorderThickness = new Thickness(1);
            ToolTip.SetTip(MaxRestoreBtn, "Ekranı Kapla");
            MaxRestoreIcon.Data = StreamGeometry.Parse("M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z");
        }
    }

    private void InitAppMode()
    {
        Title = AppConfig.AppTitle;
        if (AppConfig.IsLite)
        {
            AppNameText.Text = "HekzOntRace Lite";
            TitleBarMainText.Text = "HekzOntRace Lite v2.0.0";
            TitleBarSubText.Text = "Huawei LG8245X6-50 / HG8245X6 Kolay Bilgi ve Şifre Aracı (Linux)";
            LiteBadge.IsVisible = true;
            AboutToolTitle.Text = "Huawei LG8245X6-50 / HG8245X6 Kolay Bilgi ve Şifre Aracı (Lite)";
            AboutVersionBadge.Text = "v2.0.0 Lite Açık Kaynak";
            LiteLegalNote.IsVisible = true;

            SuserYazBtn.IsVisible = false;
            PaketTestiCheck.IsVisible = false;
            TelnetSshCheck.IsVisible = false;

            SuserBox.Header = "Yönetici Parolası Güncelleme (sUser)";
            SuserBoxDesc.Text = "Cihazın fabrika 'sUser' yönetici şifresi yukarıdaki kutuda verilmiştir. Dilerseniz bu alandan sUser hesabına yeni bir parola belirleyip güvenli şekilde modeme kaydedebilirsiniz.";
            SuserYazWebBtn.Content = "⚡ Yeni sUser Parolasını Güvenle Kaydet";
        }
    }

    private async void CopyAllInfo_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Huawei LG8245X6-50 / HG8245X6 Cihaz ve Erişim Bilgileri ===");
        sb.AppendLine($"Modem Arayüzü     : http://{_ip}");
        sb.AppendLine($"Standart Kullanıcı: admin / {VarsayilanSifre}");
        sb.AppendLine($"sUser Yönetici    : {GetSecretText(ResSuser)}");
        sb.AppendLine($"PPPoE Kullanıcı   : {ResPppoeUser.Text}");
        sb.AppendLine($"PPPoE Parolası    : {GetSecretText(ResPppoePass)}");
        sb.AppendLine($"Wi-Fi Adı (SSID)  : {ResSsid.Text}");
        sb.AppendLine($"Wi-Fi Parolası    : {GetSecretText(ResWifi)}");
        sb.AppendLine("==================================================");

        if (Clipboard != null)
            await Clipboard.SetTextAsync(sb.ToString());
        Phase("Tüm erişim ve şifre bilgileri panoya kopyalandı ✓");
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        string url = "http://" + (string.IsNullOrWhiteSpace(_ip) ? "192.168.1.1" : _ip);
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false, CreateNoWindow = true });
            Phase("Modem yönetim sayfası tarayıcıda açıldı: " + url);
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Phase("Modem yönetim sayfası tarayıcıda açıldı: " + url);
            }
            catch (Exception ex)
            {
                Phase("Tarayıcı açılamadı: " + ex.Message);
            }
        }
    }

    private async void CopyLicense_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard != null)
            await Clipboard.SetTextAsync(LicenseInfo.GnuGplV2);
        Phase("GNU GPL v2 lisans metni panoya kopyalandı ✓");
    }

    private void LogLine(string m)
    {
        if (AppConfig.IsLite)
        {
            m = LiteLogUyarlayici.Uyarlayici(m);
            if (string.IsNullOrEmpty(m)) return;
        }
        Dispatcher.UIThread.Post(() => _log.Ekle(m));
    }

    private void StartLiteAnimation()
    {
        if (!AppConfig.IsLite) return;
        _liteDotCount = 0;
        PhaseText.Text = "Lütfen bekleyiniz...";
        KurulumBar.IsVisible = true;
        if (_liteWaitTimer == null)
        {
            _liteWaitTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(450)
            };
            _liteWaitTimer.Tick += (_, _) =>
            {
                _liteDotCount = (_liteDotCount % 4) + 1;
                PhaseText.Text = "Lütfen bekleyiniz" + new string('.', _liteDotCount);
            };
        }
        _liteWaitTimer.Start();
    }

    private void StopLiteAnimation(string finalMessage = null)
    {
        if (!AppConfig.IsLite) return;
        if (_liteWaitTimer != null && _liteWaitTimer.IsEnabled)
            _liteWaitTimer.Stop();
        KurulumBar.IsVisible = false;
        if (!string.IsNullOrEmpty(finalMessage))
            PhaseText.Text = finalMessage;
    }

    public void StartMarquee(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_liteWaitTimer != null && _liteWaitTimer.IsEnabled) _liteWaitTimer.Stop();
            _marqueeActive = true;
            PhaseText.Text = message;
            KurulumBar.IsVisible = true;
        });
    }

    public void StopMarquee(string finalMessage = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _marqueeActive = false;
            KurulumBar.IsVisible = false;
            if (!string.IsNullOrEmpty(finalMessage))
                PhaseText.Text = finalMessage;
        });
    }

    private async Task<bool> WaitForRebootAndReconnectAsync(string ip, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        try { await Task.Delay(10000, token); }
        catch (OperationCanceledException) { return false; }

        var maxWait = TimeSpan.FromSeconds(180);
        while (sw.Elapsed < maxWait && !token.IsCancellationRequested)
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var connectTask = tcp.ConnectAsync(ip, 80);
                var completed = await Task.WhenAny(connectTask, Task.Delay(2000, token));
                if (completed == connectTask && tcp.Connected)
                {
                    await Task.Delay(2000, token);
                    return true;
                }
            }
            catch { }

            try { await Task.Delay(2500, token); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    private void Phase(string p)
    {
        p = LogMaskeleyici.Temizle(p ?? "");
        Dispatcher.UIThread.Post(() =>
        {
            if (_marqueeActive) return;

            if (AppConfig.IsLite)
            {
                if (_liteWaitTimer != null && _liteWaitTimer.IsEnabled)
                {
                    if (p.Contains("tamamlandı", StringComparison.OrdinalIgnoreCase) || p.Contains("✓"))
                    {
                        StopLiteAnimation("İşlem başarıyla tamamlandı ✓");
                        return;
                    }
                    if (p.Contains("Hata", StringComparison.OrdinalIgnoreCase) || p.Contains("başarısız", StringComparison.OrdinalIgnoreCase))
                    {
                        StopLiteAnimation("İşlem tamamlanamadı — Detaylar için Log sekmesine bakın");
                        return;
                    }
                    if (p.Contains("durduruldu", StringComparison.OrdinalIgnoreCase) || p.Contains("iptal", StringComparison.OrdinalIgnoreCase))
                    {
                        StopLiteAnimation("İşlem durduruldu");
                        return;
                    }
                    return;
                }

                if (!p.Contains("Wi-Fi bağlantısı kuruldu"))
                {
                    if (p.Contains("tamamlandı", StringComparison.OrdinalIgnoreCase) || p.Contains("✓"))
                        p = "İşlem başarıyla tamamlandı ✓";
                    else if (p.Contains("Hata", StringComparison.OrdinalIgnoreCase) || p.Contains("başarısız", StringComparison.OrdinalIgnoreCase))
                        p = "İşlem tamamlanamadı — Detaylar için Log sekmesine bakın";
                }
            }

            PhaseText.Text = p;
        });
    }

    // ---------- Log paneli olayları ----------
    private void LogFiltre_Changed(object sender, SelectionChangedEventArgs e)
    {
        _log?.YenidenCiz();
    }

    private void LogZaman_Click(object sender, RoutedEventArgs e)
    {
        _log?.ZamanDegisti();
    }

    private async void LogKaydet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string yol = _log.Kaydet();
            Phase("Log kaydedildi: " + Path.GetFileName(yol));
            await ShowInfoDialogAsync("HekzOntRace", "Log kaydedildi:\n\n" + yol);
        }
        catch (Exception ex) { Phase("Log kaydedilemedi: " + ex.Message); }
    }

    private async void LogKopyala_Click(object sender, RoutedEventArgs e)
    {
        await _log.KopyalaAsync();
        Phase("Log panoya kopyalandı");
    }

    private void LogTemizle_Click(object sender, RoutedEventArgs e)
    {
        _log.Temizle();
        Phase("Log temizlendi");
    }

    private string ResolveIp()
    {
        var t = (InputIp.Text ?? "").Trim();
        if (string.IsNullOrEmpty(t)) t = "192.168.1.1";
        if (!Regex.IsMatch(t, @"^\d{1,3}(\.\d{1,3}){3}$")) t = "192.168.1.1";
        return t;
    }

    private void SetRunningState(bool running)
    {
        GoBtn.IsVisible = !running;
        GoBtn.IsEnabled = !running;
        StopBtn.IsVisible = running;
        StopBtn.IsEnabled = running;

        if (AppConfig.IsLite)
        {
            if (running) StartLiteAnimation();
            else StopLiteAnimation();
        }
    }

    private async void Go_Click(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        _ip = ResolveIp();
        SetRunningState(true);
        SuserYazBtn.IsEnabled = false;
        SuserBox.IsVisible = false;
        CopyAllBtn.IsEnabled = false;
        _log.Temizle();
        ResSsid.Text = "";
        ResPppoeUser.Text = "";
        SetSecretText(ResWifi, "");
        SetSecretText(ResAdmin, "");
        SetSecretText(ResSuser, "");
        SetSecretText(ResPppoePass, "");
        CfgBtn.IsEnabled = false; RefreshInfoBtn.IsEnabled = false;
        Tr069Btn.IsEnabled = false; DualSsidBtn.IsEnabled = false; DnsApplyBtn.IsEnabled = false;
        LogLine($"=== {AppConfig.AppTitle} — Hedef Cihaz: {_ip} ===");

        string py = await HazirlaPythonOrtamiAsync();
        if (py == null)
        {
            Phase("Gereksinim hatası (Python 3 + paramiko + pysmb gerekli)");
            SetRunningState(false);
            return;
        }

        string user = string.IsNullOrWhiteSpace(InputUser.Text) ? "admin" : InputUser.Text.Trim();
        string pass = string.IsNullOrWhiteSpace(InputPass.Text) ? VarsayilanSifre : InputPass.Text.Trim();

        var engine = new Engine(_ip, pass, LogLine, Phase, _cts.Token)
        {
            PythonPath   = py,
            LoginUser    = user,
            PaketTesti   = PaketTestiCheck.IsChecked == true,
            TelnetSshAc  = TelnetSshCheck.IsChecked == true,
            SuserPass    = _sUserPass,
            HesapTemizle = HesapTemizleCheck.IsChecked == true
        };
        engine.OnPlainText += plain => Dispatcher.UIThread.Post(() => FillResults(plain));

        try
        {
            await Task.Run(() => engine.RunAsync(), _cts.Token);
            var parts = engine.PlainText.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            ResSsid.Text   = parts.Length > 0 ? parts[0] : "";
            string wifiVal = parts.Length > 1 ? parts[1] : "";
            SetSecretText(ResWifi, wifiVal);
            _blobSsid      = ResSsid.Text;
            _blobWifiSifre = wifiVal;
            SetSecretText(ResAdmin, parts.Length > 5 ? "sUser / " + parts[5] : "");
            SetSecretText(ResSuser, engine.CmeiText.Length > 0 ? engine.CmeiText : (parts.Length > 5 ? parts[5] : ""));
            _sUserPass     = parts.Length > 5 ? parts[5] : "";
            _engineCfgXml  = engine.CfgXml ?? "";

            var dir = Path.Combine(Engine.MasaustuDizini(), "HekzONT");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            await File.WriteAllBytesAsync(Path.Combine(dir, $"ont_veri_{stamp}.bin"), engine.EncryptedBlob, _cts.Token);
            await File.WriteAllTextAsync(Path.Combine(dir, $"ont_veri_{stamp}.txt"), engine.PlainText, _cts.Token);
            LogLine("[Depolama] Veri yedekleri kaydedildi: " + dir);

            CfgBtn.IsEnabled            = _sUserPass.Length > 0;
            CopyAllBtn.IsEnabled        = true;
            Tr069Btn.IsEnabled          = true;
            Tr069OffBtn.IsEnabled       = true;
            RefreshInfoBtn.IsEnabled    = true;
            FwApplyBtn.IsEnabled        = true;
            BackupProfileBtn.IsEnabled  = true;
            BackupDeviceBtn.IsEnabled   = true;
            RestoreProfileBtn.IsEnabled = true;
            RestoreDeviceBtn.IsEnabled  = true;
            SuserBox.IsVisible          = true;

            if (_engineCfgXml.Length >= 1000) CfgHesapDogrulaYaz(_engineCfgXml);

            Phase("Cihaz bilgileri ve modül parametreleri okunuyor...");
            await FetchAndFillInfoAsync();
            Phase("İşlem başarıyla tamamlandı ✓ — Detaylar ilgili sekmelerde");
        }
        catch (OperationCanceledException) { Phase("İşlem kullanıcı tarafından durduruldu"); }
        catch (Exception ex)
        {
            LogLine($"[{DateTime.Now:HH:mm:ss}] HATA: {ex.Message}");
            if (ex.InnerException != null) LogLine($"[{DateTime.Now:HH:mm:ss}] Detay: {ex.InnerException.Message}");
            Phase("İşlem başarısız — Detaylar için Log sekmesine bakın");
        }
        finally { SetRunningState(false); SuserYazBtn.IsEnabled = true; }
    }

    private void InputNewSuserPass_TextChanged(object sender, TextChangedEventArgs e)
    {
        string txt = InputNewSuserPass.Text ?? "";
        if (string.IsNullOrWhiteSpace(txt))
        {
            SuserPassStatusHint.Text = "ℹ️ Boş bırakılırsa otomatik rastgele şifre atanır";
            SuserPassStatusHint.Foreground = BrushFgDim;
            return;
        }

        var (ok, err, _) = Engine.DogrulaYeniSuserSifresi(txt);
        if (!ok)
        {
            SuserPassStatusHint.Text = "⚠️ " + err.Replace("\n", " ");
            SuserPassStatusHint.Foreground = BrushWarn;
        }
        else
        {
            SuserPassStatusHint.Text = $"✓ Geçerli parola formatı ({txt.Trim().Length} karakter)";
            SuserPassStatusHint.Foreground = BrushOk;
        }
    }

    private void RndSuserPass_Click(object sender, RoutedEventArgs e)
    {
        InputNewSuserPass.Text = "Hekz" + Engine.RndDigits(8);
    }

    private async void SuserYazWeb_Click(object sender, RoutedEventArgs e)
    {
        if (_sUserPass.Length == 0)
        {
            LogLine("[Web Enjeksiyonu] sUser parolası henüz okunmadı — önce 'BAŞLAT' ile konfigürasyonu okuyun.");
            Phase("Yönetici (sUser) oturumu gerekli");
            return;
        }

        var (gecerli, hata, yeniSifre) = Engine.DogrulaYeniSuserSifresi(InputNewSuserPass.Text);
        if (!gecerli)
        {
            await ShowInfoDialogAsync("Geçersiz sUser Şifresi", hata);
            LogLine("! [Şifre Doğrulama] " + hata.Replace("\n", " "));
            Phase("Geçersiz şifre parametresi");
            return;
        }

        SuserYazWebBtn.IsEnabled = false; SuserYazBtn.IsEnabled = false; SetRunningState(true);
        _cts = new CancellationTokenSource();
        _ip = ResolveIp();
        LogLine($"=== Web Konfigürasyon Enjeksiyonu (Önerilen) | Hedef: {_ip} ===");
        string user = string.IsNullOrWhiteSpace(InputUser.Text) ? "admin" : InputUser.Text.Trim();
        string pass = string.IsNullOrWhiteSpace(InputPass.Text) ? VarsayilanSifre : InputPass.Text.Trim();
        var engine = new Engine(_ip, pass, LogLine, Phase, _cts.Token)
        { LoginUser = user, TelnetSshAc = TelnetSshCheck.IsChecked == true, SuserPass = _sUserPass, HesapTemizle = false };
        try
        {
            var isP = Task.Run(() => engine.SuserYazWebAsync(_sUserPass, yeniSifre), _cts.Token);
            var tamam = await Task.WhenAny(isP, Task.Delay(TimeSpan.FromMinutes(6), _cts.Token));
            if (tamam != isP)
            {
                LogLine("[Web Enjeksiyonu] 6 dakika içinde tamamlanamadı (Zaman aşımı)");
                Phase("İşlem zaman aşımına uğradı");
            }
            else
            {
                var sifre = await isP;
                if (sifre != null)
                {
                    _sUserPass = sifre;
                    LogMaskeleyici.GizliDegerEkle(sifre);
                    SifreHatirlaYaz(sifre);
                    SetSecretText(ResSuser, "sUser / " + sifre + " (Web enjeksiyonu)");
                    InputNewSuserPass.Text = sifre;
                    LogLine("[Web Enjeksiyonu] ✓ YENİ PAROLA ATANDI: ******** — Cihaz yeniden başladıktan sonra sUser / ******** ile oturum açılabilir.");

                    string kayanMesaj = "⚠️ Modem yeniden başlatılıyor... Wi-Fi ve internet bağlantınız geçici olarak kesilecek. Bağlantı tekrar kurulana kadar lütfen bekleyiniz...";
                    StartMarquee(kayanMesaj);

                    LogLine("[Bağlantı] Modem yeniden başlatılıyor — Wi-Fi ve ağ bağlantısının tekrar kurulması bekleniyor...");
                    bool baglandi = await WaitForRebootAndReconnectAsync(_ip, _cts.Token);
                    StopMarquee();

                    if (baglandi)
                    {
                        LogLine("[Bağlantı] ✓ Wi-Fi ve modem bağlantısı tekrar kuruldu! Yeni sUser şifrenizle giriş yapabilirsiniz.");
                        Phase("✓ Wi-Fi bağlantısı kuruldu ve modem hazır! Yeni sUser şifreniz aktif.");
                    }
                    else
                    {
                        LogLine("[Bağlantı] Süre doldu — modem henüz yanıt vermiyor olabilir. Lütfen modemin ışıklarını kontrol edin.");
                        Phase("Yeni şifre yüklendi — Wi-Fi bağlandığında modemi kullanabilirsiniz.");
                    }
                }
                else
                {
                    LogLine("[Web Enjeksiyonu] ✗ Konfigürasyon yazılamadı — Günlüğü kontrol edin");
                    Phase("Konfigürasyon güncelleme başarısız");
                }
            }
        }
        catch (OperationCanceledException)
        {
            StopMarquee("İşlem durduruldu");
            Phase("İşlem kullanıcı tarafından durduruldu");
        }
        catch (Exception ex)
        {
            StopMarquee();
            LogLine("[Web Enjeksiyonu] Hata: " + ex.Message);
            Phase("Web enjeksiyonu sırasında hata oluştu");
        }
        finally
        {
            StopMarquee();
            SuserYazWebBtn.IsEnabled = true; SuserYazBtn.IsEnabled = true; SetRunningState(false);
        }
    }

    private async void HesapTemizle_Click(object sender, RoutedEventArgs e)
    {
        HesapTemizleBtn.IsEnabled = false; Phase("Geçici servis hesapları ve paylaşımlar temizleniyor...");
        try
        {
            _ip = ResolveIp();
            string pass = string.IsNullOrWhiteSpace(InputPass.Text) ? VarsayilanSifre : InputPass.Text.Trim();
            string user = string.IsNullOrWhiteSpace(InputUser.Text) ? "admin" : InputUser.Text.Trim();
            var engine = new Engine(_ip, pass, LogLine, Phase, CancellationToken.None)
            { LoginUser = user, SuserPass = _sUserPass };
            await Task.Run(() => engine.HesaplariTemizleAsync());
            Phase("Geçici servis hesapları başarıyla temizlendi ✓");
        }
        catch (Exception ex) { LogLine("[Servis Temizliği] Hata: " + ex.Message); Phase("Hesap temizleme hatası"); }
        finally { HesapTemizleBtn.IsEnabled = true; }
    }

    private async void SuserYaz_Click(object sender, RoutedEventArgs e)
    {
        var (gecerli, hata, yeniSifre) = Engine.DogrulaYeniSuserSifresi(InputNewSuserPass.Text);
        if (!gecerli)
        {
            await ShowInfoDialogAsync("Geçersiz sUser Şifresi", hata);
            LogLine("! [Şifre Doğrulama] " + hata.Replace("\n", " "));
            Phase("Geçersiz şifre parametresi");
            return;
        }

        SuserYazBtn.IsEnabled = false; SetRunningState(true);
        _cts = new CancellationTokenSource();
        _ip = ResolveIp();
        LogLine($"=== Donanım / Flash Enjeksiyonu (Race Modu) | Hedef: {_ip} ===");
        string py = await HazirlaPythonOrtamiAsync();
        if (py == null)
        {
            LogLine("✗ Python çalışma ortamı hazır değil.");
            Phase("Python ortamı gerekli");
            SuserYazBtn.IsEnabled = true; SetRunningState(false);
            return;
        }
        string user = string.IsNullOrWhiteSpace(InputUser.Text) ? "admin" : InputUser.Text.Trim();
        string pass = string.IsNullOrWhiteSpace(InputPass.Text) ? VarsayilanSifre : InputPass.Text.Trim();
        var engine = new Engine(_ip, pass, LogLine, Phase, _cts.Token)
        {
            PythonPath   = py,
            LoginUser    = user,
            PaketTesti   = PaketTestiCheck.IsChecked == true,
            TelnetSshAc  = TelnetSshCheck.IsChecked == true,
            SuserPass    = _sUserPass,
            HesapTemizle = HesapTemizleCheck.IsChecked == true
        };
        try
        {
            var isP = Task.Run(() => engine.SuserYazAsync(yeniSifre), _cts.Token);
            var tamam = await Task.WhenAny(isP, Task.Delay(TimeSpan.FromMinutes(12), _cts.Token));
            if (tamam != isP)
            {
                LogLine("[Flash Enjeksiyonu] 12 dakika içinde tamamlanamadı (Zaman aşımı)");
                Phase("İşlem zaman aşımına uğradı");
            }
            else
            {
                var sifre = await isP;
                LogLine(sifre != null
                    ? "[Flash Enjeksiyonu] ✓ YENİ PAROLA ENJEKTE EDİLDİ: ******** — 'FİŞİ ÇEK' uyarısında fiş çekildiyse açılışta sUser / ******** ile girilebilir."
                    : "[Flash Enjeksiyonu] ✗ Flash enjeksiyonu başarısız — Günlüğü kontrol edin");
                if (sifre != null)
                {
                    _sUserPass = sifre;
                    LogMaskeleyici.GizliDegerEkle(sifre);
                    SifreHatirlaYaz(sifre);
                    SetSecretText(ResSuser, "sUser / " + sifre + " (Flash enjeksiyonu)");
                    InputNewSuserPass.Text = sifre;
                }
                Phase(sifre != null ? "Yönetici şifresi başarıyla enjekte edildi ✓" : "Şifre enjeksiyonu başarısız");
            }
        }
        catch (OperationCanceledException) { Phase("İşlem kullanıcı tarafından durduruldu"); }
        catch (Exception ex)
        {
            LogLine("[Flash Enjeksiyonu] Hata: " + ex.Message);
            Phase("Flash enjeksiyonu sırasında hata oluştu");
        }
        finally { SuserYazBtn.IsEnabled = true; SetRunningState(false); }
    }

    private void FillResults(string plain)
    {
        var p = plain.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        ResSsid.Text   = p.Length > 0 ? p[0] : "";
        string wifiVal = p.Length > 1 ? p[1] : "";
        SetSecretText(ResWifi, wifiVal);
        _blobSsid      = ResSsid.Text;
        _blobWifiSifre = wifiVal;
        SetSecretText(ResAdmin, p.Length > 5 ? "sUser / " + p[5] : "");
        if (string.IsNullOrEmpty(GetSecretText(ResSuser))) SetSecretText(ResSuser, p.Length > 5 ? p[5] : "");
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopBtn.IsEnabled = false;
        _cts?.Cancel();
        StopMarquee("İşlem durduruldu");
        StopLiteAnimation("İşlem durduruldu");
        LogLine("Durduruluyor...");
    }

    private async Task<bool> EnsureLoggedInAsync(OntWeb web, CancellationToken ct)
    {
        var denemeler = new List<(string u, string p)>();
        if (_sUserPass.Length > 0) denemeler.Add(("sUser", _sUserPass));
        var u = string.IsNullOrWhiteSpace(InputUser.Text) ? "admin" : InputUser.Text.Trim();
        var p = string.IsNullOrWhiteSpace(InputPass.Text) ? VarsayilanSifre : InputPass.Text.Trim();
        denemeler.Add((u, p));
        if (u != "admin") denemeler.Add(("admin", p));
        if (!denemeler.Any(d => d.p == VarsayilanSifre)) denemeler.Add(("admin", VarsayilanSifre));

        foreach (var (kullanici, sifre) in denemeler)
        {
            if (await web.LoginAsync(kullanici, sifre, ct))
            {
                if (await web.SessionAliveAsync(ct))
                {
                    _aktifKullanici = kullanici;
                    LogLine($"[Oturum] ✓ '{kullanici}' ile giriş yapıldı (yönetici sayfaları açık)");
                    return true;
                }
                LogLine($"[Oturum] '{kullanici}' girişi kabul edildi ama sayfa açılmadı — sıradaki denenecek");
            }
            else LogLine($"[Oturum] '{kullanici}' girişi reddedildi");
        }
        LogLine("[Oturum] Hiçbir hesapla giriş kurulamadı. Modem tarayıcısında açık bir oturum varsa kapatıp tekrar deneyin (cihaz tek oturuma izin verir).");
        return false;
    }

    private async Task FetchAndFillInfoAsync()
    {
        try
        {
            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None)))
            { LogLine("[Info] Oturum açılamadı — bilgiler çekilemedi"); return; }
            _info = await ModemInfoReader.FetchAllAsync(web, CancellationToken.None, LogLine);
            Dispatcher.UIThread.Post(() => RenderInfo(_info));
            LogLine("[Sistem] Tüm cihaz parametreleri ve konfigürasyon modülleri başarıyla okundu ✓");
        }
        catch (Exception ex) { LogLine("! [Sistem] Bilgi okuma hatası: " + ex.Message); }
    }

    private void RenderInfo(ModemInfo mi)
    {
        DevName.Text   = Bos(mi.DeviceName);
        DevSerial.Text = Bos(mi.Serial);
        DevHw.Text     = Bos(mi.HwVersion);
        DevSw.Text     = Bos(mi.SwVersion);
        DevBoot.Text   = Bos(mi.LoaderVersion);
        DevMac.Text    = Bos(mi.Mac);
        DevUptime.Text = Bos(mi.Uptime);

        OptRx.Text       = Bos(mi.OpticalRxPower);
        OptTx.Text       = Bos(mi.OpticalTxPower);
        OptV.Text        = Bos(mi.OpticalVoltage);
        OptT.Text        = Bos(mi.OpticalTemperature);
        OptBias.Text     = Bos(mi.OpticalBias);
        OptPonMode.Text  = mi.PonMode.Length > 0 ? mi.PonMode : Bos("");
        OptOntState.Text = mi.OntDurumu.Length > 0 ? mi.OntDurumu : Bos("");
        OptTest.Text     = mi.OptikTest.Length > 0 ? mi.OptikTest : Bos("");
        OptOlt.Text      = mi.OltEslesme.Length > 0 ? mi.OltEslesme : Bos("");
        OptikNote.Text   = mi.OpticalRxPower.Length > 0 ? "" : "Cihaz bu sürümde optik güç değerini yayınlamıyor (aşağıdaki logu okuyun).";

        WanName.Text      = Bos(mi.WanName);
        WanStatus.Text    = Bos(mi.WanStatus);
        WanIp.Text        = Bos(mi.WanIp);
        WanMtu.Text       = Bos(mi.WanMtu);
        WanPppUser.Text   = Bos(mi.PppoeUser);
        WanPppUptime.Text = Bos(mi.PppoeUptime);

        DnsCur1.Text = Bos(mi.DnsPrimary);
        DnsCur2.Text = Bos(mi.DnsSecondary);
        FwLevel.Text = Bos(mi.FirewallLevel);

        foreach (var esle in new[] {
            ("FTP", CbAlgFTP), ("TFTP", CbAlgTFTP), ("H323", CbAlgH323), ("SIP", CbAlgSIP),
            ("RTSP", CbAlgRTSP), ("PPTP", CbAlgPPTP), ("L2TP", CbAlgL2TP), ("IPSec", CbAlgIPSec),
            ("RTCP", CbAlgRTCP) })
        {
            if (mi.Alg.TryGetValue(esle.Item1, out bool acik))
                esle.Item2.IsChecked = acik;
        }
        AlgNote.Text = mi.Alg.Count == 0
            ? "ALG değerleri okunamadı — sayfa açılamamış olabilir."
            : $"Cihazdan {mi.Alg.Count} ALG imzası okundu; kutular mevcut durumu gösteriyor.";
        AlgApplyBtn.IsEnabled = mi.Alg.Count > 0;
        FwApplyBtn.IsEnabled = true;

        int secIdx = 0;
        if (mi.FirewallLevel.Contains("Kapalı")) secIdx = 0;
        else if (mi.FirewallLevel.Contains("Düşük")) secIdx = 1;
        else if (mi.FirewallLevel.Contains("Orta")) secIdx = 2;
        else if (mi.FirewallLevel.Contains("Yüksek")) secIdx = 3;
        else if (mi.FirewallLevel.Contains("Katı")) secIdx = 4;
        FwLevelSel.SelectedIndex = secIdx;

        Tr069CwmpSel.SelectedIndex = mi.CwmpEnabled ? 1 : 0;
        Tr069PeriodicSel.SelectedIndex = mi.CwmpPeriodicInform ? 1 : 0;

        Tr069Cwmp.Text     = mi.CwmpEnabled ? "AÇIK" : "kapalı";
        Tr069Periodic.Text = mi.CwmpPeriodicInform ? "AÇIK" : "kapalı";
        Tr069Acs.Text      = Bos(mi.AcsUrl);

        WifiBand b24 = mi.Bands.FirstOrDefault(b => b.Band == "2.4G");
        WifiBand b5  = mi.Bands.FirstOrDefault(b => b.Band == "5G");
        if (b24 != null)
        {
            W24Ssid.Text = Bos(b24.Ssid); W24Ch.Text = Bos(b24.Channel);
            W24Std.Text  = Bos(b24.Standard); W24Enc.Text = Bos(b24.Encryption);
            W24Hide.Text = b24.Hidden ? "Evet" : "Hayır";
        }
        if (b5 != null)
        {
            W5Ssid.Text = Bos(b5.Ssid); W5Ch.Text = Bos(b5.Channel);
            W5Std.Text  = Bos(b5.Standard); W5Enc.Text = Bos(b5.Encryption);
            W5Hide.Text = b5.Hidden ? "Evet" : "Hayır";
        }

        string canli24 = b24?.Psk ?? "";
        string canli5  = b5?.Psk ?? "";
        string canliSsid = b24?.Ssid ?? "";
        if (canli24.Length > 0) LogMaskeleyici.GizliDegerEkle(canli24);
        if (canli5.Length > 0) LogMaskeleyici.GizliDegerEkle(canli5);
        if (canliSsid.Length > 0)
        {
            ResSsid.Text = canliSsid + (_blobSsid.Length > 0 && _blobSsid != canliSsid
                ? "  [fabrika: " + _blobSsid + "]" : "");
        }
        if (canli24.Length > 0 || canli5.Length > 0)
        {
            string canli;
            if (canli24.Length > 0 && canli5.Length > 0 && canli24 != canli5)
                canli = "2.4G: " + canli24 + " | 5G: " + canli5;
            else if (canli5.Length > 0 && canli24.Length == 0)
                canli = "5G: " + canli5;
            else
                canli = canli24 + (canli5.Length > 0 ? " (2.4G ve 5G)" : "");
            if (_blobWifiSifre.Length > 0 && canli != _blobWifiSifre)
                canli += "  [fabrika: " + _blobWifiSifre + "]";
            SetSecretText(ResWifi, canli);
        }
        else if (_blobWifiSifre.Length > 0 && GetSecretText(ResWifi) == _blobWifiSifre)
        {
            SetSecretText(ResWifi, _blobWifiSifre + " (fabrika varsayılanı — canlı değer okunamadı)");
        }

        DualSsidBtn.IsEnabled = mi.Bands.Count >= 2;
        DualSsidNote.Text = mi.Bands.Count >= 2
            ? "Her iki bant görünür yapılır + 5G'ye _5G eki eklenir."
            : "Bant bilgisi okunamadı — WiFi sayfası açılamadı.";
        DnsApplyBtn.IsEnabled = true;

        foreach (var n in mi.Notes)
        {
            if (n.Contains("opticInfo") || n.Contains("getOpticTxRx")) continue;
            LogLine("[Bilgi] " + n);
        }

        if (mi.Bands.Count < 2)
            LogLine("[WiFi] WiFi verisi eksik — yalnızca yönetici (sUser) oturumunda okunabilir; standart kullanıcıda bu sayfa kapalıdır.");
        BackupNote.Text = "Yedek klasörü: " + YedekKlasoru();
        if (b24 != null && !string.IsNullOrEmpty(b24.Psk))
            LogLine("[WiFi] 2.4G WiFi parolası: ********");
        if (b5 != null && !string.IsNullOrEmpty(b5.Psk) && b5.Psk != (b24?.Psk ?? ""))
            LogLine("[WiFi] 5G WiFi parolası: ********");
    }

    private static string Bos(string s) => string.IsNullOrEmpty(s) ? "(okunamadı)" : s;

    private async void RefreshInfo_Click(object sender, RoutedEventArgs e)
    {
        RefreshInfoBtn.IsEnabled = false;
        if (AppConfig.IsLite) StartLiteAnimation();
        else Phase("Cihaz parametreleri yenileniyor...");
        await FetchAndFillInfoAsync();
        if (AppConfig.IsLite) StopLiteAnimation("Cihaz bilgileri güncellendi ✓");
        else Phase("Cihaz bilgileri güncellendi ✓");
        RefreshInfoBtn.IsEnabled = true;
    }

    private async void DualSsid_Click(object sender, RoutedEventArgs e)
    {
        if (_info == null) return;
        DualSsidBtn.IsEnabled = false; Phase("2.4G ve 5G WiFi ayrıştırma uygulanıyor...");
        try
        {
            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None))) { LogLine("[WiFi] oturum açılamadı"); return; }
            var (ok, msg) = await ModemInfoWriter.EnableDualSsidAsync(web, _info, CancellationToken.None);
            LogLine("[WiFi] " + msg);
            Phase(ok ? "Çift SSID ayrıştırma uygulandı ✓" : "Çift SSID uygulanamadı");
            await Task.Delay(1500); await FetchAndFillInfoAsync();
        }
        catch (Exception ex) { LogLine("[WiFi] Hata: " + ex.Message); Phase("WiFi yapılandırma hatası"); }
        finally { DualSsidBtn.IsEnabled = true; }
    }

    private void DnsPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DnsPreset == null || DnsNew1 == null || DnsNew2 == null) return;
        if (DnsPreset.SelectedItem is not ComboBoxItem it) return;
        var s = it.Content?.ToString() ?? "";
        if      (s.Contains("Cloudflare")) { DnsNew1.Text = "1.1.1.1";    DnsNew2.Text = "1.0.0.1"; }
        else if (s.Contains("Google"))     { DnsNew1.Text = "8.8.8.8";    DnsNew2.Text = "8.8.4.4"; }
        else if (s.Contains("Quad9"))      { DnsNew1.Text = "9.9.9.9";    DnsNew2.Text = "149.112.112.112"; }
        else if (s.Contains("Turkcell"))   { DnsNew1.Text = "213.74.1.1"; DnsNew2.Text = "213.74.0.1"; }
    }

    private async void DnsApply_Click(object sender, RoutedEventArgs e)
    {
        DnsApplyBtn.IsEnabled = false; Phase("DNS sunucu adresleri uygulanıyor...");
        try
        {
            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None))) { LogLine("[DNS] oturum açılamadı"); return; }
            var ok = await ModemInfoWriter.SetLanDnsAsync(web, (DnsNew1.Text ?? "").Trim(), (DnsNew2.Text ?? "").Trim(), CancellationToken.None);
            LogLine("[DNS] " + ok.msg);
            Phase(ok.ok ? "DNS adresleri başarıyla kaydedildi ✓" : "DNS güncellenemedi");
            await Task.Delay(1500); await FetchAndFillInfoAsync();
        }
        catch (Exception ex) { LogLine("[DNS] Hata: " + ex.Message); Phase("DNS güncelleme hatası"); }
        finally { DnsApplyBtn.IsEnabled = true; }
    }

    private static string SifreDosyasi()
        => Path.Combine(Engine.MasaustuDizini(), "HekzONT", "suser_hatirla.txt");

    private static void SifreHatirlaYaz(string sifre)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(SifreDosyasi())); File.WriteAllText(SifreDosyasi(), sifre); }
        catch { }
    }

    private static string SifreHatirlaOku()
    {
        try { return File.Exists(SifreDosyasi()) ? File.ReadAllText(SifreDosyasi()).Trim() : ""; }
        catch { return ""; }
    }

    private void CfgHesapDogrulaYaz(string cfgXml)
    {
        try
        {
            var list = Engine.WebHesaplariCek(cfgXml);
            if (list.Count == 0) return;
            LogLine("[Kullanıcılar] Cihaz web arayüz hesapları:");
            foreach (var it in list)
            {
                if (it.FactoryPass.Length > 0) LogMaskeleyici.GizliDegerEkle(it.FactoryPass);
                var sev = it.Level == 0 ? "Yönetici (Root)" : it.Level == 1 ? "Standart Kullanıcı" : $"Yetki Seviyesi {it.Level}";
                var fab = it.FactoryPass.Length > 0 ? " — Fabrika Varsayılanı: ********" : "";
                LogLine($"  • {it.User} [{sev}]{fab}");
            }
            var su = list.FirstOrDefault(x => x.User.Equals("sUser", StringComparison.OrdinalIgnoreCase));
            if (su != null)
            {
                var adaylar = new List<string>();
                string hatirlanan = SifreHatirlaOku();
                if (hatirlanan.Length > 0) adaylar.Add(hatirlanan);
                if (_sUserPass.Length > 0 && !adaylar.Contains(_sUserPass)) adaylar.Add(_sUserPass);
                string eslesen = adaylar.FirstOrDefault(a => Engine.SifreDogrula(su, a));
                if (eslesen != null)
                {
                    _sUserPass = eslesen;
                    LogMaskeleyici.GizliDegerEkle(eslesen);
                    SetSecretText(ResSuser, "sUser / " + eslesen);
                    LogLine(eslesen == hatirlanan
                        ? "[Doğrulama] ✓ sUser yönetici şifresi doğrulandı (önceki oturumdan hatırlanan şifre)"
                        : "[Doğrulama] ✓ sUser yönetici şifresi cihaz hash özetiyle doğrulandı");
                }
                else
                {
                    LogLine("[Doğrulama] ⚠ sUser şifresi bilinen adaylarla uyuşmuyor — şifre daha önce değiştirilmiş olabilir");
                }
            }
        }
        catch (Exception ex) { LogLine("! [Kullanıcılar] Hesap doğrulama uyarısı: " + ex.Message); }
    }

    private async void Cfg_Click(object sender, RoutedEventArgs e)
    {
        CfgBtn.IsEnabled = false;
        if (AppConfig.IsLite) StartLiteAnimation();
        else Phase("PPPoE ve konfigürasyon verisi alınıyor...");
        try
        {
            if (_engineCfgXml.Length >= 1000)
            {
                var dir = Path.Combine(Engine.MasaustuDizini(), "HekzONT");
                Directory.CreateDirectory(dir);
                var dosya = Path.Combine(dir, $"ont_ayar_{DateTime.Now:yyyyMMdd_HHmmss}.xml");
                await File.WriteAllTextAsync(dosya, _engineCfgXml, CancellationToken.None);
                LogLine($"[PPPoE] Ayar dosyası: {dosya} ({_engineCfgXml.Length:N0} krktr)");
                var m = Regex.Match(_engineCfgXml, "WANPPPConnectionInstance[^>]*Username=\"([^\"]*)\"[^>]*Password=\"([^\"]*)\"");
                if (m.Success)
                {
                    ResPppoeUser.Text = m.Groups[1].Value;
                    var dec = Engine.Dollar2.Decrypt(m.Groups[2].Value);
                    if (dec.Length > 0) LogMaskeleyici.GizliDegerEkle(dec);
                    SetSecretText(ResPppoePass, dec.Length > 0 ? dec : "(çözülemedi)");
                    LogLine($"[PPPoE] ✓ {m.Groups[1].Value} / {(dec.Length > 0 ? "********" : "çözülemedi")}");
                }
                CfgHesapDogrulaYaz(_engineCfgXml);
                CopyAllBtn.IsEnabled = true;
                if (AppConfig.IsLite) StopLiteAnimation("PPPoE bilgileri hazırlandı ✓");
                else Phase("PPPoE kullanıcı bilgileri çözüldü ✓");
            }
            else { LogLine("[PPPoE] Konfigürasyon verisi yok — BAŞLAT'ı çalıştır"); Phase("PPPoE: Önce 'BAŞLAT' ile konfigürasyonu çekin"); }
        }
        catch (Exception ex) { LogLine("[Cihaz] Hata: " + ex.Message); Phase("PPPoE okuma hatası"); }
        finally { CfgBtn.IsEnabled = true; if (AppConfig.IsLite) StopLiteAnimation(); }
    }

    private static string YedekKlasoru()
    {
        var d = Path.Combine(Engine.MasaustuDizini(), "HekzONT", "yedekler");
        Directory.CreateDirectory(d);
        return d;
    }

    private void LogVeDurum(string onEk, List<(bool ok, string msg)> sonuclar)
    {
        int ok = sonuclar.Count(x => x.ok);
        foreach (var r in sonuclar) LogLine(onEk + " " + r.msg);
        LogLine($"{onEk} Özet: {ok}/{sonuclar.Count} adım doğrulandı");
        Phase($"{onEk} {ok}/{sonuclar.Count} tamamlandı ✓");
    }

    private async void BackupProfile_Click(object sender, RoutedEventArgs e)
    {
        BackupProfileBtn.IsEnabled = false; Phase("Seçili kategori ayarları yedekleniyor...");
        try
        {
            var kapsam = KapsamOku();
            if (kapsam.HicBiriSecili) { LogLine("[Yedek] Hiçbir kategori seçili değil."); Phase("Lütfen en az bir kategori seçin"); return; }

            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None)))
            { LogLine("[Yedek] Oturum açılamadı"); Phase("Yedekleme: Oturum açılamadı"); return; }

            var mi = await ModemInfoReader.FetchAllAsync(web, CancellationToken.None, LogLine);
            var profil = BackupYardimci.ProfilOlustur(mi, kapsam, _ip);
            string yol = await BackupYardimci.ProfilKaydetAsync(profil, YedekKlasoru(), CancellationToken.None);

            LogLine("[Yedek] Profil kaydedildi: " + yol);
            LogLine("[Yedek] İçerik: " + BackupYardimci.Ozet(profil));
            Phase("Ayar profili başarıyla kaydedildi ✓");
        }
        catch (Exception ex) { LogLine("[Yedek] Hata: " + ex.Message); Phase("Yedekleme hatası"); }
        finally { BackupProfileBtn.IsEnabled = true; }
    }

    private async void BackupDevice_Click(object sender, RoutedEventArgs e)
    {
        BackupDeviceBtn.IsEnabled = false; Phase("Cihaz tam konfigürasyon yedeği indiriliyor...");
        try
        {
            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None)))
            { LogLine("[Yedek] Oturum açılamadı"); Phase("Yedekleme: Oturum açılamadı"); return; }

            byte[] data = await web.DownloadConfigBackupAsync(CancellationToken.None);
            if (data == null || data.Length == 0)
            {
                LogLine("[Yedek] Cihaz yedeği indirilemedi — modem bu sayfada indirmeye izin vermiyor olabilir.");
                Phase("Cihaz yedeği indirilemedi");
                return;
            }

            string yol = Path.Combine(YedekKlasoru(), $"hekz_cihaz_yedek_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
            await File.WriteAllBytesAsync(yol, data, CancellationToken.None);
            LogLine($"[Yedek] Cihazın tam yedeği indirildi: {yol} ({data.Length:N0} bayt)");
            Phase("Cihaz yedeği başarıyla indirildi ✓");
        }
        catch (Exception ex) { LogLine("[Yedek] Hata: " + ex.Message); Phase("Cihaz yedeği indirme hatası"); }
        finally { BackupDeviceBtn.IsEnabled = true; }
    }

    private async void RestoreProfile_Click(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Yedek profilini seç",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("HekzOntRace yedek profili (*.json)") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All
            }
        });
        if (files == null || files.Count == 0) return;
        string secilenYol = files[0].Path.LocalPath;

        var profil = BackupYardimci.ProfilOku(secilenYol, out string hata);
        if (profil == null) { LogLine("[Geri yükle] " + hata); Phase("Yedek profil dosyası okunamadı"); return; }

        var kapsam = KapsamOku();
        if (kapsam.HicBiriSecili) { LogLine("[Geri yükle] Hiçbir kategori seçili değil."); Phase("Lütfen geri yüklenecek en az bir kategori seçin"); return; }

        kapsam.Wifi        &= profil.Kapsam.Wifi;
        kapsam.Dns         &= profil.Kapsam.Dns;
        kapsam.Tr069       &= profil.Kapsam.Tr069;
        kapsam.GuvenlikAlg &= profil.Kapsam.GuvenlikAlg;

        LogLine($"[Geri yükle] Profil: {Path.GetFileName(secilenYol)}");
        LogLine($"[Geri yükle] Profil içeriği: {BackupYardimci.Ozet(profil)}");

        if (!kapsam.Wifi && !kapsam.Dns && !kapsam.Tr069 && !kapsam.GuvenlikAlg)
        {
            LogLine("[Geri yükle] Yazılabilecek bir kategori yok (profil yalnızca bilgi içeriyor).");
            Phase("Yazılacak ayar bulunamadı");
            return;
        }

        bool onay = await ShowConfirmDialogAsync(
            "Yedekten geri yükle",
            "Seçili ayarlar modeme yazılacak:\n\n" +
            (kapsam.Wifi ? "• WiFi (SSID ve parola)\n" : "") +
            (kapsam.Dns ? "• DNS\n" : "") +
            (kapsam.Tr069 ? "• TR-069\n" : "") +
            (kapsam.GuvenlikAlg ? "• Güvenlik seviyesi ve ALG\n" : "") +
            "\nDevam edilsin mi?");
        if (!onay) { Phase("Geri yükleme iptal edildi"); return; }

        RestoreProfileBtn.IsEnabled = false; Phase("Ayar profili cihaza geri yükleniyor...");
        try
        {
            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None)))
            { LogLine("[Geri yükle] Oturum açılamadı"); Phase("Geri yükleme: Oturum açılamadı"); return; }

            var sonuclar = await ProfilGeriYukleme.UygulaAsync(web, profil, kapsam, CancellationToken.None);
            LogVeDurum("[Geri yükle]", sonuclar);
            await Task.Delay(1200);
            await FetchAndFillInfoAsync();
        }
        catch (Exception ex) { LogLine("[Geri yükle] Hata: " + ex.Message); Phase("Geri yükleme hatası"); }
        finally { RestoreProfileBtn.IsEnabled = true; }
    }

    private async void RestoreDevice_Click(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Cihaz yedeğini seç",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Modem yapılandırma dosyası (*.bin;*.xml;*.cfg)") { Patterns = new[] { "*.bin", "*.xml", "*.cfg" } },
                FilePickerFileTypes.All
            }
        });
        if (files == null || files.Count == 0) return;
        string secilenYol = files[0].Path.LocalPath;

        bool onay = await ShowConfirmDialogAsync(
            "Cihaz yedeğini geri yükle",
            "Modem, yapılandırma dosyasını geri yükleyip YENİDEN BAŞLAYACAK.\n\n" +
            "Bu işlem bağlantıyı kısa süre keser. Yalnızca kendi cihazınızda ve\n" +
            "bu dosyanın size ait olduğundan emin olduğunuzda kullanın.\n\nDevam edilsin mi?");
        if (!onay) { Phase("Geri yükleme iptal edildi"); return; }

        RestoreDeviceBtn.IsEnabled = false; Phase("Cihaz konfigürasyon yedeği yükleniyor...");
        try
        {
            byte[] dosya = await File.ReadAllBytesAsync(secilenYol);
            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None)))
            { LogLine("[Geri yükle] Oturum açılamadı"); Phase("Geri yükleme: Oturum açılamadı"); return; }

            var (ok, detay) = await web.UploadConfigRestoreAsync(dosya, Path.GetFileName(secilenYol), CancellationToken.None);
            LogLine("[Geri yükle] " + (ok ? "✓ Yükleme gönderildi" : "⚠ Yükleme başarısız") + " — " + detay);
            if (ok)
            {
                LogLine("[Geri yükle] Modem kendini yeniden başlatıyor — 1-2 dakika sonra 'Sistem Parametrelerini Yenile' ile kontrol edin.");
                Phase("Yedek yüklendi — Cihaz yeniden başlatılıyor...");
            }
            else Phase("Cihaz yedeği yüklenemedi");
        }
        catch (Exception ex) { LogLine("[Geri yükle] Hata: " + ex.Message); Phase("Geri yükleme hatası"); }
        finally { RestoreDeviceBtn.IsEnabled = true; }
    }

    private async Task Tr069UygulaAsync(bool cwmp, bool periyodik)
    {
        Tr069Btn.IsEnabled = false; Tr069OffBtn.IsEnabled = false; Phase("TR-069 ACS ayarları güncelleniyor...");
        try
        {
            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None)))
            { LogLine("[TR-069] Giriş başarısız — yönetici (sUser) hesabı gerekli"); Phase("TR-069: Yönetici (sUser) oturumu gerekli"); return; }

            var (ok, msg) = await ModemInfoWriter.SetTr069Async(web, cwmp, periyodik, CancellationToken.None);
            LogLine("[TR-069] " + msg);
            Phase(ok ? "TR-069 ayarları başarıyla kaydedildi ✓" : "TR-069 güncellenemedi");
            await FetchAndFillInfoAsync();
        }
        catch (Exception ex) { LogLine("[TR-069] Hata: " + ex.Message); Phase("TR-069 güncelleme hatası"); }
        finally { Tr069Btn.IsEnabled = true; Tr069OffBtn.IsEnabled = true; }
    }

    private async void Tr069_Click(object sender, RoutedEventArgs e)
        => await Tr069UygulaAsync(Tr069CwmpSel.SelectedIndex == 1, Tr069PeriodicSel.SelectedIndex == 1);

    private async void Tr069Off_Click(object sender, RoutedEventArgs e)
    {
        Tr069CwmpSel.SelectedIndex = 0;
        Tr069PeriodicSel.SelectedIndex = 0;
        await Tr069UygulaAsync(false, false);
    }

    private async void FwApply_Click(object sender, RoutedEventArgs e)
    {
        FwApplyBtn.IsEnabled = false; Phase("Güvenlik duvarı seviyesi ayarlanıyor...");
        try
        {
            if (FwLevelSel.SelectedItem is not ComboBoxItem it) return;
            string metin = it.Content?.ToString() ?? "";
            string kod = metin.Contains("Kapalı") ? "Disable"
                       : metin.Contains("Düşük") ? "Low"
                       : metin.Contains("Orta") ? "Medium"
                       : metin.Contains("Yüksek") ? "High"
                       : metin.Contains("Katı") ? "Strict" : "";

            if (kod == "Medium")
            {
                LogLine("[Güvenlik] Bu cihaz Orta (Medium) seviyeyi desteklemiyor — Düşük/Yüksek/Katı seçin.");
                Phase("Desteklenmeyen güvenlik seviyesi"); return;
            }
            if (kod.Length == 0) { LogLine("[Güvenlik] Seviye seçilmedi."); return; }

            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None)))
            { LogLine("[Güvenlik] Oturum açılamadı"); Phase("Güvenlik duvarı: Oturum açılamadı"); return; }

            var (ok, msg) = await ModemInfoWriter.SetFirewallLevelAsync(web, kod, CancellationToken.None);
            LogLine("[Güvenlik] " + msg);
            Phase(ok ? "Güvenlik duvarı seviyesi uygulandı ✓" : "Güvenlik duvarı güncellenemedi");
            await Task.Delay(1200);
            await FetchAndFillInfoAsync();
        }
        catch (Exception ex) { LogLine("[Güvenlik] Hata: " + ex.Message); Phase("Güvenlik duvarı güncelleme hatası"); }
        finally { FwApplyBtn.IsEnabled = true; }
    }

    private async void AlgApply_Click(object sender, RoutedEventArgs e)
    {
        AlgApplyBtn.IsEnabled = false; Phase("ALG protokol geçitleri kaydediliyor...");
        try
        {
            var secim = new Dictionary<string, bool>
            {
                ["FTP"]   = CbAlgFTP.IsChecked == true,
                ["TFTP"]  = CbAlgTFTP.IsChecked == true,
                ["H323"]  = CbAlgH323.IsChecked == true,
                ["SIP"]   = CbAlgSIP.IsChecked == true,
                ["RTSP"]  = CbAlgRTSP.IsChecked == true,
                ["PPTP"]  = CbAlgPPTP.IsChecked == true,
                ["L2TP"]  = CbAlgL2TP.IsChecked == true,
                ["IPSec"] = CbAlgIPSec.IsChecked == true,
                ["RTCP"]  = CbAlgRTCP.IsChecked == true
            };

            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None)))
            { LogLine("[ALG] Oturum açılamadı"); Phase("ALG ayarları: Oturum açılamadı"); return; }

            var (ok, msg) = await ModemInfoWriter.SetAlgAsync(web, secim, CancellationToken.None);
            LogLine("[ALG] " + msg);
            Phase(ok ? "ALG ayarları başarıyla kaydedildi ✓" : "ALG güncellenemedi");
            await Task.Delay(1200);
            await FetchAndFillInfoAsync();
        }
        catch (Exception ex) { LogLine("[ALG] Hata: " + ex.Message); Phase("ALG güncelleme hatası"); }
        finally { AlgApplyBtn.IsEnabled = true; }
    }

    // ---------- Linux Python 3 + Venv + Paket Hazırlayıcı ----------
    private async Task<string> HazirlaPythonOrtamiAsync()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string venvPy = !string.IsNullOrWhiteSpace(home)
            ? Path.Combine(home, ".local", "share", "hekzontrace", "venv", "bin", "python3")
            : null;

        if (!string.IsNullOrEmpty(venvPy) && File.Exists(venvPy) &&
            KutuphaneVar(venvPy, "paramiko") && KutuphaneVar(venvPy, "smb"))
        {
            return venvPy;
        }

        string sysPy = PythonBul();
        if (sysPy == null)
        {
            LogLine("✗ Sistemde Python 3 bulunamadı. Lütfen paket yöneticinizle kurun (örn: sudo apt install python3 python3-venv python3-pip).");
            return null;
        }

        if (KutuphaneVar(sysPy, "paramiko") && KutuphaneVar(sysPy, "smb"))
        {
            return sysPy;
        }

        KurulumBar.IsVisible = true;
        Phase("Python bileşenleri (paramiko, pysmb) hazırlanıyor...");
        try
        {
            // 1. Önce izole sanal ortam (~/.local/share/hekzontrace/venv) dene (PEP 668 uyumlu)
            if (!string.IsNullOrEmpty(venvPy))
            {
                string venvDir = Path.GetDirectoryName(Path.GetDirectoryName(venvPy));
                Directory.CreateDirectory(Path.GetDirectoryName(venvDir));
                var pVenv = Process.Start(new ProcessStartInfo(sysPy, $"-m venv \"{venvDir}\"")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                if (pVenv != null) await pVenv.WaitForExitAsync();

                if (File.Exists(venvPy))
                {
                    var pPip = Process.Start(new ProcessStartInfo(venvPy, "-m pip install paramiko pysmb --quiet --disable-pip-version-check")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                    if (pPip != null)
                    {
                        await pPip.StandardOutput.ReadToEndAsync();
                        await pPip.StandardError.ReadToEndAsync();
                        await pPip.WaitForExitAsync();
                    }
                    if (KutuphaneVar(venvPy, "paramiko") && KutuphaneVar(venvPy, "smb"))
                        return venvPy;
                }
            }

            // 2. Yedek: --user --break-system-packages ile dene
            foreach (var args in new[]
            {
                "-m pip install --user --break-system-packages paramiko pysmb --quiet --disable-pip-version-check",
                "-m pip install --user paramiko pysmb --quiet --disable-pip-version-check"
            })
            {
                var p = Process.Start(new ProcessStartInfo(sysPy, args)
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                if (p != null)
                {
                    await p.StandardOutput.ReadToEndAsync();
                    await p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync();
                }
                if (KutuphaneVar(sysPy, "paramiko") && KutuphaneVar(sysPy, "smb"))
                    return sysPy;
            }
        }
        catch { }
        finally
        {
            KurulumBar.IsVisible = false;
        }

        LogLine("✗ Python bileşenleri (paramiko, pysmb) kurulamadı. Terminalde 'pip install paramiko pysmb' veya 'sudo apt install python3-paramiko python3-smb' çalıştırın.");
        return null;
    }

    private static string PythonBul()
    {
        foreach (var a in new[] { "python3", "python", "/usr/bin/python3", "/usr/local/bin/python3" })
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(a, "-c \"print('OK')\"")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                if (p == null) continue;
                var o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                if (o.Trim() == "OK") return a;
            }
            catch { }
        }
        return null;
    }

    private static bool KutuphaneVar(string py, string mod)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(py, "-c \"import " + mod + "\"")
            { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    // ---------- Tema Uyumlu Diyalog Pencereleri ----------
    private async Task ShowInfoDialogAsync(string title, string message)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#1A1E2A"))
        };
        var okBtn = new Button { Content = "Tamam", Width = 96, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        okBtn.Click += (_, _) => dlg.Close();
        dlg.Content = new Border
        {
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.Parse("#323A4D")),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#4A97EC")), Margin = new Thickness(0, 0, 0, 8) },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#E6E9F0")) },
                    okBtn
                }
            }
        };
        await dlg.ShowDialog(this);
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        bool result = false;
        var dlg = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#1A1E2A"))
        };
        var yesBtn = new Button { Content = "Evet", Width = 90 };
        var noBtn  = new Button { Content = "Hayır", Width = 90, Margin = new Thickness(8, 0, 0, 0) };
        noBtn.Classes.Add("GhostButton");
        yesBtn.Click += (_, _) => { result = true; dlg.Close(); };
        noBtn.Click  += (_, _) => { result = false; dlg.Close(); };

        dlg.Content = new Border
        {
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Color.Parse("#323A4D")),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#E8B44C")), Margin = new Thickness(0, 0, 0, 8) },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#E6E9F0")) },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Margin = new Thickness(0, 14, 0, 0),
                        Children = { yesBtn, noBtn }
                    }
                }
            }
        };
        await dlg.ShowDialog(this);
        return result;
    }
}
