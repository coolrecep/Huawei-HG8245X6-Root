using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using HekzOntRace.Core;

namespace HekzOntRace;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private CancellationTokenSource _cts;
    private string _aktifKullanici = "";
    private string _sUserPass = "";
    private string _engineCfgXml = "";
    private string _blobSsid = "";      // customizepara blob'undaki (fabrika) SSID
    private string _blobWifiSifre = ""; // customizepara blob'undaki (fabrika) WiFi şifresi
    private string _ip = "192.168.1.1";
    private ModemInfo _info;
    private BackupKapsam KapsamOku() => new BackupKapsam
    {
        Wifi = CbWifi.IsChecked == true,
        Dns = CbDns.IsChecked == true,
        Tr069 = CbTr069.IsChecked == true,
        GuvenlikAlg = CbGuvenlik.IsChecked == true,
        WanBilgi = CbWan.IsChecked == true,
        CihazBilgi = CbCihaz.IsChecked == true
    };

    private const string VarsayilanSifre = "superonline";
    private const string MaskeMetni = "••••••••••";
    private readonly Dictionary<TextBox, string> _gizliDegerler = new();
    private readonly HashSet<string> _acikAlanlar = new() { "ResAdmin", "ResSuser" };
    private bool _sifreSenkronKilidi;

    public MainWindow()
    {
        InitializeComponent();
        InitSecretControls();
        _log = new LogYonetici(Log, LogFiltre, LogOtoKaydir, LogZaman, () => LogOzet);
        LicenseRichBox.Document = LicenseInfo.CreateFlowDocument();
        InitAppMode();
        string path = Path.Combine(Path.GetTempPath(), "hekz_kurulum_ok");
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
            Dispatcher.BeginInvoke(() =>
            {
                LogLine("✓ Gereksinimler başarıyla kuruldu — BAŞLAT'a basabilirsin!");
                Phase("Hazır — gereksinimler tamam ✓");
            });
        }
    }

    private void InitSecretControls()
    {
        InputPass.Text = VarsayilanSifre;
        InputPassPwd.Password = VarsayilanSifre;

        InputPassPwd.PasswordChanged += (_, _) =>
        {
            if (_sifreSenkronKilidi) return;
            _sifreSenkronKilidi = true;
            InputPass.Text = InputPassPwd.Password;
            _sifreSenkronKilidi = false;
        };
        InputPass.TextChanged += (_, _) =>
        {
            if (_sifreSenkronKilidi) return;
            _sifreSenkronKilidi = true;
            InputPassPwd.Password = InputPass.Text;
            _sifreSenkronKilidi = false;
        };

        InputNewSuserPassPwd.PasswordChanged += (_, _) =>
        {
            if (_sifreSenkronKilidi) return;
            _sifreSenkronKilidi = true;
            InputNewSuserPass.Text = InputNewSuserPassPwd.Password;
            _sifreSenkronKilidi = false;
        };
        InputNewSuserPass.TextChanged += (_, _) =>
        {
            if (_sifreSenkronKilidi) return;
            _sifreSenkronKilidi = true;
            InputNewSuserPassPwd.Password = InputNewSuserPass.Text;
            _sifreSenkronKilidi = false;
        };

        foreach (var box in new[] { ResWifi, ResAdmin, ResSuser, ResPppoePass })
        {
            DataObject.AddCopyingHandler(box, (s, e) =>
            {
                if (s is TextBox tb)
                {
                    string gercek = GetSecretText(tb);
                    if (!string.IsNullOrEmpty(gercek))
                    {
                        Clipboard.SetText(gercek);
                        e.CancelCommand();
                    }
                }
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
        box.Text = _acikAlanlar.Contains(box.Name) ? v : MaskeMetni;
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

        btn.Foreground = (Brush)FindResource(yeniAcik ? "AccentHi" : "FgDim");

        if (tag == "InputPass")
        {
            InputPass.Visibility = yeniAcik ? Visibility.Visible : Visibility.Collapsed;
            InputPassPwd.Visibility = yeniAcik ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (tag == "InputNewSuserPass")
        {
            InputNewSuserPass.Visibility = yeniAcik ? Visibility.Visible : Visibility.Collapsed;
            InputNewSuserPassPwd.Visibility = yeniAcik ? Visibility.Collapsed : Visibility.Visible;
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(hwnd, 19, ref darkMode, sizeof(int)); // Eski Win10 derlemeleri için yedek

            int cornerPref = 2; // DWMWCP_ROUND (Windows 11 yuvarlatılmış köşeler)
            DwmSetWindowAttribute(hwnd, 33, ref cornerPref, sizeof(int));

            int borderColor = 0x004D3A32; // #323A4D (Line)
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));

            int captionColor = 0x002A1E1A; // #1A1E2A (BgCard)
            DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));

            int textColor = 0x00F0E9E6; // #E6E9F0 (Fg)
            DwmSetWindowAttribute(hwnd, 36, ref textColor, sizeof(int));
        }
        catch
        {
            // Eski Windows sürümlerinde DWM öznitelikleri desteklenmiyorsa sessizce geç
        }
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

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            RootWindowBorder.Padding = new Thickness(7);
            RootWindowBorder.BorderThickness = new Thickness(0);
            MaxRestoreBtn.ToolTip = "Önceki Boyut";
            MaxRestoreIcon.Data = Geometry.Parse("M 2.5,0.5 L 9.5,0.5 L 9.5,7.5 M 0.5,2.5 L 7.5,2.5 L 7.5,9.5 L 0.5,9.5 Z");
        }
        else
        {
            RootWindowBorder.Padding = new Thickness(0);
            RootWindowBorder.BorderThickness = new Thickness(1);
            MaxRestoreBtn.ToolTip = "Ekranı Kapla";
            MaxRestoreIcon.Data = Geometry.Parse("M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z");
        }
    }

    private void InitAppMode()
    {
        Title = AppConfig.AppTitle;
        if (AppConfig.IsLite)
        {
            AppNameText.Text = "HekzOntRace Lite";
            TitleBarMainText.Text = "HekzOntRace Lite v2.0.1";
            TitleBarSubText.Text = "Huawei LG8245X6-50 / HG8245X6 Kolay Bilgi ve Şifre Aracı";
            LiteBadge.Visibility = Visibility.Visible;
            AboutToolTitle.Text = "Huawei LG8245X6-50 / HG8245X6 Kolay Bilgi ve Şifre Aracı (Lite)";
            AboutVersionBadge.Text = "v2.0.1 Lite Açık Kaynak";
            LiteLegalNote.Visibility = Visibility.Visible;

            // Lite modda tehlikeli fiş çekmeli donanım race modu ve teknik simülasyon kutuları gizlenir
            SuserYazBtn.Visibility = Visibility.Collapsed;
            PaketTestiCheck.Visibility = Visibility.Collapsed;
            TelnetSshCheck.Visibility = Visibility.Collapsed;

            // Açıklama ve buton metni kullanıcı dostu yapılır
            SuserBox.Header = "Yönetici Parolası Güncelleme (sUser)";
            SuserBoxDesc.Text = "Cihazın fabrika 'sUser' yönetici şifresi yukarıdaki kutuda verilmiştir. Dilerseniz bu alandan sUser hesabına yeni bir parola belirleyip güvenli şekilde modeme kaydedebilirsiniz.";
            SuserYazWebBtn.Content = "⚡ Yeni sUser Parolasını Güvenle Kaydet";
        }
    }

    private void CopyAllInfo_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Huawei HG8245X6 Cihaz ve Erişim Bilgileri ===");
        sb.AppendLine($"Modem Arayüzü     : http://{_ip}");
        sb.AppendLine($"Standart Kullanıcı: admin / {VarsayilanSifre}");
        sb.AppendLine($"sUser Yönetici    : {GetSecretText(ResSuser)}");
        sb.AppendLine($"PPPoE Kullanıcı   : {ResPppoeUser.Text}");
        sb.AppendLine($"PPPoE Parolası    : {GetSecretText(ResPppoePass)}");
        sb.AppendLine($"Wi-Fi Adı (SSID)  : {ResSsid.Text}");
        sb.AppendLine($"Wi-Fi Parolası    : {GetSecretText(ResWifi)}");
        sb.AppendLine("==================================================");

        Clipboard.SetText(sb.ToString());
        Phase("Tüm erişim ve şifre bilgileri panoya kopyalandı ✓");
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string url = "http://" + (string.IsNullOrWhiteSpace(_ip) ? "192.168.1.1" : _ip);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Phase("Modem yönetim sayfası tarayıcıda açıldı: " + url);
        }
        catch (Exception ex)
        {
            Phase("Tarayıcı açılamadı: " + ex.Message);
        }
    }

    private void CopyLicense_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(LicenseInfo.GnuGplV2);
        Phase("GNU GPL v2 lisans metni panoya kopyalandı ✓");
    }

    private LogYonetici _log;

    private void LogLine(string m)
    {
        if (AppConfig.IsLite)
        {
            m = LiteLogUyarlayici.Uyarlayici(m);
            if (string.IsNullOrEmpty(m)) return;
        }
        Dispatcher.Invoke(() => _log.Ekle(m));
    }
    private System.Windows.Threading.DispatcherTimer _liteWaitTimer;
    private int _liteDotCount = 0;

    private void StartLiteAnimation()
    {
        if (!AppConfig.IsLite) return;
        _liteDotCount = 0;
        PhaseText.Text = "Lütfen bekleyiniz...";
        KurulumBar.Visibility = Visibility.Visible;
        if (_liteWaitTimer == null)
        {
            _liteWaitTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(450)
            };
            _liteWaitTimer.Tick += (s, e) =>
            {
                _liteDotCount = (_liteDotCount % 4) + 1;
                string dots = new string('.', _liteDotCount);
                PhaseText.Text = "Lütfen bekleyiniz" + dots;
            };
        }
        _liteWaitTimer.Start();
    }

    private void StopLiteAnimation(string finalMessage = null)
    {
        if (!AppConfig.IsLite) return;
        if (_liteWaitTimer != null && _liteWaitTimer.IsEnabled)
        {
            _liteWaitTimer.Stop();
        }
        KurulumBar.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrEmpty(finalMessage))
        {
            PhaseText.Text = finalMessage;
        }
    }

    private System.Windows.Media.Animation.DoubleAnimation _marqueeAnim;

    public void StartMarquee(string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (_liteWaitTimer != null && _liteWaitTimer.IsEnabled) _liteWaitTimer.Stop();
            PhaseText.Visibility = Visibility.Collapsed;
            MarqueeText.Text = message;
            MarqueeCanvas.Visibility = Visibility.Visible;
            KurulumBar.Visibility = Visibility.Visible;

            MarqueeText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = MarqueeText.DesiredSize.Width;
            double canvasWidth = MarqueeCanvas.ActualWidth > 50 
                ? MarqueeCanvas.ActualWidth 
                : (this.ActualWidth > 250 ? this.ActualWidth - 260 : 650);

            _marqueeAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = canvasWidth,
                To = -textWidth - 40,
                Duration = TimeSpan.FromSeconds(Math.Max(12, (canvasWidth + textWidth) / 60.0)),
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            MarqueeTransform.BeginAnimation(TranslateTransform.XProperty, _marqueeAnim);
        });
    }

    public void StopMarquee(string finalMessage = null)
    {
        Dispatcher.Invoke(() =>
        {
            if (_marqueeAnim != null)
            {
                MarqueeTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _marqueeAnim = null;
            }
            MarqueeCanvas.Visibility = Visibility.Collapsed;
            PhaseText.Visibility = Visibility.Visible;
            KurulumBar.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrEmpty(finalMessage))
            {
                PhaseText.Text = finalMessage;
            }
        });
    }

    private async Task<bool> WaitForRebootAndReconnectAsync(string ip, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();

        // 1. Aşama: Modemin eski oturumu kapatıp yeniden başlama sürecine girmesi için 10 saniye bekle
        try
        {
            await Task.Delay(10000, token);
        }
        catch (OperationCanceledException) { return false; }

        // 2. Aşama: Modemin tekrar açılıp Wi-Fi / ağ bağlantısının kurulmasını bekle (en fazla 180 saniye)
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
            catch
            {
                // Bağlantı henüz yok, modem boot aşamasında ya da Wi-Fi henüz bağlanmadı
            }

            try
            {
                await Task.Delay(2500, token);
            }
            catch (OperationCanceledException) { return false; }
        }

        return false;
    }

    private void Phase(string p)
    {
        p = LogMaskeleyici.Temizle(p ?? "");
        Dispatcher.Invoke(() =>
        {
            if (_marqueeAnim != null) return; // Kayan yazı devredeyken ara durum metinleri engellenir

            if (AppConfig.IsLite)
            {
                // Devam eden bekleme animasyonu varsa ("Lütfen bekleyiniz...")
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
                    // Ayrıntılı teknik ara aşamalar durum çubuğunda gizlenir, "Lütfen bekleyiniz..." animasyonu akar
                    return;
                }

                // Boşta veya buton tıklamalarında
                if (p.Contains("Wi-Fi bağlantısı kuruldu"))
                {
                    // Özel Wi-Fi yeniden bağlantı başarı mesajını olduğu gibi koru
                }
                else if (p.Contains("tamamlandı", StringComparison.OrdinalIgnoreCase) || p.Contains("✓"))
                {
                    p = "İşlem başarıyla tamamlandı ✓";
                }
                else if (p.Contains("Hata", StringComparison.OrdinalIgnoreCase) || p.Contains("başarısız", StringComparison.OrdinalIgnoreCase))
                {
                    p = "İşlem tamamlanamadı — Detaylar için Log sekmesine bakın";
                }
            }

            PhaseText.Text = p;
        });
    }

    // ---------- Log paneli olayları ----------
    private void LogFiltre_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_log != null) _log.YenidenCiz();
    }

    private void LogZaman_Click(object sender, RoutedEventArgs e)
    {
        if (_log != null) _log.ZamanDegisti();
    }

    private void LogKaydet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string yol = _log.Kaydet();
            Phase("Log kaydedildi: " + System.IO.Path.GetFileName(yol));
            MessageBox.Show("Log kaydedildi:\n\n" + yol, "HekzOntRace",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Phase("Log kaydedilemedi: " + ex.Message); }
    }

    private void LogKopyala_Click(object sender, RoutedEventArgs e)
    {
        _log.Kopyala();
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
        // basit sağlama
        if (!Regex.IsMatch(t, @"^\d{1,3}(\.\d{1,3}){3}$")) t = "192.168.1.1";
        return t;
    }

    private void SetRunningState(bool running)
    {
        GoBtn.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        GoBtn.IsEnabled = !running;
        StopBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
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
        SuserBox.Visibility = Visibility.Collapsed;
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

        string py = PythonBul();
        if (py == null)
        {
            KurulumBar.Visibility = Visibility.Visible; Phase("Gereksinimler kuruluyor...");
            LogLine("Python bulunamadı — otomatik kurulum deneniyor...");
            if (!(await WingetKur()) && !(await PythonIndirKur()))
            {
                KurulumBar.Visibility = Visibility.Collapsed;
                LogLine("Otomatik kurulum başarısız — python.org'dan 3.13 kurup tekrar deneyin.");
                Phase("Gereksinim hatası"); SetRunningState(false); return;
            }
            KurulumBar.Visibility = Visibility.Collapsed;
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "hekz_kurulum_ok"), "1"); } catch { }
            await Task.Delay(1200); App.YenidenBaslat(); return;
        }
        if (!KutuphaneVar(py, "paramiko") || !KutuphaneVar(py, "smb"))
        {
            KurulumBar.Visibility = Visibility.Visible; Phase("Python bileşenleri kuruluyor...");
            var proc = Process.Start(new ProcessStartInfo(py, "-m pip install paramiko pysmb --quiet --disable-pip-version-check")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
            await proc.StandardOutput.ReadToEndAsync(); await proc.StandardError.ReadToEndAsync(); await proc.WaitForExitAsync();
            KurulumBar.Visibility = Visibility.Collapsed;
            if (KutuphaneVar(py, "paramiko") && KutuphaneVar(py, "smb"))
            { try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "hekz_kurulum_ok"), "1"); } catch { } await Task.Delay(800); App.YenidenBaslat(); return; }
            LogLine("Bileşen kurulumu başarısız"); Phase("Gereksinim hatası");
            SetRunningState(false); return;
        }

        string user = string.IsNullOrWhiteSpace(InputUser.Text) ? "admin" : InputUser.Text.Trim();
        string pass = string.IsNullOrWhiteSpace(InputPass.Text) ? VarsayilanSifre : InputPass.Text.Trim();

        var engine = new Engine(_ip, pass, LogLine, Phase, _cts.Token) { PythonPath = py, LoginUser = user, PaketTesti = PaketTestiCheck.IsChecked == true, TelnetSshAc = TelnetSshCheck.IsChecked == true, SuserPass = _sUserPass, HesapTemizle = HesapTemizleCheck.IsChecked == true };
        engine.OnPlainText += plain => Dispatcher.Invoke(() => FillResults(plain));

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

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "HekzONT");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            await File.WriteAllBytesAsync(Path.Combine(dir, $"ont_veri_{stamp}.bin"), engine.EncryptedBlob, _cts.Token);
            await File.WriteAllTextAsync (Path.Combine(dir, $"ont_veri_{stamp}.txt"), engine.PlainText,  _cts.Token);
            LogLine("[Depolama] Veri yedekleri kaydedildi: " + dir);

            CfgBtn.IsEnabled = _sUserPass.Length > 0;
            CopyAllBtn.IsEnabled = true;
            Tr069Btn.IsEnabled = true;
            Tr069OffBtn.IsEnabled = true;
            RefreshInfoBtn.IsEnabled = true;
            FwApplyBtn.IsEnabled = true;
            BackupProfileBtn.IsEnabled = true;
            BackupDeviceBtn.IsEnabled = true;
            RestoreProfileBtn.IsEnabled = true;
            RestoreDeviceBtn.IsEnabled = true;
            SuserBox.Visibility = Visibility.Visible;

            if (_engineCfgXml.Length >= 1000) CfgHesapDogrulaYaz(_engineCfgXml);

            // Cihaz bilgilerini çek ve sekmelere dök
            Phase("Cihaz bilgileri ve modül parametreleri okunuyor...");
            await FetchAndFillInfoAsync();
            Phase("İşlem başarıyla tamamlandı ✓ — Detaylar ilgili sekmelerde");
        }
        catch (OperationCanceledException) { Phase("İşlem kullanıcı tarafından durduruldu"); }
        catch (Exception ex)
        {
            LogLine($"[{DateTime.Now:HH:mm:ss}] HATA: {ex.Message}");
            if (ex.InnerException != null) LogLine($"[{DateTime.Now:HH:mm:ss}] Detay: {ex.InnerException.Message}");
            Phase("İşlem başarısız — Detaylar için Günlük sekmesine bakın");
        }
        finally { SetRunningState(false); SuserYazBtn.IsEnabled = true; }
    }

    private void InputNewSuserPass_TextChanged(object sender, TextChangedEventArgs e)
    {
        string txt = InputNewSuserPass.Text;
        if (string.IsNullOrWhiteSpace(txt))
        {
            SuserPassStatusHint.Text = "ℹ️ Boş bırakılırsa otomatik rastgele şifre atanır";
            SuserPassStatusHint.Foreground = (Brush)FindResource("FgDim");
            return;
        }

        var (ok, err, _) = Engine.DogrulaYeniSuserSifresi(txt);
        if (!ok)
        {
            SuserPassStatusHint.Text = "⚠️ " + err.Replace("\n", " ");
            SuserPassStatusHint.Foreground = (Brush)FindResource("Warn");
        }
        else
        {
            SuserPassStatusHint.Text = $"✓ Geçerli parola formatı ({txt.Trim().Length} karakter)";
            SuserPassStatusHint.Foreground = (Brush)FindResource("Ok");
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
            MessageBox.Show(hata, "Geçersiz sUser Şifresi", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(hata, "Geçersiz sUser Şifresi", MessageBoxButton.OK, MessageBoxImage.Warning);
            LogLine("! [Şifre Doğrulama] " + hata.Replace("\n", " "));
            Phase("Geçersiz şifre parametresi");
            return;
        }

        SuserYazBtn.IsEnabled = false; SetRunningState(true);
        _cts = new CancellationTokenSource();
        _ip = ResolveIp();
        LogLine($"=== Donanım / Flash Enjeksiyonu (Race Modu) | Hedef: {_ip} ===");
        string py = PythonBul();
        if (py == null)
        {
            LogLine("✗ Python çalışma ortamı hazır değil — önce 'BAŞLAT'ı çalıştırarak kurulumu tamamlayın.");
            Phase("Python ortamı gerekli");
            SuserYazBtn.IsEnabled = true; SetRunningState(false);
            return;
        }
        string user = string.IsNullOrWhiteSpace(InputUser.Text) ? "admin" : InputUser.Text.Trim();
        string pass = string.IsNullOrWhiteSpace(InputPass.Text) ? VarsayilanSifre : InputPass.Text.Trim();
        var engine = new Engine(_ip, pass, LogLine, Phase, _cts.Token) { PythonPath = py, LoginUser = user, PaketTesti = PaketTestiCheck.IsChecked == true, TelnetSshAc = TelnetSshCheck.IsChecked == true, SuserPass = _sUserPass, HesapTemizle = HesapTemizleCheck.IsChecked == true };
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

    /// <summary>
    /// cihaza giriş: sUser (yönetici) varsa onu kullan, yoksa kutudaki bilgiler,
    /// en son admin/superonline. Cihaz TEK oturuma izin veriyor — bu yüzden
    /// eski/başarısız çerezler temizlenip tek seferde giriş yapılır.
    /// </summary>
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
            Dispatcher.Invoke(() => RenderInfo(_info));
            LogLine("[Sistem] Tüm cihaz parametreleri ve konfigürasyon modülleri başarıyla okundu ✓");
        }
        catch (Exception ex) { LogLine("! [Sistem] Bilgi okuma hatası: " + ex.Message); }
    }

    private void RenderInfo(ModemInfo mi)
    {
        // --- Cihaz ---
        DevName.Text   = Bos(mi.DeviceName);
        DevSerial.Text = Bos(mi.Serial);
        DevHw.Text     = Bos(mi.HwVersion);
        DevSw.Text     = Bos(mi.SwVersion);
        DevBoot.Text   = Bos(mi.LoaderVersion);
        DevMac.Text    = Bos(mi.Mac);
        DevUptime.Text = Bos(mi.Uptime);

        // --- Optik ---
        OptRx.Text   = Bos(mi.OpticalRxPower);
        OptTx.Text   = Bos(mi.OpticalTxPower);
        OptV.Text    = Bos(mi.OpticalVoltage);
        OptT.Text    = Bos(mi.OpticalTemperature);
        OptBias.Text = Bos(mi.OpticalBias);
        OptPonMode.Text = mi.PonMode.Length > 0 ? mi.PonMode : Bos("");
        OptOntState.Text = mi.OntDurumu.Length > 0 ? mi.OntDurumu : Bos("");
        OptTest.Text    = mi.OptikTest.Length > 0 ? mi.OptikTest : Bos("");
        OptOlt.Text     = mi.OltEslesme.Length > 0 ? mi.OltEslesme : Bos("");
        OptikNote.Text = mi.OpticalRxPower.Length > 0 ? "" : "Cihaz bu sürümde optik güç değerini yayınlamıyor (aşağıdaki logu okuyun).";

        // --- WAN ---
        WanName.Text      = Bos(mi.WanName);
        WanStatus.Text    = Bos(mi.WanStatus);
        WanIp.Text        = Bos(mi.WanIp);
        WanMtu.Text       = Bos(mi.WanMtu);
        WanPppUser.Text   = Bos(mi.PppoeUser);
        WanPppUptime.Text = Bos(mi.PppoeUptime);

        // --- DNS (LAN bilgisi WAN'ı eziyorsa onu göster) ---
        DnsCur1.Text = Bos(mi.DnsPrimary);
        DnsCur2.Text = Bos(mi.DnsSecondary);
        FwLevel.Text = Bos(mi.FirewallLevel);

        // --- ALG: mevcut durum onay kutularına yansıtılır ---
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

        // Mevcut güvenlik seviyesine göre açılır listeyi konumlandır
        int secIdx = 0;
        if (mi.FirewallLevel.Contains("Kapalı")) secIdx = 0;
        else if (mi.FirewallLevel.Contains("Düşük")) secIdx = 1;
        else if (mi.FirewallLevel.Contains("Orta")) secIdx = 2;
        else if (mi.FirewallLevel.Contains("Yüksek")) secIdx = 3;
        else if (mi.FirewallLevel.Contains("Katı")) secIdx = 4;
        FwLevelSel.SelectedIndex = secIdx;

        // TR-069 açılır listeleri mevcut durumu göstersin
        Tr069CwmpSel.SelectedIndex = mi.CwmpEnabled ? 1 : 0;
        Tr069PeriodicSel.SelectedIndex = mi.CwmpPeriodicInform ? 1 : 0;

        // --- TR-069 ---
        Tr069Cwmp.Text     = mi.CwmpEnabled ? "AÇIK" : "kapalı";
        Tr069Periodic.Text = mi.CwmpPeriodicInform ? "AÇIK" : "kapalı";
        Tr069Acs.Text      = Bos(mi.AcsUrl);

        // --- WiFi ---
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

        // --- Erişim bilgileri paneli: fabrika varsayılanlarını
        //     içerir (etiketteki değerler). Canlı değer web sayfalarından okunduysa
        //     paneli onunla güncelle, fabrika değeri köşeli parantezle not düş. ---
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

        // --- Notlar: boş sekme yerine gerçek sebebi loga yaz ---
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
        if (DnsPreset.SelectedItem is not ComboBoxItem it) return;
        var s = it.Content?.ToString() ?? "";
        if      (s.Contains("Cloudflare")) { DnsNew1.Text = "1.1.1.1";  DnsNew2.Text = "1.0.0.1"; }
        else if (s.Contains("Google"))     { DnsNew1.Text = "8.8.8.8";  DnsNew2.Text = "8.8.4.4"; }
        else if (s.Contains("Quad9"))      { DnsNew1.Text = "9.9.9.9";  DnsNew2.Text = "149.112.112.112"; }
        else if (s.Contains("Turkcell"))   { DnsNew1.Text = "213.74.1.1"; DnsNew2.Text = "213.74.0.1"; }
    }

    private async void DnsApply_Click(object sender, RoutedEventArgs e)
    {
        DnsApplyBtn.IsEnabled = false; Phase("DNS sunucu adresleri uygulanıyor...");
        try
        {
            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None))) { LogLine("[DNS] oturum açılamadı"); return; }
            var ok = await ModemInfoWriter.SetLanDnsAsync(web, DnsNew1.Text.Trim(), DnsNew2.Text.Trim(), CancellationToken.None);
            LogLine("[DNS] " + ok.msg);
            Phase(ok.ok ? "DNS adresleri başarıyla kaydedildi ✓" : "DNS güncellenemedi");
            await Task.Delay(1500); await FetchAndFillInfoAsync();
        }
        catch (Exception ex) { LogLine("[DNS] Hata: " + ex.Message); Phase("DNS güncelleme hatası"); }
        finally { DnsApplyBtn.IsEnabled = true; }
    }

    // --- sUser şifresi hafızası: yazdığımız şifre cihazda kalır ama blob fabrika değerini
    //     tuttuğu için yeni oturumlarda kayboluyordu. Masaüstü/HekzONT altına yazılır. ---
    private static string SifreDosyasi()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "HekzONT", "suser_hatirla.txt");

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
                // ADAYLAR: hatırlanan (önceki yazmalarımız) + blob fabrika şifresi.
                // Cihazın MEVCUT konfigürasyon hash'i hangisiyle doğrulanıyorsa aktif şifre odur.
                var adaylar = new List<string>();
                string hatirlanan = SifreHatirlaOku();
                if (hatirlanan.Length > 0) adaylar.Add(hatirlanan);
                if (_sUserPass.Length > 0 && !adaylar.Contains(_sUserPass)) adaylar.Add(_sUserPass);
                string eslesen = adaylar.FirstOrDefault(a => Engine.SifreDogrula(su, a));
                if (eslesen != null)
                {
                    _sUserPass = eslesen; // aktif şifre artık doğrulanmış değer
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
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "HekzONT");
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

    // ---------- Yedekleme / Geri yükleme ----------

    private static string YedekKlasoru()
    {
        var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "HekzONT", "yedekler");
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
        var dlg = new OpenFileDialog
        {
            Title = "Yedek profilini seç",
            InitialDirectory = YedekKlasoru(),
            Filter = "HekzOntRace yedek profili (*.json)|*.json|Tüm dosyalar (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var profil = BackupYardimci.ProfilOku(dlg.FileName, out string hata);
        if (profil == null) { LogLine("[Geri yükle] " + hata); Phase("Yedek profil dosyası okunamadı"); return; }

        var kapsam = KapsamOku();
        if (kapsam.HicBiriSecili) { LogLine("[Geri yükle] Hiçbir kategori seçili değil."); Phase("Lütfen geri yüklenecek en az bir kategori seçin"); return; }

        // Yalnızca hem profilde hem seçimde olanlar uygulanır
        kapsam.Wifi &= profil.Kapsam.Wifi;
        kapsam.Dns &= profil.Kapsam.Dns;
        kapsam.Tr069 &= profil.Kapsam.Tr069;
        kapsam.GuvenlikAlg &= profil.Kapsam.GuvenlikAlg;

        BackupYardimci.ProfilOku(dlg.FileName, out _);
        LogLine($"[Geri yükle] Profil: {Path.GetFileName(dlg.FileName)}");
        LogLine($"[Geri yükle] Profil içeriği: {BackupYardimci.Ozet(profil)}");

        if (!kapsam.Wifi && !kapsam.Dns && !kapsam.Tr069 && !kapsam.GuvenlikAlg)
        {
            LogLine("[Geri yükle] Yazılabilecek bir kategori yok (profil yalnızca bilgi içeriyor).");
            Phase("Yazılacak ayar bulunamadı");
            return;
        }

        var onay = MessageBox.Show(
            "Seçili ayarlar modeme yazılacak:\n\n" +
            (kapsam.Wifi ? "• WiFi (SSID ve parola)\n" : "") +
            (kapsam.Dns ? "• DNS\n" : "") +
            (kapsam.Tr069 ? "• TR-069\n" : "") +
            (kapsam.GuvenlikAlg ? "• Güvenlik seviyesi ve ALG\n" : "") +
            "\nDevam edilsin mi?",
            "Yedekten geri yükle", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (onay != MessageBoxResult.Yes) { Phase("Geri yükleme iptal edildi"); return; }

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
        var dlg = new OpenFileDialog
        {
            Title = "Cihaz yedeğini seç",
            InitialDirectory = YedekKlasoru(),
            Filter = "Modem yapılandırma dosyası (*.bin;*.xml;*.cfg)|*.bin;*.xml;*.cfg|Tüm dosyalar (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var onay = MessageBox.Show(
            "Modem, yapılandırma dosyasını geri yükleyip YENİDEN BAŞLAYACAK.\n\n" +
            "Bu işlem bağlantıyı kısa süre keser. Yalnızca kendi cihazınızda ve\n" +
            "bu dosyanın size ait olduğundan emin olduğunuzda kullanın.\n\nDevam edilsin mi?",
            "Cihaz yedeğini geri yükle", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (onay != MessageBoxResult.Yes) { Phase("Geri yükleme iptal edildi"); return; }

        RestoreDeviceBtn.IsEnabled = false; Phase("Cihaz konfigürasyon yedeği yükleniyor...");
        try
        {
            byte[] dosya = await File.ReadAllBytesAsync(dlg.FileName);
            using var web = new OntWeb(_ip);
            if (!(await EnsureLoggedInAsync(web, CancellationToken.None)))
            { LogLine("[Geri yükle] Oturum açılamadı"); Phase("Geri yükleme: Oturum açılamadı"); return; }

            var (ok, detay) = await web.UploadConfigRestoreAsync(dosya, Path.GetFileName(dlg.FileName), CancellationToken.None);
            LogLine("[Geri yükle] " + (ok ? "✓ Yükleme gönderildi" : "⚠ Yükleme başarısız") + " — " + detay);
            if (ok)
            {
                LogLine("[Geri yükle] Modem kendini yeniden başlatıyor — 1-2 dakika sonra 'Cihaz bilgilerini yenile' ile kontrol edin.");
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

    // -- Python keşif --
    private static string PythonBul()
    {
        foreach (var a in new[] { "python", "python3", "py" })
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(a, "-c \"print('OK')\"")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
                var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000);
                if (o.Trim() == "OK") return a;
            }
            catch { }
        }
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var y in new[]
        {
            Path.Combine(lad, "Programs", "Python", "Python313", "python.exe"),
            Path.Combine(lad, "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(lad, "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(pf,  "Python313", "python.exe"),
            Path.Combine(pf,  "Python312", "python.exe"),
        }) if (File.Exists(y)) return y;
        return null;
    }

    private static async Task<bool> WingetKur()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "install Python.Python.3.13 --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            await p.StandardOutput.ReadToEndAsync(); await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync();
            return p.ExitCode == 0;
        } catch { return false; }
    }

    private static async Task<bool> PythonIndirKur()
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "python_hekz_kur.exe");
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            await File.WriteAllBytesAsync(tmp, await http.GetByteArrayAsync("https://www.python.org/ftp/python/3.13.1/python-3.13.1-amd64.exe"));
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = tmp,
                Arguments = "/quiet InstallAllUsers=0 PrependPath=1 Include_test=0 Include_launcher=0 Shortcuts=0",
                UseShellExecute = false, CreateNoWindow = true
            })!;
            await p.WaitForExitAsync(); try { File.Delete(tmp); } catch { }
            return p.ExitCode == 0;
        } catch { return false; }
    }

    private static bool KutuphaneVar(string py, string mod)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(py, "-c \"import " + mod + "\"")
            { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        } catch { return false; }
    }
}
