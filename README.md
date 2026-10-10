# Huawei HG8245X6 Root Guide

Bu depo, Superonline (veya diğer ISP'ler) tarafından kilitlenmiş Huawei HG8245X6 (ve benzeri V500R021 serisi) Fiber ONT / Router cihazlarında tam root erişimi (Telnet/SSH) elde etme, donanımsal NAND dökümü alma, şifreli Firmware yapısını (whwh/HWNP) çözme ve konfigürasyon dosyalarını manipüle etme süreçlerini adım adım açıklamaktadır.

⚠️ **Uyarı / Feragatname:** Bu depodaki bilgiler tamamen eğitim ve güvenlik araştırmaları (Reverse Engineering) amacıyla paylaşılmıştır. Yapacağınız işlemler (özellikle Firmware güncellemesi ve MTD bloklarına müdahale) cihazınızı garanti dışı bırakabilir veya "brick" (kullanılamaz) hale getirebilir. Tüm sorumluluk işlemi yapan kişiye aittir.

## 📌 İçindekiler
- [Donanım ve Yazılım Bilgileri](#donanım-ve-yazılım-bilgileri)
- [Ganimet: Şifreler ve Kritik Bilgiler](#ganimet-şifreler-ve-kritik-bilgiler)
- [Yöntem 2: Cihaz İçinden Canlı Firmware Dökümü (Live Dump)](#yöntem-2-cihaz-içinden-canlı-firmware-dökümü)
- [NAND ve SquashFS Analizi (Tersine Mühendislik Notları)](#nand-ve-squashfs-analizi)

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
* **Üretim Bilgisi (Production Info):** 2150084230HYM3000324.C402
* **Çıkış Tarihi (Release Time):** 2022-03-16

### ⚙️ İşletim Sistemi, Çekirdek ve Dosya Sistemi Detayları

* **Product Version:** V5R021C00S128
* **Tespit Edilen İç Sürümler (Inner / Main Versions):**
  * `V500R021C00SPC128B125` *(Payload analizlerinde incelenen sürüm)*
  * `V500R021C00SPC128B130` *(Firmware yapısında tespit edilen ara sürüm)*
  * `V500R021C00SPC128B345` *(İkinci/Güncel cihazda tespit edilen ana sürüm)*
* **Firmware İmza Yapısı (Magic Header):** `whwh`
* **U-Boot Version:** HiSilicon U-Boot *(Çekirdek tarafından mtd0 kilitli)*
* **Kernel Version:** Linux 4.4.240 (#1 SMP Fri Dec 17 01:10:57 CST 2021)
* **Kriptografi Mimarisi:** SoC / CPU içerisine donanımsal olarak gömülü (Hardware-assisted) şifreleme ve anahtar saklama yöntemleri.
* **Dosya Sistemi (Filesystem) ve Çıkarma Detayları:** SquashFS (rootfs) + UBIFS / JFFS2 (config & app data)
  * **Sıkıştırma Algoritmaları:** LZMA ve `liblzo2-2` (LZO desteği)
  * **Başarılı Çıkarma/Açma Araçları:** `unsquashfs 4.7.5` ve `sasquatch` (Huawei imza yapısına duyarlı fork!)
* **Build Toolchain:** gcc version 7.3.0 (Compiler CPU V200R006C10SPC010B002)

---

## 🔬 GPON ve Optik Modül (Fiber) Detayları
Cihaz, anakarta entegre (BOB - BOSA On Board) bir optik modül kullanmaktadır.
* **Optik Sınıfı:** CLASS B+
* **Vendor PN:** HW-BOB-0008
* **SOC Version:** 22
* **Fiber Teşhis Komutu:** `display optic` (Rx/Tx güçlerini, sıcaklığı ve voltajı gösterir)

---

## 🌐 Superonline (ISP) Ağ Mimarisi ve VLAN Bilgileri
Cihazın WAN arayüzü (`display waninfo all detail`) ve TR-069 yapılandırması incelendiğinde Superonline'ın kullandığı ağ mimarisi şu şekildedir. Kendi router'ınızı kullanmak isterseniz bu VLAN kimliklerine ihtiyacınız olacaktır:

### WAN1 (İnternet, VoIP ve Yönetim)
* **Servis Türü:** `TR069_VOIP_INTERNET`
* **VLAN ID:** `100`
* **802.1p (Öncelik):** `1`
* **Protokol:** PPPoE (IPv4)
* **MTU:** 1492

### WAN2 (TV+ / IPTV)
* **Servis Türü:** `IPTV`
* **VLAN ID:** `103` (Multicast VLAN / MVLAN: 103)
* **802.1p (Öncelik):** `4`
* **Protokol:** DHCP (IPv4)
* **MTU:** 1500

### TR-069 (Uzaktan Yönetim - ACS)
* **ACS Sunucu URL:** `http://acs.superonline.net:8015/cwmpWeb/WGCPEMgt`
* **ACS Kullanıcı Adı:** `superonlineacs`
* **CPE OUI:** `00E0FC` (Huawei Technologies)

### 🥷 Çekirdek Açılış Parametreleri (Kernel Boot Cmdline)

Cihazın `dmesg` log okuma yetkisi (klogctl) donanımsal olarak kilitlenmiştir. Ancak `/proc/cmdline` üzerinden alınan boot parametreleri, cihazın UART ve dosya sistemi mimarisini açıkça ortaya koymaktadır:

`noalign mem=494M flashsize=0x20000000 console=ttyAMA1,115200 root=/dev/mtdblock7 rootflags=image_off=0x28c094 rootfstype=squashfs mtdparts=hinand:0x200000(bootcode)raw,0x1fe00000(ubilayer_v5) ubi.mtd=1 maxcpus=2 flash_chip=spinand`

**Donanım Hackleme (UART) İçin Önemli Notlar:**
* **UART Konsol:** `ttyAMA1`
* **Baud Rate:** `115200`
* **RAM:** `512 MB` (494M Kullanılabilir)
* **RootFS Başlangıç Ofseti:** `0x28c094` (SquashFS doğrudan bu ofsetten başlar)
* **Fiziksel NAND Mimarisi:** Fiziksel olarak çip sadece 2 bölümdür. 2MB Bootcode (`0x200000`) ve UBI Katmanı (`0x1fe00000`). Geri kalan tüm MTD blokları UBI üzerinde çalışan mantıksal bölümlerdir.
* UART üzerinden dinleme yapılabiliyor fakat MCU tarafından komut alımı kapalı.

---

#### 🔌 UART Boot Log Analizi (Seri Port Neden Sağırlaşıyor?)

Cihazın JTAG/UART pinleri üzerinden alınan ham önyükleme (boot) günlüğü incelendiğinde, cihazın donanımsal güvenlik önlemleri ve UART terminalinin neden aniden kapandığı net bir şekilde görülmektedir:

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
[    0.273976] mtdoops: mtd device (mtddev=name/number) must be supplied
[    3.347413] ubi0 error: do_sync_erase: cannot erase PEB 1041, error -5
[    3.354166] ubi0 error: __erase_worker: failed to erase PEB 1041, error -5
[    3.383291] ** Total Boot time: 3383 ms, uncompress initrd cost 0 ms **
insmod: can't insert '/lib/modules/linux/kernel/drivers/tty/serial/serial_core.ko': No such file or directory
insmod: can't insert '/lib/modules/linux/kernel/drivers/tty/serial/8250/8250.ko': No such file or directory
insmod: can't insert '/lib/modules/linux/kernel/drivers/tty/serial/8250/8250_dw.ko': No such file or directory
```

**Donanım Hackleme (UART) İçin Önemli Notlar:**
* **UART Konsol:** `ttyAMA1`
* **Baud Rate:** `115200`
* **RAM:** `512 MB` (494M Kullanılabilir)
* **RootFS Başlangıç Ofseti:** `0x28c094` (SquashFS doğrudan bu ofsetten başlar)
* **Fiziksel NAND Mimarisi:** Fiziksel olarak çip sadece 2 bölümdür. 2MB Bootcode (`0x200000`) ve UBI Katmanı (`0x1fe00000`). Geri kalan tüm MTD blokları UBI üzerinde çalışan mantıksal bölümlerdir.
* UART üzerinden dinleme yapılabiliyor fakat MCU tarafından komut alımı kapalı.

Başlangıçta cihazın 22 (SSH) ve 23 (Telnet) portları dışarıdan erişime tamamen kapalıdır (Filtered/Closed). Web arayüzü (`admin` hesabı) üzerinden Telnet açma veya yapılandırma indirme menüleri gizlenmiştir.

---

## 🗝️ Ganimet: Şifreler ve Kritik Bilgiler
KMC (Key Management Center) deşifre süreçleri ve NAND dökümünün UBIFS bölümlerinden (`volume_9.raw` ve `hw_ctree.xml`) elde edilen açık metin şifreler ve kimlik bilgileri:

| Tür | Kullanıcı Adı | Şifre / Değer |
| :--- | :--- | :--- |
| **Telnet / CLI (Root)** | `sUser` / `root` | `EP!99R4HLH9E` *(Standart HG8245X6)*<br>`U3YELC4J#X39` *(HG8245X6-10 Varyantı)* |
| **Web Arayüzü (Kısıtlı)** | `admin` | `superonline` |
| **TR-069 ACS Sunucu** | `superonlineacs` | `$2M\xR1w!!W0j{'78t...` (AES Encrypted) |

*(Not: Admin arayüzü şifreleri PBKDF2 algoritması ile 10000 iterasyonla hashlenmiştir.)*

### 🔓 Kriptografik Zafiyet: `su_pub_key` (RSA-77) Kırılması

Dosya sisteminden elde edilen ve `sUser` kabuğunun şifre/kimlik doğrulamasında (Daily Password / Challenge-Response) kullanılan `su_pub_key` dosyasının, modern güvenlik standartlarına aykırı olarak çok zayıf bir **77 basamaklı (~256-bit) RSA modülü** kullandığı tespit edilmiştir.

Bu zayıflık sayesinde, genel anahtar (Public Key) SIQS (Self-Initializing Quadratic Sieve) algoritması kullanılarak standart bir bilgisayarda sadece **58 saniyede** asal çarpanlarına ayrılmış ve sistemin Özel Anahtarı (Private Key) tamamen ele geçirilmiştir.

**SIQS Kriptanaliz Çıktısı:**
```text
starting SIQS on c77: 93047119368797069533900709356153666374682780211774131252649219508533058394837

==== sieving in progress (1 thread):   36224 relations needed ====
36330 rels found: 17915 full + 18415 from 192870 partial, (4062.93 rels/sec)

SIQS elapsed time = 52.4544 seconds.
Total factoring time = 58.9546 seconds

**factors found**
P39 = 297098113301310309198580524816784910303
P39 = 313186503727240930873981527043146130379
```
---

### 🧩 KMC Şifreleme Algoritması ve Hard-coded Vendor Secret

Firmware üzerinde yapılan derinlemesine statik analizler sonucunda (Ghidra/IDA), Huawei'nin KMC (Key Management Center) şifreleme mimarisinde kullandığı **sistem geneli sabit bir gizli değer (Hard-coded Vendor Secret)** keşfedilmiştir.

Bellekte `DAT_b48f8` adresinde saklanan bu sabit değer: `Df7!ui%s9(lmV1L8`

Sistemdeki nihai şifreleme/deşifreleme işlemleri doğrudan KMC Master Key ile yapılmamaktadır. Sistem, şifreli verileri çözecek olan nihai **AES Anahtarını** elde etmek için Master Key ile bu gizli sabiti uç uca ekleyip (Concatenation) SHA-256 algoritmasından geçirmektedir.

**Nihai AES Anahtarı (AES_KEY) Türetme Formülü:**
```text
AES_KEY = SHA-256( KMC_Master_Key || "Df7!ui%s9(lmV1L8" )
```

### 3. Telnet ve SSH Erişimini Açma (XML Konfigürasyon Dosyası)

> **Not:** Önceki analizlerimizde Telnet/SSH erişiminin modifiye bir BIN dosyası (R22 vb.) ile açılabileceğini düşünmüştük. Ancak güncel ve herkes için çalışacak evrensel yöntem, cihazın gizli bir menüsünden konfigürasyon dosyasının çekilip düzenlenmesidir.
>
> *Gizli konfigürasyon sayfası bağlantısını keşfeden ve paylaşan **Ferdi Burak**'a teşekkürler.*

**Adım 1: Yapılandırma Dosyasını İndirme**

Tarayıcınız üzerinden cihazın aşağıdaki gizli konfigürasyon sayfasına giriş yapın:
```text
[http://192.168.1.1/html/ssmp/cfgfile/cfgfile.asp](http://192.168.1.1/html/ssmp/cfgfile/cfgfile.asp)
```
Bu sayfa üzerinden cihazın mevcut yapılandırma dosyasını (`hw_ctree.xml`) bilgisayarınıza indirin.

**Adım 2: XML Dosyasını Düzenleme**

İndirdiğiniz dosyayı bir metin editörü (Notepad++, VS Code vb.) ile açın ve `<AclServices` etiketini bulun. Dosyanın orijinal halinde yerel ağ için Telnet ve SSH kapalı durumdadır (`TELNETLanEnable="0"` ve `SSHLanEnable="0"`). 

Erişimi açmak için bu değerleri `1` olarak değiştirin:

```xml
<AclServices HTTPLanEnable="1" HTTPWanEnable="0" FTPLanEnable="0" FTPWanEnable="0" TELNETLanEnable="1" TELNETWanEnable="0" SSHLanEnable="1" SSHWanEnable="0" HTTPPORT="80" FTPPORT="21" TELNETPORT="23" SSHPORT="22" HTTPWifiEnable="1" TELNETWifiEnable="0">
```
*(İsteğinize bağlı olarak `FTP` veya `HTTPWan` gibi diğer yetkileri de bu satır üzerinden yönetebilirsiniz.)*

**Adım 3: Düzenlenen Dosyayı Geri Yükleme**

Dosyayı kaydedin ve yine indirme yaptığınız `http://192.168.1.1/html/ssmp/cfgfile/cfgfile.asp` sayfası üzerinden modifiye ettiğiniz dosyayı cihaza **Upload (Restore)** edin. Yükleme tamamlandıktan sonra ayarların aktif olması için modemi yeniden başlatın.

---

### 4. Sisteme Giriş (Root ve Shell Erişimi)

Cihaz yeniden açıldıktan ve Telnet/SSH erişimi aktif olduktan sonra Terminal (veya PuTTY) kullanarak cihaza bağlanabilirsiniz.

**Terminal üzerinden bağlanmak için:**
```bash
telnet 192.168.1.1
```

**Kimlik Bilgileri:**
- **Login:** `sUser`
- **Password:** `EP!99R4HLH9E`

Giriş yaptıktan sonra `WAP>` (Huawei Diagnostic Shell) konsoluna düşeceksiniz. Tam yetkiye (root) sahip olmak için ayrıcalık yükseltmemiz gerekiyor:

1. `su` komutunu girin.
2. Şifreyi (`EP!99R4HLH9E`) tekrar girerek `SU_WAP>` yetkili moduna geçin.
3. Son olarak `shell` yazarak Huawei'nin arka planda çalışan Dopra Linux kök işletim sistemine (`#`) düşün.

```bash
WAP> su
Password:
SU_WAP> shell
#
```

Artık cihazın tüm dosya sistemine tam (root) erişim sağladınız!

---

### 5. R22.bin Tersine Mühendislik Analizi (Referans)

*Bu bölüm, cihaza doğrudan dosya iterek (push) kalıcı değişiklikler yapmayı hedefleyen geliştiriciler ve exploit çalışmaları için referans olarak arşivlenmiştir.*

Şu an elimizde, cihazda root/telnet erişimi sağlamak için kullanılan resmi veya sızdırılmış bir araç setine ait **R22.bin** dosyası mevcut. 
- **Dosya Linki:** [Google Drive - R22.bin](https://drive.google.com/file/d/1736o4JLuJ6KGjDFAysB3yZH_gSLwmJYN/view)

**R22.bin (Telnet Enablement Payload):**
Bu dosya aslında standart bir firmware güncellemesi değildir. İçerisinde router'ı kandırıp özel scriptler çalıştırmasını sağlayan modifiye edilmiş bir HWNP formatlı güncelleme paketidir. Paketi parçaladığımızda (unpack) şu kritik dosyalar ortaya çıkıyor:
- `var/duit9rr.sh` (Ana Exploit Scripti)
- `mnt/jffs2/TelnetEnable`
- `var/UpgradeCheck.xml`

**Exploit'in Çalışma Mantığı (`duit9rr.sh`):**
Router bu `R22.bin` dosyasını bir firmware güncellemesi sanarak kabul ettiğinde, içerisindeki `duit9rr.sh` scripti çalışır. Bu script şu işlemleri yapar:
1. **Şifre Çözme:** Router'ın ana konfigürasyon dosyasını (`/mnt/jffs2/hw_ctree.xml`) Huawei'nin gömülü `aescrypt2` aracını kullanarak geçici bir dosyaya çözer.
2. **Parametre Değiştirme:** `cfgtool` aracını kullanarak konfigürasyon içerisindeki `InternetGatewayDevice.X_HW_Security.AclServices TELNETLanEnable 1` gibi parametreleri zorla değiştirir.
3. **Yeniden Şifreleme:** Değiştirilen konfigürasyon dosyasını tekrar şifreler ve orijinal dosyanın üzerine yazar.
4. **Temizlik:** İzleri siler ve kendini imha eder.

---

## 💾 Yöntem 2: Cihaz İçinden Canlı Firmware Dökümü
Fiziksel donanıma (SPI/NAND programlayıcı) ihtiyaç duymadan, Root yetkisi elde ettikten sonra tüm blokları (MTD) ağ üzerinden doğrudan bilgisayara indirebiliriz[cite: 2]. 

Önce bölümleri listeleyelim:
```bash
# WAP(Dopra Linux) terminalinde:
cat /proc/mtd
```
**Kritik Bölümler:**
* `mtd0`: bootcode (U-Boot)
* `mtd2` / `mtd3`: flash_config (Şifreli XML konfigürasyonları)
* `mtd6`: allsystemA (Kernel ve Kök Dosya Sistemi / SquashFS)
* `mtd11`: file_system (Kullanıcı verileri)

Huawei, standart `nc` (Netcat) aracını `/bin` içerisinden gizlemiştir (`nc: not found` hatası verir) ancak BusyBox içerisine gömülü olarak durmaktadır[cite: 2]. 

**Ağ üzerinden Dump Alma (Örnek: `mtd6`):**
1. **Bilgisayarınızda (Fedora/Linux)** dinlemeyi başlatın:
   ```bash
   nc -l -p 4444 > mtd6_allsystemA.bin
   
```
2. **Modem Terminalinde (BusyBox)** veriyi gönderin:
   ```bash
   cat /dev/mtd6 | busybox nc 192.168.1.20 4444
   
```
*(Bu işlem NAND çipini fiziksel olarak sökmekten çok daha güvenli ve dosya sistemi bütünlüğünü koruyan bir yöntemdir.)*

---

## 🔬 NAND ve SquashFS Analizi (Tersine Mühendislik Notları)
* **HWNP vs whwh Formatı:** Yeni nesil Huawei ürün yazılımları beklenen `HWNP` sihirli numarası (magic header) yerine `whwh` imza formatını ve `SIGNINFO` sarmalayıcısını kullanır.
* **SquashFS Koruması (SEC_SQS):** `allsystemA` içerisinden çıkartılan ana SquashFS imajı standart araçlarla (`unsquashfs`, `sasquatch`) açılmaya çalışıldığında LZMA sıkıştırması çözülse bile ID tabloları ve parçalanma (fragment) blok işaretçileri kasıtlı olarak bozularak/sıfırlanarak (`0x2fcecd9048fa6315` gibi geçersiz ofsetler) korunmuştur.
* **Zafiyet:** Ancak dosya sistemi canlı (RAM üzerinde) çalışırken `binwalk -e` ile yapılan bir çıkarma işlemi veya canlı Netcat dökümleri bu korumayı aşmamıza olanak sağlamıştır.
## 🗄️ Orijinal NAND Dump (`raw_dump.bin`) Analizi

Fiziksel donanım müdahalesi (NAND okuyucu) ile cihazdan alınan ham `raw_dump.bin` dosyasının standart araçlarla çıkartılamamasının ardında Huawei'nin bellek yönetimi mimarisi yatmaktadır.

İşte ham NAND dökümünün `binwalk` analiz raporu:

```text
DECIMAL       HEXADECIMAL     DESCRIPTION
--------------------------------------------------------------------------------
63532         0xF82C          SHA256 hash constants, little endian
63788         0xF92C          CRC32 polynomial table, little endian
134976        0x20F40         Certificate in DER format (x509 v3), header length: 4, sequence length: 1290
436566        0x6A956         SHA256 hash constants, little endian
436882        0x6AA92         CRC32 polynomial table, little endian
437956        0x6AEC4         CRC32 polynomial table, little endian
439238        0x6B3C6         CRC32 polynomial table, little endian
442938        0x6C23A         PEM certificate
529036        0x8128C         Certificate in DER format (x509 v3), header length: 4, sequence length: 1359
715094        0xAE956         SHA256 hash constants, little endian
715410        0xAEA92         CRC32 polynomial table, little endian
716484        0xAEEC4         CRC32 polynomial table, little endian
717766        0xAF3C6         CRC32 polynomial table, little endian
721466        0xB023A         PEM certificate
807564        0xC528C         Certificate in DER format (x509 v3), header length: 4, sequence length: 1359
2228224       0x220000        UBI erase count header, version: 1, EC: 0x1, VID header offset: 0x1000, data offset: 0x2000

```
### 🧠 Derin Analiz: "Binwalk Körlüğü" ve Fiziksel NAND Geometrisi

Ham NAND dökümünün standart araçlarla çıkartılamaması bir şifreleme değil, tamamen **fiziksel bellek geometrisi** ile ilgilidir. Cihazda root yetkisi elde edildikten sonra sistemin MTD sürücüleri incelenmiş ve şu fiziksel parametreler tespit edilmiştir:

```bash
WAP(Dopra Linux) # cat /sys/class/mtd/mtd0/oobsize
256
WAP(Dopra Linux) # cat /sys/class/mtd/mtd0/erasesize
262144
```
### 🧩 UBI Mantıksal Bölümlemesi (Logical Volume Mapping)
Cihazda `ubinfo` aracı Huawei tarafından silinmiş olsa da, root yetkisiyle doğrudan Kernel'in `/sys/class/ubi/` dizinine inildiğinde UBI katmanının devasa ham çipi nasıl mantıksal bölümlere ayırdığı net bir şekilde görülmektedir:

```bash
WAP(Dopra Linux) # ls -la /sys/class/ubi/
lrwxrwxrwx    1 root     root             0 Jan  1 09:14 ubi0 -> ../../devices/virtual/ubi/ubi0
lrwxrwxrwx    1 root     root             0 Jan  1 09:14 ubi0_0 -> ../../devices/virtual/ubi/ubi0/ubi0_0
...
lrwxrwxrwx    1 root     root             0 Jan  1 09:14 ubi0_10 -> ../../devices/virtual/ubi/ubi0/ubi0_10
```

## 🗂️ NAND Bellek Haritası (MTD Partition Layout)

Cihazdan başarılı bir şekilde aldığımız `full_nand_dump` içindeki MTD bloklarının boyutları ve Huawei mimarisindeki genel işlevleri aşağıdadır. 

*Not: Çip toplam 512 MB boyutundadır. `mtd0` (Bootloader) işletim sistemi düzeyinde (Kernel) kilitli olduğu için doğrudan `dd` komutu ile kopyalanamaz, bu güvenlik sebebiyle normaldir.*

| Partition | Dosya Boyutu | Çıkarılan Veri | Açıklama (Tahmini Huawei Yapısı) |
| :--- | :--- | :--- | :--- |
| **`mtd0`** | `N/A` | *Locked* | Bootloader / U-Boot (Çekirdek tarafından kilitli) |
| **`mtd1`** | `510.0 MB` | `534,773,760 bytes` | **UBI Master / Tüm NAND:** Geri kalan tüm alt bölümleri kapsayan ana blok |
| **`mtd2`** | `248.0 KB` | `253,952 bytes` | `flash_configA` (Birincil XML yapılandırmaları ve şifreler) |
| **`mtd3`** | `248.0 KB` | `253,952 bytes` | `flash_configB` (Yedek XML yapılandırmaları) |
| **`mtd4`** | `248.0 KB` | `253,952 bytes` | `slave_paramA` (Birincil operatör parametreleri) |
| **`mtd5`** | `248.0 KB` | `253,952 bytes` | `slave_paramB` (Yedek operatör parametreleri) |
| **`mtd6`** | `80.2 MB` | `84,058,112 bytes` | **`allsystemA` (Ana Firmware):** Aktif Linux Kernel ve SquashFS Kök Dosya Sistemi |
| **`mtd7`** | `80.2 MB` | `84,058,112 bytes` | **`allsystemB` (Yedek Firmware):** Kurtarma/Yedek İşletim Sistemi imajı |
| **`mtd8`** | `248.0 KB` | `253,952 bytes` | `board_info` (MAC Adresi, SN ve donanım kimlikleri) |
| **`mtd9`** | `248.0 KB` | `253,952 bytes` | RF / WLAN Kalibrasyon verileri |
| **`mtd10`** | `2.2 MB` | `2,285,568 bytes` | Sistem logları ve crash dump (Çökme) raporları |
| **`mtd11`** | `20.1 MB` | `21,078,016 bytes` | `file_system` / JFFS2 (Değiştirilebilir kullanıcı ayarları ve verileri) |
| **`mtd12`** | `295.2 MB` | `309,567,488 bytes` | `app_system` (Ekstra uygulamalar, SDK veya operatör bileşenleri) |

### 🛠️ USB Bellek Üzerinden Döküm Alma Komutu
Bu döküm, cihaza root yetkisiyle bağlanıp arayüze bir USB flash bellek (FAT32) takıldıktan sonra şu komutla elde edilmiştir:
```bash
for i in 0 1 2 3 4 5 6 7 8 9 10 11 12; do dd if=/dev/mtd$i of=/mnt/usb/usb-13fe-121A57_1/mtd${i}_dump.bin; echo "mtd$i kopyalamasi bitti!"; done; sync
```
## 🛠️ Huawei WAP (Diagnostic) Gizli Komut Seti Referansı

Huawei cihazlarında Telnet üzerinden `sUser` veya `root` yetkisiyle bağlandıktan sonra `SU_WAP>` kabuğunda (shell) kullanılabilecek gizli donanım, ağ ve tanılama komutlarının tam listesi aşağıda kategorize edilmiştir. 

> ⚠️ **DİKKAT:** `set`, `clear`, `restore` ve `reset` ile başlayan komutlar cihazın konfigürasyonunu kalıcı olarak bozabilir veya cihazı fabrika ayarlarına döndürebilir. Lütfen ne yaptığınızı bilmiyorsanız sadece `display` ve `get` komutlarını kullanın!

### 📊 1. Sistem ve Cihaz Bilgisi (System & Hardware)
Cihazın genel durumunu, donanım kimliğini ve kaynak tüketimini gösteren komutlar:
* `display deviceInfo` - Kapsamlı cihaz bilgisi (Model, SN, MAC, Uptime).
* `display cpu info` - İşlemci mimarisi ve yük durumu.
* `display memory detail` - RAM kullanım haritası (Hangi process ne kadar RAM tüketiyor).
* `display inner version` / `display version` - Cihazın gizli imza sürümü ve yazılım versiyonu.
* `display board-temperatures` - Anakart sıcaklık sensörleri.
* `display flashlock status` - Flash bellek kilit durumu.
* `sysinfo` / `display system info` - Genel sistem özetleri.

### 📡 2. Fiber Optik ve GPON (Optical & PON)
Fiber bağlantının kalitesini, lazer durumunu ve GPON parametrelerini incelemek için:
* `display optic` - Optik modül anlık değerleri (Sıcaklık, Voltaj, Tx/Rx Sinyal Gücü).
* `get optic par info` - Lazer modülünün marka/model (Vendor) bilgileri.
* `display pon statistics` - PON hattındaki hata paketleri ve istatistikler.
* `display onu info` - ONT (Terminal) kayıt durumu ve OLT ile olan iletişimi.
* `omcicmd mib show` - OMCI (ONT Management and Control Interface) veritabanı.

### 🌐 3. Ağ, Yönlendirme ve WAN (Network & Routing)
Operatörün (ISP) atadığı IP'ler, VLAN'lar ve yönlendirme tabloları:
* `display waninfo all detail` - Tüm WAN portları, VLAN ID'leri ve PPPoE/DHCP durumları.
* `display ip route` / `ip route show` - IPv4 Yönlendirme (Routing) tablosu.
* `display ip interface` - Tüm ağ arayüzleri ve atanan IP adresleri.
* `display macaddress` - Cihazın ARP ve MAC adres tablosu.
* `display dhcp server pool all` - Yerel ağdaki DHCP havuzu ve bağlı cihazlar.
* `ifconfig` / `ping` / `traceroute` / `netstat -na` - Standart Linux ağ araçları.

### 📶 4. Wi-Fi ve Kablosuz Ağ (WLAN / RF)
Kablosuz ağ çiplerini ve bağlı cihazları yönetmek için:
* `display wifichip` - Wi-Fi donanım durumu ve sürücü versiyonu.
* `display wifi radio` - 2.4GHz ve 5GHz radyo frekans değerleri, kanal durumları.
* `display wifi associate` - O an Wi-Fi'ye bağlı olan cihazların listesi ve sinyal (RSSI) kaliteleri.
* `set wifi radio` / `set wifi para` - Wi-Fi parametrelerini komut satırından zorlamak için.

### 🚪 5. Uzaktan Yönetim ve Güvenlik (TR-069 & Firewall)
Operatörün arka kapı bağlantıları ve cihazın güvenlik duvarı:
* `display tr069 info` - ACS sunucu adresi, şifreleri ve TR-069 aktiflik durumu.
* `display cwmp status` - CWMP (TR-069) anlık bağlantı durumu.
* `display firewall rule` - Güvenlik duvarı kuralları.
* `display iptables filter` / `display iptables nat` - Netfilter / Iptables yönlendirme kuralları.

### ☎️ 6. VoIP ve Ses Hizmetleri (Voice / DSP)
Eğer cihazda sabit telefon (POTS) kullanılıyorsa:
* `display voip info` - SIP hesap bilgileri ve kayıt durumu.
* `vspa display dsp state` - DSP (Dijital Sinyal İşleyici) durumu.
* `display voip ring info` - Çalan telefonun sinyal değerleri.

### 🐞 7. Gelişmiş Hata Ayıklama (Debugging & Tracing)
Mühendislik modları ve paket analizi:
* `trafficdump` - Doğrudan cihaz üzerinden ağ trafiğini dinleme (Tcpdump benzeri).
* `debugging ...` / `debug ...` - Belirli modüller (DSP, IGMP, DHCP vb.) için anlık hata ayıklama loglarını açar.
* `ampcmd trace ...` - Düşük seviye çip ve ethernet paket izleme.
* `display log info` / `display syslog` - Sistem hata ve olay günlükleri.

### 💻 8. Sistem Kontrolü ve Yönetim (System Control)
Kritik sistem eylemleri:
* `shell` - WAP kabuğundan çıkıp tam yetkili (Root) **Dopra Linux** terminaline (`#`) geçiş yapar.

## 🔑 Yetki Hiyerarşisi, Gizli Şifreler ve Kriptografi
Cihazın yapılandırma dosyasından (`hw_ctree.xml`) çekilen parolalar, donanıma gömülü AES anahtarıyla şifrelenmiş ve ardından PBKDF2 (10000 iterasyon + Salt) ile hashlanmıştır. 

Sistemdeki kullanıcılar ve yetki hiyerarşisi (Aşağıdan yukarıya doğru) şu şekildedir:

### Yetki Seviyesi 1: Standart Yönetici
* **Kullanıcı Adı:** `admin`
* **Şifre:** `superonline`
* **Erişim:** Kısıtlı Web arayüzü ayarları.

### Yetki Seviyesi 0: Super User (Sistem Yöneticisi)
* **Kullanıcı Adı:** `sUser`
* **Şifre:** `EP!99R4HLH9E`
* **Erişim:** Kapsamlı Web arayüzü ve standart root/telnet (`SU_WAP>`) erişimi.

### 👑 God Mode (En Yüksek Sistem Yetkisi): `srv_ssmp`
* **Kullanıcı Adı / Yetki Grubu:** `srv_ssmp`
* **Erişim:** Cihazdaki **en yüksek** yetki seviyesidir. `sUser`'ın bile üzerinde yer alır. Huawei'nin arka planda çalışan çekirdek servislerini (SSMP Daemon) ve sistemin en derin donanım ayarlarını kontrol eden "Tanrı Modu" yetkisidir. Web arayüzünden bağımsızdır, doğrudan işletim sistemi çekirdeğiyle konuşur.

### 📡 TR-069 (Uzaktan Yönetim - ACS) & Resmi Yazılım Sunucusu

Superonline'ın modeme uzaktan müdahale etmek, konfigürasyon basmak ve arka planda güncelleme tetiklemek için kullandığı CWMP (TR-069) protokolünün detaylı parametreleri ve ağ haritası aşağıdadır:

* **ACS Sunucu URL:** `http://acs.superonline.net:8015/cwmpWeb/WGCPEMgt`
* **ACS Kullanıcı Adı:** `superonlineacs`
* **ACS Şifresi:** `superonlineacs` *(Donanımsal hash üzerinden deşifre edilmiş açık metin)*
* **Periodic Inform (Periyodik Bilgilendirme):** `True`
* **Periodic Inform Interval:** `86400 saniye` (Cihaz her 24 saatte bir ACS sunucusuna rapor gönderir)
* **Connection Request Port / Path:** `3050` / `fd7730f602d0bd86feca9c6ffd7e9c5e` *(Sunucunun modemi tetiklemek için kullandığı dinamik yol)*
* **Connection Request Kullanıcı Adı:** `superonlineacs`
* **CPE OUI / Üretici Kimliği:** `00E0FC` (Huawei Technologies)
* **CPE Seri Numarası (Örnek Donanım):** `485754437C07DEA5`

#### 🚨 TR-069 Mimari Zafiyet Analizi (Exploit Vectors)

Cihazın yapılandırma satırları siber güvenlik perspektifiyle incelendiğinde, Huawei ve Superonline mühendisleri tarafından bırakılmış 3 kritik tasarım hatası ve sızma vektörü göze çarpmaktadır:

1. **Sertifika Doğrulama Defekti (`X_HW_EnableCertificate="0"`):**
   Cihaz, ACS sunucusuna bağlanırken SSL/TLS sertifika doğrulaması yapmamaktadır. Bu durum, yerel ağda gerçekleştirilecek bir **DNS Zehirlenmesi (DNS Spoofing)** veya **Man-in-the-Middle (MitM)** saldırısıyla modemin sahte bir ACS sunucusuna sorgusuz sualsiz bağlanmasını sağlar. Cihaz, sahte sunucudan gönderilecek manipüle edilmiş konfigürasyon dosyalarını orijinal kabul edecektir.
2. **KMC Kripto Belirteci Yapısı (`$2...`):**
   `Password` ve `ConnectionRequestPassword` alanlarının `$2` ile başlaması, verilerin düz hash (MD5/SHA) olmadığını, KMC (Key Management Center) tarafından şifrelendiğini doğrular. Domain 4 (XML AES Anahtarı) çözüldüğü için, bu şifreli bloklar (`superonlineacs`) çevrimdışı ortamda tamamen deşifre edilebilir durumdadır.
3. **Aktif Uyandırma Tetikleyicisi (`X_HW_Path`):**
   `fd7730f602d0bd86feca9c6ffd7e9c5e` değeri, modemin harici bağlantı isteklerini (Connection Request) dinlediği gizli URL patikasıdır. Sahte ACS saldırısı sırasında periyodik raporlama süresini (86400 saniye / 24 saat) beklemek istemeyen bir araştırmacı, yerel ağdan `http://192.168.1.1:3050/fd7730f602d0bd86feca9c6ffd7e9c5e` adresine düz bir HTTP GET isteği atarak modemi anında kendi sunucusuna bağlanmaya zorlayabilir (Force Connect).

#### 📦 Orijinal Firmware Dosyasını Çekme (ACS Üzerinden)

ACS sunucusu (`85.29.13.3:8010`) dış internete tamamen kapalıdır (Timeout verir). İnternette bulunmayan bu resmi firmware dosyasını elde etmenin en pratik yolu, servis sağlayıcının iç ağına (WAN) doğrudan erişimi olan **rootlanmış bir modem** kullanmaktır.

Root yetkisine sahip olduğunuz modemin terminaline (shell) girerek aşağıdaki komutlarla güncel firmware dosyasını doğrudan cihazın RAM'ine (`/tmp`) indirebilir ve ardından bilgisayarınıza alabilirsiniz:

**1. Firmware'i Modeme İndirme:**
```bash
# Standart wget ile:
wget "[http://acs.superonline.net:8010/firmware/LG8245X6-50_UNIFY_ONTV500R021C00SPC128.bin](http://acs.superonline.net:8010/firmware/LG8245X6-50_UNIFY_ONTV500R021C00SPC128.bin)" -O /tmp/firmware.bin

# Eğer standart wget hata verirse/yoksa alternatifler:
curl -o /tmp/firmware.bin "[http://acs.superonline.net:8010/firmware/LG8245X6-50_UNIFY_ONTV500R021C00SPC128.bin](http://acs.superonline.net:8010/firmware/LG8245X6-50_UNIFY_ONTV500R021C00SPC128.bin)"

# Veya BusyBox üzerinden:
busybox wget -O /tmp/firmware.bin "[http://acs.superonline.net:8010/firmware/LG8245X6-50_UNIFY_ONTV500R021C00SPC128.bin](http://acs.superonline.net:8010/firmware/LG8245X6-50_UNIFY_ONTV500R021C00SPC128.bin)"
```
#### 📦 Orijinal Firmware Dağıtım Kanalları (Transfer Queue)
Sistem veri akışında ve transfer kuyruğunda (`X_HW_TransferQueueInstance`) tespit edilen, Superonline'ın sadece iç ağa (VLAN 100) açık olan resmi firmware barındırma sunucusu ve dosya yapısı:

* **Resmi Güncelleme URL'si:** `http://acs.superonline.net:8010/firmware/LG8245X6-50_UNIFY_ONTV500R021C00SPC128.bin`
* **Dosya Boyutu (Firmware Size):** `50,492,616 bytes` (~48.1 MB)
* **Görev Tipi (RPC Type):** `Download` (Firmware Upgrade Image)

#### 🛠️ WAP CLI Temel Yönetim Komutları
* `reset` - Modemi donanımsal olarak yeniden başlatır (Reboot).
* `restore manufactory` - Cihazı fabrikadan çıktığı ilk güne döndürür (Tüm konfigürasyon silinir).
* `save data` - RAM'deki geçici değişiklikleri NAND Flash'a kalıcı olarak yazar.

### Kriptografik Duvarın Anatomisi: Neden Sahte Payload Üretemiyoruz?

Eski cihazlarda çalışan firmware modifikasyon yöntemlerinin yeni nesil cihazlarda (R022 ve sonrası) anında reddedilmesinin sebebi, iki imza formatı arasındaki devasa güvenlik uçurumudur. 

* **Eski Format (signinfo_v3 / HuaweiFirmwareTool):**
  Bu araç, dosyanın son 256 baytına sadece basit bir RSA-SHA256 imzası (`RSA_sign`) yerleştiriyordu. Eski bootloader (B125 sürümleri) sadece dosyanın hash bütünlüğüne bakıp, "Hash tutuyor, dosya bozuk değil" diyerek manipüle edilmiş yazılımı içeri alıyordu.
* **Yeni Format (signinfo_v5 / R022.bin):**
  Huawei güvenlik mimarisini tamamen kurumsal seviyeye çıkarmıştır. `ASN.1 DER PKCS#7 SignedData` kullanılması demek, cihazın artık sadece hash kontrolü yapmaması; "Bu dosyayı gerçekten Huawei'nin kendisi mi üretti?" diye sorması demektir. Sistemin içine gömülü `Huawei Code Signing CA 2` X.509 kök sertifikası, donanım (TrustZone/U-Boot) seviyesinde doğrulanmaktadır.

**Sonuç:** Huawei'nin genel merkezindeki Asimetrik Özel Anahtar (Private Key) internete sızmadığı veya PKCS#7 / X.509 doğrulama zincirinde mantıksal bir zafiyet (tarih kontrolü eksikliği vb.) bulunmadığı sürece, R022 ve sonrasını kandıracak sahte bir `.bin` firmware dosyası üretmek matematiksel olarak imkansızdır. 

Bu nedenle donanım root işlemlerinde firmware sahteciliği tamamen rafa kaldırılmış, modifiye edilmiş `hw_ctree.xml` ve yerel exploitler üzerinden ilerleyen "Yöntem 1" ana standart olarak belirlenmiştir.
