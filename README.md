# Huawei HG8245X6 — Interoperability & Diagnostic Research

Bu proje, kullanıcıların yasal olarak sahip oldukları Huawei HG8245X6 (ve benzeri V500R021 serisi) Fiber ONT cihazları üzerinde tam kontrol sağlamalarına, kendi yerel ağlarında (LAN) bağımsız yönetim ve tanılama (diagnostic) işlevlerini kullanabilmelerine olanak tanımak amacıyla geliştirilmiştir.

Geliştirilen araçlar, cihazın yerel teşhis ve bağımsız yedekleme işlevlerini restore etmek içindir. Proje, **birlikte çalışabilirlik (interoperability)** ve **donanım sahipliği hakkı (right to repair)** çerçevesinde yürütülmektedir.

> **Feragatname:** Bu depodaki bilgiler tamamen eğitim ve güvenlik araştırmaları amacıyla paylaşılmıştır. Yapacağınız işlemler cihazınızı garanti dışı bırakabilir. Tüm sorumluluk işlemi yapan kişiye aittir.

> **Sorumlu İfşa (Responsible Disclosure):** Bu projede tespit edilen güvenlik bulguları, ilgili kurumlara (üretici ve servis sağlayıcı) raporlanmıştır. Burada paylaşılan bilgiler, açıkların kapatılmasına katkı sağlamak amacıyla yayınlanmaktadır.

## 📌 İçindekiler
- [Donanım ve Yazılım Bilgileri](#donanım-ve-yazılım-bilgileri)
- [Konfigürasyon Erişimi ve Yerel Yönetim](#konfigürasyon-erişimi-ve-yerel-yönetim)
- [Cihaz İçinden Canlı Firmware Dökümü](#cihaz-içinden-canlı-firmware-dökümü)
- [NAND ve SquashFS Analizi](#nand-ve-squashfs-analizi)
- [WAP Diagnostic Komut Referansı](#wap-diagnostic-komut-referansı)
- [Kriptografik Mimari Notları](#kriptografik-mimari-notları)

---

## 💻 Donanım ve Yazılım Bilgileri (Device Specifications)

* **Cihaz Modeli:** Huawei OptiXstar HG8245X6 GPON Terminal
* **Donanım Özellikleri:** GPON 4*GE + 2.4G/5G Wi-Fi + 2POTS + 1USB
* **İşlemci (SoC):** HiSilicon A9 (Çift Çekirdekli ARM Cortex-A9, BogoMIPS: 2190.54)
* **RAM / Flash:** 512 MB / 512 MB (SPI-NAND)
* **NAND Flash Bellek (512 MB SPI-NAND):** Cihazın üretim partisine göre iki farklı NAND çipi tespit edilmiştir:
  * Varyasyon 1: **Kioxia/Toshiba `TC58CVG2S0HRAIG`**
  * Varyasyon 2: **XTX Technology `XT26G04CWSIGA`**
* **PCB (Anakart):** DN8245XA
* **Donanım Sürümü (Hardware Version):** 1E8E.A
* **Çıkış Tarihi (Release Time):** 2022-03-16

### ⚙️ İşletim Sistemi, Çekirdek ve Dosya Sistemi Detayları

* **Product Version:** V5R021C00S128
* **Firmware İmza Yapısı (Magic Header):** `whwh`
* **Kernel Version:** Linux 4.4.240
* **Dosya Sistemi:** SquashFS (rootfs) + UBIFS / JFFS2 (config & app data)
  * **Sıkıştırma Algoritmaları:** LZMA ve LZO

---

## 🔬 GPON ve Optik Modül (Fiber) Detayları
Cihaz, anakarta entegre (BOB - BOSA On Board) bir optik modül kullanmaktadır.
* **Optik Sınıfı:** CLASS B+
* **Vendor PN:** HW-BOB-0008
* **Fiber Teşhis Komutu:** `display optic` (Rx/Tx güçlerini, sıcaklığı ve voltajı gösterir)

---

## 🌐 Ağ Mimarisi ve VLAN Bilgileri
Kendi router'ınızı kullanmak isterseniz bu VLAN kimliklerine ihtiyacınız olacaktır:

### WAN1 (İnternet, VoIP ve Yönetim)
* **VLAN ID:** `100`
* **802.1p (Öncelik):** `1`
* **Protokol:** PPPoE (IPv4)
* **MTU:** 1492

### WAN2 (TV+ / IPTV)
* **VLAN ID:** `103` (Multicast VLAN: 103)
* **802.1p (Öncelik):** `4`
* **Protokol:** DHCP (IPv4)
* **MTU:** 1500

---

## 🔌 UART Boot Log Analizi

Cihazın JTAG/UART pinleri üzerinden alınan önyükleme günlüğü:

* **UART Konsol:** `ttyAMA1`
* **Baud Rate:** `115200`
* **RAM:** `512 MB` (494M Kullanılabilir)
* **RootFS Başlangıç Ofseti:** `0x28c094`
* **Fiziksel NAND Mimarisi:** Fiziksel olarak çip 2 bölümdür: 2MB Bootcode (`0x200000`) ve UBI Katmanı (`0x1fe00000`). Geri kalan tüm MTD blokları UBI üzerinde çalışan mantıksal bölümlerdir.
* UART üzerinden dinleme yapılabiliyor fakat MCU tarafından komut alımı kapalı.

```text
Start in Safetycode
Hard rst reason:0x3c000000
End out Safetycode

Start in Statcode
startcode uboot boot count:-1
Start in Safe Mode
Use the AllsytemA to load success
End out startcode

Start in U-boot
Main area: Cert partition Found
Slave area: Cert partition Found
End out U-boot

Uncompressing... done, booting...
```

---

## 🔑 Konfigürasyon Erişimi ve Yerel Yönetim

### Konfigürasyon Dosyası ile Yerel Erişim Açma

Cihazın yerel tanılama arayüzünü (Telnet/SSH) etkinleştirmek için konfigürasyon dosyası düzenlenmektedir. Bu yöntem cihazın kendi gizli menüsünü kullanır.

*Gizli konfigürasyon sayfası bağlantısını keşfeden ve paylaşan **Ferdi Burak**'a teşekkürler.*

**Adım 1: Yapılandırma Dosyasını İndirme**

Tarayıcınız üzerinden cihazın konfigürasyon sayfasına giriş yapın:
```text
http://192.168.1.1/html/ssmp/cfgfile/cfgfile.asp
```
Bu sayfa üzerinden cihazın mevcut yapılandırma dosyasını (`hw_ctree.xml`) bilgisayarınıza indirin.

**Adım 2: XML Dosyasını Düzenleme**

İndirdiğiniz dosyayı bir metin editörü ile açın ve `<AclServices` etiketini bulun. Yerel ağ için Telnet ve SSH erişimini açmak için ilgili değerleri `1` olarak değiştirin:

```xml
<AclServices HTTPLanEnable="1" HTTPWanEnable="0" FTPLanEnable="0" FTPWanEnable="0" TELNETLanEnable="1" TELNETWanEnable="0" SSHLanEnable="1" SSHWanEnable="0" HTTPPORT="80" FTPPORT="21" TELNETPORT="23" SSHPORT="22" HTTPWifiEnable="1" TELNETWifiEnable="0">
```

**Adım 3: Düzenlenen Dosyayı Geri Yükleme**

Dosyayı kaydedin ve yine aynı sayfa üzerinden cihaza **Upload (Restore)** edin. Yükleme tamamlandıktan sonra modemi yeniden başlatın.

### Sisteme Giriş (Diagnostic Shell Erişimi)

Telnet/SSH erişimi aktif olduktan sonra terminal üzerinden bağlanabilirsiniz:

```bash
telnet 192.168.1.1
```

**Kimlik Bilgileri:** Cihazınıza özgü `sUser` şifresi, konfigürasyon dosyasındaki `customizepara` bölümünden okunabilir. Şifre, cihazın donanım seri numarasına bağlı olarak üretilmektedir. Bu proje kapsamında geliştirilen **HekzOntRace** aracı, bu şifreyi cihazınızdan otomatik olarak çözümler.

Giriş yaptıktan sonra `WAP>` (Huawei Diagnostic Shell) konsoluna düşeceksiniz. Tam yetkili moda geçmek için:

```bash
WAP> su
Password:
SU_WAP> shell
#
```

---

## 💾 Cihaz İçinden Canlı Firmware Dökümü

Root yetkisi elde ettikten sonra tüm MTD bloklarını ağ üzerinden bilgisayara aktarabilirsiniz.

Bölümleri listeleyin:
```bash
cat /proc/mtd
```

**Ağ üzerinden Dump Alma (Örnek: `mtd6`):**
1. **Bilgisayarınızda** dinlemeyi başlatın:
   ```bash
   nc -l -p 4444 > mtd6_allsystemA.bin
   ```
2. **Modem Terminalinde** veriyi gönderin:
   ```bash
   cat /dev/mtd6 | busybox nc 192.168.1.20 4444
   ```

### USB Bellek Üzerinden Döküm Alma
```bash
for i in 0 1 2 3 4 5 6 7 8 9 10 11 12; do dd if=/dev/mtd$i of=/mnt/usb/usb-device/mtd${i}_dump.bin; echo "mtd$i done"; done; sync
```

---

## 🔬 NAND ve SquashFS Analizi

* **HWNP vs whwh Formatı:** Yeni nesil Huawei firmware'leri `HWNP` yerine `whwh` imza formatını ve `SIGNINFO` sarmalayıcısını kullanır.
* **SquashFS Koruması (SEC_SQS):** Ana SquashFS imajı standart araçlarla açılmaya çalışıldığında ID tabloları ve fragment blok işaretçileri kasıtlı olarak bozularak korunmuştur. `sasquatch` (Huawei imza yapısına duyarlı fork) ile açılabilir.

## 🗂️ NAND Bellek Haritası (MTD Partition Layout)

| Partition | Boyut | Açıklama |
| :--- | :--- | :--- |
| **`mtd0`** | N/A | Bootloader / U-Boot (Çekirdek tarafından kilitli) |
| **`mtd1`** | 510.0 MB | UBI Master — tüm alt bölümleri kapsayan ana blok |
| **`mtd2`** | 248.0 KB | `flash_configA` (Birincil XML yapılandırmaları) |
| **`mtd3`** | 248.0 KB | `flash_configB` (Yedek XML yapılandırmaları) |
| **`mtd4`** | 248.0 KB | `slave_paramA` (Birincil operatör parametreleri) |
| **`mtd5`** | 248.0 KB | `slave_paramB` (Yedek operatör parametreleri) |
| **`mtd6`** | 80.2 MB | `allsystemA` — Aktif Kernel ve SquashFS |
| **`mtd7`** | 80.2 MB | `allsystemB` — Yedek İşletim Sistemi imajı |
| **`mtd8`** | 248.0 KB | `board_info` (MAC, SN ve donanım kimlikleri) |
| **`mtd9`** | 248.0 KB | RF / WLAN Kalibrasyon verileri |
| **`mtd10`** | 2.2 MB | Sistem logları ve crash dump raporları |
| **`mtd11`** | 20.1 MB | `file_system` / JFFS2 (Kullanıcı ayarları) |
| **`mtd12`** | 295.2 MB | `app_system` (Ekstra uygulama bileşenleri) |

---

## 🛠️ WAP Diagnostic Komut Referansı

Telnet üzerinden `SU_WAP>` kabuğunda kullanılabilecek tanılama komutları:

> ⚠️ **DİKKAT:** `set`, `clear`, `restore` ve `reset` komutları cihazın konfigürasyonunu kalıcı olarak bozabilir. Ne yaptığınızı bilmiyorsanız sadece `display` ve `get` komutlarını kullanın.

### 📊 Sistem ve Cihaz Bilgisi
* `display deviceInfo` — Kapsamlı cihaz bilgisi (Model, SN, MAC, Uptime)
* `display cpu info` — İşlemci yük durumu
* `display memory detail` — RAM kullanım haritası
* `display inner version` / `display version` — Yazılım versiyonu
* `display board-temperatures` — Anakart sıcaklık sensörleri
* `display flashlock status` — Flash bellek kilit durumu

### 📡 Fiber Optik ve GPON
* `display optic` — Optik modül anlık değerleri (Sıcaklık, Voltaj, Tx/Rx Gücü)
* `get optic par info` — Lazer modülü marka/model bilgileri
* `display pon statistics` — PON hattı istatistikleri
* `display onu info` — ONT kayıt durumu

### 🌐 Ağ ve Yönlendirme
* `display waninfo all detail` — WAN portları, VLAN ID'leri ve durumları
* `display ip route` — IPv4 Yönlendirme tablosu
* `display ip interface` — Ağ arayüzleri ve IP adresleri
* `display dhcp server pool all` — DHCP havuzu ve bağlı cihazlar
* `ifconfig` / `ping` / `traceroute` / `netstat -na` — Standart ağ araçları

### 📶 Wi-Fi ve Kablosuz Ağ
* `display wifichip` — Wi-Fi donanım durumu
* `display wifi radio` — 2.4GHz ve 5GHz kanal durumları
* `display wifi associate` — Bağlı cihaz listesi ve sinyal kaliteleri

### ☎️ VoIP ve Ses Hizmetleri
* `display voip info` — SIP hesap bilgileri ve kayıt durumu

### 🐞 Hata Ayıklama
* `trafficdump` — Cihaz üzerinden ağ trafiği dinleme
* `display log info` / `display syslog` — Sistem olay günlükleri

### 💻 Sistem Kontrolü
* `shell` — WAP kabuğundan tam yetkili Dopra Linux terminaline geçiş
* `reset` — Modemi yeniden başlatır
* `save data` — RAM'deki değişiklikleri Flash'a kalıcı olarak yazar

---

## 🔐 Kriptografik Mimari Notları

### Yetki Hiyerarşisi
Cihazda üç adet yetki seviyesi bulunmaktadır:
1. **admin** — Kısıtlı web arayüzü (varsayılan ISP kullanıcısı)
2. **sUser** — Tanılama kabuğu ve gelişmiş yönetim erişimi
3. **srv_ssmp** — En yüksek sistem yetkisi (çekirdek servisleri)

Şifreler PBKDF2 algoritması ile hashlenmiş ve donanıma gömülü AES anahtarıyla şifrelenmiştir.

### KMC Şifreleme Mimarisi
Cihaz, KMC (Key Management Center) tabanlı çok katmanlı bir şifreleme mimarisi kullanmaktadır. Nihai AES anahtarı, KMC Master Key ile bir sistem sabiti birleştirilerek SHA-256'dan türetilmektedir:

```text
AES_KEY = SHA-256( KMC_Master_Key || VENDOR_SECRET )
```

> **Not:** Araştırma etiği gereği, sistem sabiti (vendor secret) bu depoda açık olarak paylaşılmamaktadır. Kendi cihazınızdan statik analiz yoluyla elde edebilirsiniz.

### RSA Zafiyet Bulgusu
Cihazın `sUser` kimlik doğrulama mekanizmasında kullanılan `su_pub_key` dosyasının, modern standartların çok altında (~256-bit) bir RSA modülü kullandığı tespit edilmiştir. Bu zayıflık nedeniyle açık anahtar, standart bir bilgisayarda SIQS algoritması ile kısa sürede çarpanlarına ayrılabilmektedir.

> **Not:** Asal çarpanlar araştırma etiği gereği burada paylaşılmamaktadır.

### Firmware İmza Güvenliği (signinfo_v5)
Yeni nesil cihazlar `ASN.1 DER PKCS#7 SignedData` ve `Huawei Code Signing CA 2` X.509 kök sertifikası ile donanım seviyesinde doğrulama yapmaktadır. Bu nedenle sahte firmware üretmek matematiksel olarak mümkün değildir.

---

## 🛡️ Yasal Çerçeve

Bu proje, aşağıdaki yasal çerçeveler kapsamında yürütülmektedir:

- **AB Yazılım Direktifi (2009/24/EC) Madde 6:** Birlikte çalışabilirlik amacıyla tersine mühendisliğe izin verir.
- **ABD DMCA §1201(f):** Interoperability araştırması için sınırlı muafiyet sağlar.
- **Right to Repair (Onarım Hakkı):** Kullanıcının yasal olarak sahip olduğu donanım üzerindeki kontrol hakkı.

Bu depoda üreticiye ait orijinal firmware, yapılandırma dosyaları veya ticari sırlar bulundurmamaktadır. Tüm araçlar, kullanıcının **kendi cihazından** elde edeceği veriler üzerinde **lokal olarak** çalışacak şekilde tasarlanmıştır.

---

## 📄 Lisans

Bu proje [GNU General Public License v3.0](LICENSE) ile lisanslanmıştır.
