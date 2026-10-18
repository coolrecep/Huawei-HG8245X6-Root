# HekzOntRace v2.0.0 — Huawei LG8245X6-50 / HG8245X6 Yönetici (Root) & Bilgi Aracı

[![Platform](https://img.shields.io/badge/Platform-Windows%20x64%20%7C%20Linux%20x64-blue.svg)](https://dotnet.microsoft.com/)
[![Framework](https://img.shields.io/badge/.NET-9.0%20(WPF%20%26%20Avalonia%20UI)-purple.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Edition](https://img.shields.io/badge/Editions-Standard%20%7C%20Lite-success.svg)]()
[![License](https://img.shields.io/badge/License-GNU%20GPL%20v2-orange.svg)](LICENSE)

**HekzOntRace**, ISP'ler (Superonline vb.) tarafından kısıtlanmış ve kilitlenmiş **Huawei LG8245X6-50 / OptiXstar HG8245X6** (ve benzeri V500R021 mimarisine sahip) Wi-Fi 6 Fiber GPON ONT / Router cihazlarında gizlenmiş yönetim şifrelerini çözen, kendi router'ınızı kullanabilmeniz için gerekli PPPoE ve Wi-Fi bilgilerini dışa aktaran ve cihaza yeni kalıcı `sUser` (Root) parolası tanımlayarak SSH/Telnet yetkilerini açan modern, çapraz platform (**Windows & Linux**) bir masaüstü uygulamasıdır.

Uygulama; hem ileri düzey kullanıcılar ve araştırmacılar için **Standart Sürüm**, hem de son kullanıcılar için sadeleştirilmiş **Lite Sürüm** olmak üzere hem **Windows (`win-x64`)** hem de **Linux (`linux-x64`)** için tek-dosya (`self-contained`) mimarisiyle sunulmaktadır.

---

## 🖥️ Desteklenen Modem Modelleri

* **Huawei LG8245X6-50** (Wi-Fi 6 GPON ONT / Router)
* **Huawei OptiXstar HG8245X6**
* **Huawei HG8245X6 Serisi** (`HG8245X6-6N`, `HG8245X6-10`, `HG8245X6-8N-30`)
* Benzer **V500R021** yazılım mimarisine sahip Huawei EchoLife / OptiXstar ONT ve Router cihazları

---

## 📌 Temel Yetenekler ve Sekmeler

### 1. Şifre ve Erişim Parametrelerini Çözme (`Sonuçlar` Sekmesi)
* **sUser (Root) Parolası:** Cihazın gizlenen ve web arayüzünde gösterilmeyen en yetkili `sUser` şifresini tespit eder, mevcut `hw_ctree.xml` PBKDF2-SHA256 hash özetiyle doğrular ve listeler.
* **PPPoE İnternet Bilgileri:** ISP tarafından verilen PPPoE kullanıcı adı ve parolasını konfigürasyon veri tabanından (`hw_ctree.xml`) AES-256 ve KMC Dollar2 (`$2`) ile çözerek kendi router'ınızı kullanmanıza olanak tanır.
* **Fabrika & Canlı Wi-Fi Bilgileri:** Hem 2.4 GHz hem de 5 GHz kablosuz ağ parolalarını ve gizli fabrika (`customizepara`) SSID/WPA anahtarlarını karşılaştırmalı olarak listeler.
* **Tek Tıkla Kopyalama & Tarayıcıda Açma:** Tüm erişim bilgilerini panoya kopyalar veya varsayılan tarayıcıda modem yönetim arayüzünü (`http://192.168.1.1`) açar.

### 2. Yeni sUser Parolası Belirleme & Akıllı Doğrulama
* **Kullanıcı Tanımlı Parola:** Kullanıcı, dilediği yeni yönetici (`sUser`) parolasını belirleyebilir.
* **Otomatik Güvenli Parola:** Parola alanı boş bırakıldığında uygulama otomatik yüksek güvenlikli rastgele parola (`HekzXXXXXXXX`) üretir.
* **🎲 Rastgele Üret Butonu:** Tek tıkla kurallara uygun rastgele parola üretip kutuya yazar.
* **Huawei LG8245X6-50 / HG8245X6 Güvenlik Filtrelemesi:**
  * En az 8, en fazla 32 karakter kontrolü.
  * Modem güvenlik politikası gereği en az bir harf (`A-Z, a-z`) ve en az bir rakam (`0-9`) zorunluluğu.
  * Boşluk yasağı ve BusyBox/CLI uyumsuzluğunu önlemek için yalnızca standart ASCII karakter kabulü (Türkçe karakter veya tırnak, ters eğik çizgi gibi komut bozan semboller engellenir).
  * Anlık (real-time) renkli durum bildirimi ve önleyici doğrulama.

### 3. Web Konfigürasyon Enjeksiyonu (Önerilen & Yarışsız Yol)
* `sUser` oturumuyla resmi `cfgfiledown.cgi` üzerinden konfigürasyonu açık metin XML olarak indirir.
* PBKDF2-SHA256 (5.000 ve 10.000 tur) ve KMC Dollar2 şifreleme algoritmalarıyla yeni parolayı enjekte eder ve CLI şifresiyle eşitler.
* `cfgfileupload.cgi` üzerinden doğrudan flash belleğe yazar ve modemi kontrollü yeniden başlatır. Fiş çekme veya USB belleğe dosya kopyalama gerekmez.
* **Akıllı Wi-Fi & Yeniden Başlatma Takibi (Kayan Yazı):**
  * Konfigürasyon modeme yüklendiği anda durum çubuğunda **Kayan Yazı (Marquee)** başlar:
    > *⚠️ Modem yeniden başlatılıyor... Wi-Fi ve internet bağlantınız geçici olarak kesilecek. Bağlantı tekrar kurulana kadar lütfen bekleyiniz...*
  * Wi-Fi ve modem bağlantısı tekrar kurulduğunda (TCP port 80 yoklamasıyla) anında tespit edilir ve başarı mesajı görüntülenir.

### 4. Donanım / Flash Enjeksiyonu (Race Modu — Standart Sürüm)
* Web yükleme yolunun kısıtlı olduğu eski yazılımlar için dosya sistemi yarış penceresi (TOCTOU) üzerinden doğrudan SPI-NAND flash belleğe yazma desteği.

### 5. Cihaz, Optik, WAN, Wi-Fi, DNS, Güvenlik, TR-069 ve Yedekleme Sekmeleri
* **Cihaz / Optik:** Model, Seri No, Donanım/Yazılım/Bootloader sürümü, MAC, Çalışma süresi, GPON ONT durumu, OLT eşleşmesi ve Optik güç değerleri (`RX dBm`, `TX dBm`, `Besleme V`, `Sıcaklık °C`, `Bias mA`).
* **WAN / PPPoE:** Bağlantı adı, durumu, WAN IP adresi, MTU, PPPoE kullanıcı adı ve çalışma süresi.
* **WiFi (2.4G / 5G):** Her iki bandın SSID, kanal, standart, şifreleme ve gizlilik durumu + tek tıkla **Çift SSID Ayrıştırma** (5 GHz bandına `_5G` eki ekleyip her iki bandı görünür yapma).
* **DNS:** Mevcut LAN DNS sunucularını görüntüleme ve hazır profillerle (**Cloudflare**, **Google**, **Quad9**, **Turkcell** veya **Özel**) DHCP DNS değiştirme.
* **Güvenlik + ALG:** Güvenlik duvarı seviyesini (`Kapalı`, `Düşük`, `Yüksek`, `Katı`) ve **ALG** geçitlerini (`FTP`, `TFTP`, `H323`, `SIP`, `RTSP`, `PPTP`, `L2TP`, `IPSec`, `RTCP`) yönetme.
* **TR-069:** Operatör uzaktan yönetimini (`CWMP` ve `Periyodik Bildirim`) tek tıkla kapatma veya yapılandırma.
* **Yedekle / Geri Yükle:** Seçili kategorileri (Wi-Fi, DNS, TR-069, Güvenlik+ALG, WAN, Cihaz) **JSON profil** olarak yedekleme/doğrulamalı geri yükleme veya cihazın resmi tam konfigürasyon yedeğini (`.bin/.cfg`) indirme ve geri yükleme.
* **Log & Hakkında:** Seviye filtreli (`Tümü`, `Yalnız önemli`, `Yalnız sorunlar`), zaman damgalı renkli günlük ekranı ve Huawei LG8245X6-50 modem bilgisi + GNU GPL v2 lisans görüntüleyici.

---

## ⚖️ Sürüm ve Platform Karşılaştırması

Dört derleme çıktısı da aynı kod tabanından üretilir ve tek bir bağımsız yürütülebilir dosya olarak çalışır:

| Özellik | Standart Sürüm (`HekzOntRace.exe` / `HekzOntRace-linux-x64`) | Lite Sürüm (`HekzOntRace-Lite.exe` / `HekzOntRace-Lite-linux-x64`) |
|---|:---:|:---:|
| **Hedef Kitle** | İleri Düzey Kullanıcılar & Araştırmacılar | Son Kullanıcılar & Hızlı Kurulum |
| **Platform Desteği** | Windows x64 (WPF) & Linux x64 (Avalonia UI) | Windows x64 (WPF) & Linux x64 (Avalonia UI) |
| **Koyu Tema & Özel Başlık Çubuğu** | ✔️ Evet (Windows DWM + Özel Chrome) | ✔️ Evet (Windows DWM + Özel Chrome) |
| **sUser & PPPoE & Wi-Fi Çözme** | ✔️ Evet | ✔️ Evet |
| **Özel sUser Parolası Belirleme** | ✔️ Evet | ✔️ Evet |
| **Web Konfigürasyon Enjeksiyonu** | ✔️ Evet | ✔️ Evet |
| **Wi-Fi / DNS / ALG / TR-069 / Yedekleme** | ✔️ Evet | ✔️ Evet |
| **Donanım / Fiş Çekmeli Race Modu** | ✔️ Açık | ❌ Gizli (Riskleri önlemek için) |
| **Bütünlük Testi (Simülasyon)** | ✔️ Açık | ❌ Gizli |
| **Teknik Log Akışı** | Ayrıntılı (Tüm iç döngüler, oturumlar, hex dökümler) | Sadeleştirilmiş (Yalnızca anlaşılır kilometre taşları) |
| **Durum Çubuğu** | Aşama aşama teknik metinler + Kayan Yazı | `Lütfen bekleyiniz...` animasyonu + Kayan Yazı |

---

## 🔬 Derin Teknik Detaylar ve Tersine Mühendislik (Reverse Engineering)

HekzOntRace, Huawei V500R021 mimarisine sahip ağ geçitlerinin iç işleyişine dayanan kapsamlı bir tersine mühendislik çalışmasının ürünüdür:

### 1. Web Oturumu ve Tek-Oturum Mekanizması (`Core/OntWeb.cs`)
- **Tek Oturum Kısıtı (Single Session Constraint):** Huawei LG8245X6-50 / HG8245X6 donanımı aynı anda yalnızca tek bir web oturumuna izin verir. Tarayıcıda açık sekme bırakıldığında veya önceki bağlantı düzgün kapatılmadığında cihaz HTTP isteklerini reddeder. `OntWeb.cs`, çerez kalıntılarını temizler, alternatif kimlik doğrulamalarını dener (`admin, sUser, root, telecomadmin`) ve oturumun canlılığını `usbhost.asp` sayfası üzerinden doğrular.
- **Ham TCP Fallback:** Bazı cihaz sürümleri `HttpClient` veya standart soket kütüphanesinin HTTP/1.1 pipeline başlıklarına tepkisiz kalabilir. Bu durumlarda uygulama doğrudan ham TCP soketi (`Socket` / `NetworkStream`) açarak HTTP paketlerini enjekte eder.

### 2. Kriptografik Mimari ve Şifreleme Algoritmaları (`Core/HuaweiCrypto.cs` & `Engine.Dollar2`)
- **AES-256-CBC Konfigürasyon Çözümü:** Cihazın ana veri tabanı olan `hw_ctree.xml`, donanımsal SoC anahtarıyla AES-256-CBC formatında şifrelenmiştir. `HuaweiCrypto.Decrypt`, ilk 8 baytlık başlık üzerinden şifre çözme işlemini gerçekleştirir ve gzip (`0x1F 0x8B`) katmanını açar.
- **KMC Dollar2 (`$2...`) Şifreli Değer Formatı:** Huawei web hesapları ve CLI parolaları doğrudan açık metin saklanmaz. `$2` ön ekiyle başlayan KMC şifreleme formatı kullanılır.
- **Çift Katmanlı PBKDF2-SHA256 Hash Yapısı:**
  - `X_HW_WebUserInfoInstance`: Kullanıcı parolası, cihazın ürettiği özel base64 `Salt` değeri kullanılarak **5.000 tur** PBKDF2-SHA256 ile özetlenir (`main Password`).
  - `X_HW_IteratePassword`: Cihazın oturum açma doğrulamasında kullandığı ikincil güvenlik katmanıdır. Kendi özel `Salt` ve `IterateCount` (genellikle **10.000 tur**) değeriyle PBKDF2-SHA256 üzerinden hesaplanır.
  - **Kanonik Salt Koruma:** Rastgele yeni salt üretmek cihazın kendi doğrulayıcısını bozduğu için, HekzOntRace cihazın orijinal Salt değerini koruyarak kanonik PBKDF2 turuyla şifreyi hesaplar.
- **Öz-Doğrulama (Self-Verification):** Oluşturulan yeni XML bloğu modeme yüklenmeden önce araç kendi `SifreDogrula` fonksiyonundan geçirilir; şifre kendi içinde doğrulanmazsa cihazın kilitlenmesini önlemek için işlem derhal iptal edilir.

### 3. Resmi Web Konfigürasyon Enjeksiyonu Mimarisi
- **Yarışsız (Race-Free) Kalıcı Yazma:** `cfgfiledown.cgi` ve `cfgfileupload.cgi` resmi arayüz uçları kullanılır. Dosya açık metin XML olarak indirilir ve resmi daemon yoluyla flash_config'e kalıcı yazılır.
- **Çift Bayraklı SSH & Telnet Açma:**
  - `AclServices` (`TELNETLanEnable="1"`, `SSHLanEnable="1"`)
  - `X_HW_CLITelnetAccess Access="1"`
  - Sadece biri açıldığında telnet/SSH devreye girmemektedir; her iki bayrağın aynı anda aktif edilmesi şarttır.
- **CLI Parola Eşitlemesi:** CLI girişi `X_HW_CLIUserInfoInstance` düğümünden gelir. Web şifresi değiştirilirken CLI şifresi de eşitlenir.
- **Root Shell Yetki Yükseltme Zinciri:**
  ```text
  ssh root@192.168.1.1 (veya telnet 192.168.1.1)
  ├── Login: sUser / <Yeni_Belirlediğiniz_Şifre>
  ├── WAP> su
  ├── SU_WAP> shell
  └── WAP(Dopra Linux) # (Tam Root Kabuğu)
  ```

### 4. Donanım / Flash Dosya Sistemi Yarış Mekanizması (TOCTOU & Python v9)
- **Samba (Port 445) ve SFTP (Port 8022) Servis Köprüsü:** Modeme takılı USB bellek üzerinden geçici servis hesapları açılır.
- **Sembolik Bağlantı (Symlink) Manipülasyonu:** `/mnt/usb/...` altındaki bir dosya `/mnt/jffs2/hw_ctree.xml` hedefine sembolik bağ (symlink) yapılır.
- **Time-of-Check to Time-of-Use (TOCTOU):** Samba dosya paylaşım servisi dosyayı okuyup doğrulamak üzere açtığı anda arka planda çalışan Python yardımcısı (v9) mutex ile sıralı yarış yazıcılarıyla `cur.bin` ve `lnk.bin` dosyalarını milisaniye hassasiyetinde takas eder.
- `WSTAT:1` yakalandığı an modem flash'ına dosya yazılır ("FİŞİ ÇEK" penceresi).

### 5. Superonline (ISP) Ağ ve VLAN Mimarisi
Kendi router'ınızı bağlamak istediğinizde gereken altyapı parametreleri:
- **WAN1 (İnternet, VoIP & Yönetim):**
  - Servis: `TR069_VOIP_INTERNET`
  - VLAN ID: `100` (802.1p Öncelik: `1`)
  - Protokol: PPPoE (IPv4), MTU: `1492`
- **WAN2 (IPTV / TV+):**
  - Servis: `IPTV`
  - VLAN ID: `103` (Multicast VLAN / MVLAN: `103`)
  - 802.1p Öncelik: `4`, Protokol: DHCP (IPv4), MTU: `1500`
- **TR-069 ACS Sunucusu:**
  - URL: `http://acs.superonline.net:8015/cwmpWeb/WGCPEMgt`
  - Kullanıcı: `superonlineacs` | OUI: `00E0FC`

### 6. Cihaz Donanım ve Çekirdek (Kernel) Özellikleri
- **SoC:** HiSilicon Dual-Core ARM Cortex-A9 (BogoMIPS: 2190.54)
- **Bellek:** 512 MB DDR3 RAM / 512 MB SPI-NAND Flash (Kioxia `TC58CVG2S0HRAIG` veya XTX `XT26G04CWSIGA`)
- **İşletim Sistemi:** Linux 4.4.240 (#1 SMP)
- **Dosya Sistemi:** SquashFS (rootfs) + UBIFS / JFFS2 (config & user data)
- **UART Seri Konsol:** `ttyAMA1` (115200 baud, 8N1)

---

## 🚀 Derleme (Build & Publish — Windows + Linux)

Proje, .NET 9 SDK kullanılarak hem **Windows (`win-x64`)** hem de **Linux (`linux-x64`)** için tamamen bağımsız (`self-contained`), sıkıştırılmış ve tek-dosya yürütülebilir formatta derlenir.

### Tek-Tık 4'lü Derleme (Önerilen)
Proje kök klasöründeki [`build.ps1`](build.ps1) betiği Windows ve Linux için Standart ve Lite sürümleri sırayla derler ve aynı çıktı dizinine (`publish_final/`) bırakır:

```powershell
# PowerShell ile tek tıkla derleme:
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Derleme çıktıları doğrudan `publish_final/` klasörüne kaydedilir:
* `publish_final\HekzOntRace.exe` (~62.8 MB — **Windows x64 Standart**)
* `publish_final\HekzOntRace-Lite.exe` (~62.8 MB — **Windows x64 Lite**)
* `publish_final\HekzOntRace-linux-x64` (~47.6 MB — **Linux x64 Standart**)
* `publish_final\HekzOntRace-Lite-linux-x64` (~47.6 MB — **Linux x64 Lite**)

### Manuel Derleme Komutları

```powershell
# 1) Windows Standart Sürüm:
dotnet publish HekzOntRace.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish_final

# 2) Windows Lite Sürüm:
dotnet publish HekzOntRace.csproj -c Release -r win-x64 --self-contained true -p:LiteMode=true -p:PublishSingleFile=true -p:DebugType=none -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish_final

# 3) Linux (x64) Standart Sürüm:
dotnet publish HekzOntRace.Linux.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish_final

# 4) Linux (x64) Lite Sürüm:
dotnet publish HekzOntRace.Linux.csproj -c Release -r linux-x64 --self-contained true -p:LiteMode=true -p:PublishSingleFile=true -p:DebugType=none -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish_final
```

---

## 🛠️ Ön Koşullar ve Çalıştırma

### Windows (10 / 11 64-bit)
1. `publish_final\HekzOntRace.exe` (Standart) veya `publish_final\HekzOntRace-Lite.exe` (Lite) dosyasını çalıştırın.
2. Sistemde Python veya `paramiko` / `pysmb` kütüphaneleri eksikse uygulama arka planda otomatik olarak (`winget` / resmi yükleyici ile) kurar.

### Linux (64-bit X11 / Wayland Masaüstü — Ubuntu, Debian, Fedora, Arch, Mint vb.)
1. Çalıştırma izni verin ve başlatın:
   ```bash
   chmod +x ./HekzOntRace-linux-x64 ./HekzOntRace-Lite-linux-x64
   ./HekzOntRace-linux-x64        # Standart Sürüm
   # veya
   ./HekzOntRace-Lite-linux-x64   # Lite Sürüm
   ```
2. **Python Çalışma Ortamı (PEP 668 Uyumlu Otomatik Kurulum):**
   - Linux sürümü sistemdeki `python3` yorumlayıcısını otomatik bulur.
   - `paramiko` ve `pysmb` paketleri sistemde kurulu değilse, modern dağıtımlardaki PEP 668 (`externally-managed-environment`) kısıtına takılmamak için `~/.local/share/hekzontrace/venv` dizininde izole bir Python sanal ortamı oluşturup paketleri otomatik yükler.
   - İsterseniz paketleri dağıtımınızın paket yöneticisiyle de kurabilirsiniz:
     ```bash
     # Debian / Ubuntu / Linux Mint:
     sudo apt install python3 python3-venv python3-pip
     ```

### Donanım & Ağ Gereksinimleri
* **Ağ Bağlantısı:** Bilgisayar modeme Ethernet kablosu veya Wi-Fi üzerinden bağlı olmalıdır (Varsayılan modem IP adresi: `192.168.1.1`).
* **USB Bellek (İlk bilgi okuma ve Race modu için):** Modemin USB portuna takılmış FAT32 veya NTFS formatlı bir USB bellek.
* **Otomatik Yedek Klasörü:** Çekilen tüm fabrika verileri, çözülmüş `hw_ctree.xml` dosyaları, JSON profilleri ve loglar otomatik olarak `Masaüstü/HekzONT` (`~/Desktop/HekzONT` veya `~/Masaüstü/HekzONT`) klasörüne kaydedilir.

---

## 📖 Kullanım Adımları

1. **Uygulamayı Açın:**
   - Hızlı ve kolay işlem için **Lite** sürümü (`HekzOntRace-Lite.exe` / `HekzOntRace-Lite-linux-x64`) çalıştırın.
   - İleri seviye araçlar ve detaylı log takibi için **Standart** sürümü (`HekzOntRace.exe` / `HekzOntRace-linux-x64`) çalıştırın.
2. **Cihaza Bağlanın:**
   - Modem IP adresi (varsayılan `192.168.1.1`), kullanıcı adı (`admin`) ve web şifresini (boş bırakılırsa `superonline` denenir) kontrol edin.
   - **BAŞLAT** butonuna tıklayın.
3. **Bilgileri Alın:**
   - Birkaç saniye içinde `sUser` yönetici şifreniz, PPPoE internet kullanıcı adı/parolanız ve Wi-Fi şifreniz çözülerek ekrandaki kutulara doldurulur; tüm sekmeler (Cihaz/Optik, WAN, Wi-Fi, DNS, Güvenlik+ALG, TR-069) güncellenir.
   - **📋 Tüm Bilgileri Kopyala** butonu ile bilgilerinizi güvenle arşivleyebilirsiniz.
4. **(İsteğe Bağlı) Yeni sUser Parolası Belirleyin:**
   - `Yeni sUser Parolası` kutusuna dilediğiniz parolayı yazın (veya boş bırakarak otomatik atanmasını sağlayın).
   - **⚡ Web Konfigürasyon Enjeksiyonu (Önerilen)** / **⚡ Yeni sUser Parolasını Güvenle Kaydet** butonuna tıklayın.
   - Modemin yeniden başlama süreci durum çubuğundan takip edilecek ve Wi-Fi tekrar bağlandığında yeni şifreniz aktifleşecektir.

---

## 🛡️ Yasal ve Teknik Dipnot

Bu yazılımda modem üreticisine veya servis sağlayıcıya ait orijinal firmware ikili dosyaları (firmware binary), gizli anahtarlar veya telif hakkı içeren yapılandırma dosyaları **bulunmamaktadır**.

Tüm araçlar, kullanıcının meşru sahibi olduğu cihaz üzerindeki konfigürasyon verilerini kendi yerel ortamında (offline / local) analiz etmek, ağ kimlik bilgilerini yedeklemek ve cihaz üzerinde idari kontrol sağlamak amacıyla GNU Genel Kamu Lisansı v2 (GPL-2.0) şartları altında geliştirilmiştir.
