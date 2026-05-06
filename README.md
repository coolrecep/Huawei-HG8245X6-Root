# Huawei HG8245X6 Root Guide

Bu depo, Superonline (veya diğer ISP'ler) tarafından kilitlenmiş Huawei HG8245X6 (ve benzeri V500R021 serisi) Fiber ONT / Router cihazlarında tam root erişimi (Telnet/SSH) elde etme, donanımsal NAND dökümü alma, şifreli Firmware yapısını (whwh/HWNP) çözme ve konfigürasyon dosyalarını manipüle etme süreçlerini adım adım açıklamaktadır.

⚠️ **Uyarı:** Bu repodaki işlemler cihazınızı garanti dışı bırakabilir veya kullanılamaz (brick) hale getirebilir. Tüm sorumluluk size aittir.

## 📌 İçindekiler
- [Donanım ve Yazılım Bilgileri](#donanım-ve-yazılım-bilgileri)
- [Ganimet: Şifreler ve Kritik Bilgiler](#ganimet-şifreler-ve-kritik-bilgiler)
- [Yöntem 1: Özel Payload ile Telnet/SSH Açma (Önerilen)](#yöntem-1-özel-payload-ile-telnetssh-açma)
- [Yöntem 2: Cihaz İçinden Canlı Firmware Dökümü (Live Dump)](#yöntem-2-cihaz-içinden-canlı-firmware-dökümü)
- [NAND ve SquashFS Analizi (Tersine Mühendislik Notları)](#nand-ve-squashfs-analizi)

---

## 💻 Donanım ve Yazılım Bilgileri (Device Specifications)

* **Cihaz Modeli:** Huawei OptiXstar HG8245X6 GPON Terminal
* **Donanım Özellikleri:** GPON 4*GE + 2.4G/5G Wi-Fi + 2POTS + 1USB
* **İşlemci (SoC):** HiSilicon A9 (Çift Çekirdekli ARM Cortex-A9, BogoMIPS: 2190.54)
* **RAM / Flash:** 512 MB / 512 MB (SPI-NAND)
* **NAND Flash Çipi:** TC58CVG2S0HRAIG
* **PCB (Anakart):** DN8245XA
* **Donanım Sürümü (Hardware Version):** 1E8E.A
* **Üretim Bilgisi (Production Info):** 2150084230HYM3000324.C402
* **Çıkış Tarihi (Release Time):** 2022-03-16

### ⚙️ İşletim Sistemi ve Çekirdek Detayları
* **Product Version:** V5R021C00S128
* **Inner Version:** `V500R021C00SPC128B125` *(Payload imzalama için kritik sürüm)*
* **U-Boot Version:** HiSilicon U-Boot *(Çekirdek tarafından mtd0 kilitli)*
* **Kernel Version:** Linux 4.4.240 (#1 SMP Fri Dec 17 01:10:57 CST 2021)
* **Filesystem:** SquashFS (rootfs) + UBIFS / JFFS2 (config & app data)
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

Başlangıçta cihazın 22 (SSH) ve 23 (Telnet) portları dışarıdan erişime tamamen kapalıdır (Filtered/Closed). Web arayüzü (`admin` hesabı) üzerinden Telnet açma veya yapılandırma indirme menüleri gizlenmiştir.

---

## 🗝️ Ganimet: Şifreler ve Kritik Bilgiler
NAND dökümünün UBIFS bölümlerinden (`volume_9.raw` ve `hw_ctree.xml`) elde edilen açık metin şifreler ve kimlik bilgileri:

| Tür | Kullanıcı Adı | Şifre / Değer |
| :--- | :--- | :--- |
| **Telnet / CLI (Root)** | `sUser` / `root` | `EP!99R4HLH9E` |
| **Web Arayüzü (Kısıtlı)** | `admin` | `superonline` |
| **TR-069 ACS Sunucu** | `superonlineacs` | `$2M\xR1w!!W0j{'78t...` (AES Encrypted) |

*(Not: Admin arayüzü şifreleri PBKDF2 algoritması ile 10000 iterasyonla hashlenmiştir.)*

---

## 🚀 Yöntem 1: Özel Payload ile Telnet/SSH Açma
Bu cihazlarda web arayüzünden config dosyası indirip yükleyerek Telnet açmak mümkün değildir. Bunun yerine, cihazın ürün yazılımı güncelleme mekanizmasını kandırarak, Telnet'i kalıcı olarak açan ve "Çin Modu"nu (China Mode CLI) aktif eden özel bir `.bin` payload'u hazırladık.

### 1. Payload'u Derlemek
Aşağıdaki bash betiği, cihazın sürümüne uygun sahte bir ürün yazılımı güncellemesi oluşturur. Bu güncelleme yüklendiğinde, arka planda çalışan komut dosyası yapılandırma dosyasını (`hw_ctree.xml`) çözer (`aescrypt2`), `TELNETLanEnable` ve `SSHLanEnable` değerlerini 1 yapar, TR-069'u kapatır ve dosyayı geri şifreler.

Bunun için [HuaweiFirmwareTool](https://github.com/0xuserpag3/HuaweiFirmwareTool) gereklidir.

```bash
mkdir -p payload_build/var
cd payload_build

# Payload betiğini oluştur (var/signature)
cat > var/signature << 'EOF'
#! /bin/sh
echo "=== HG8245X6 Custom Payload Execution ===" > /dev/console

# 1. HW_SSMP_FEATURE_CLI_CHINA_MODE özelliğini aktif et
echo 'feature.name = "HW_SSMP_FEATURE_CLI_CHINA_MODE" feature.enable="1" feature.attribute="1"' > /mnt/jffs2/hw_hardinfo_feature

# 2. hw_ctree.xml dosyasını manipüle et
var_jffs2_current_ctree_file="/mnt/jffs2/hw_ctree.xml"
var_current_ctree_bak_file="/var/hw_ctree_equipbak.xml"
var_current_ctree_file_tmp="/var/hw_ctree.xml.tmp"
var_node_telnet="InternetGatewayDevice.X_HW_Security.AclServices"
var_node_telnet_acs="InternetGatewayDevice.UserInterface.X_HW_CLITelnetAccess"

if [ -f "$var_jffs2_current_ctree_file" ]; then
    cp -f $var_jffs2_current_ctree_file $var_current_ctree_bak_file
    
    /bin/aescrypt2 1 $var_current_ctree_bak_file $var_current_ctree_file_tmp
    varIsXmlEncrypted=$?
    
    if [ $varIsXmlEncrypted -eq 0 ]; then
        mv $var_current_ctree_bak_file $var_current_ctree_bak_file".gz"
        gunzip -f $var_current_ctree_bak_file".gz"
    fi

    # Telnet'i kalıcı olarak aç
    cfgtool set $var_current_ctree_bak_file $var_node_telnet TELNETLanEnable 1
    cfgtool set $var_current_ctree_bak_file $var_node_telnet_acs Access 1
    
    # TR-069 ACS bağlantısını kapat (Kalıcılık için)
    cfgtool set $var_current_ctree_bak_file "InternetGatewayDevice.ManagementServer" EnableCWMP 0

    if [ $varIsXmlEncrypted -eq 0 ]; then
        gzip -f $var_current_ctree_bak_file
        mv $var_current_ctree_bak_file".gz" $var_current_ctree_bak_file
        /bin/aescrypt2 0 $var_current_ctree_bak_file $var_current_ctree_file_tmp
    fi

    rm -f $var_jffs2_current_ctree_file
    cp -f $var_current_ctree_bak_file $var_jffs2_current_ctree_file
fi

# 3. Telnet daemon'unu hemen başlat
/sbin/telnetd -p 23 -l /bin/sh &
/sbin/telnetd -p 2323 -l /bin/sh &

echo "success!" > /dev/console
exit 0
EOF

chmod +x var/signature

# Hedef cihazın tam sürümünü girin (ÖNEMLİ!)
echo -n "V500R021C00SPC128B125" > var/signinfo

# Harita dosyasını oluştur (item_list.txt)
cat > item_list.txt << 'EOF'
0x504e5748
256 BB9|1029|997|734|393|323|627|767|1067|1007|AC7|10C7|147C|148C|14ED|15BD|120|130|140|141|150|160|170|171|180|190|1B1|1A1|1A0|1B0|1D0|1F1|201|211|221|230|240|260|261|270|271|280|281|291|2A1|431|
+ 0 file:/var/signature SIGNATURE V500R021C00SPC128B125 2
+ 1 file:/var/signinfo SIGNINFO V500R021C00SPC128B125 0
EOF

# Aracı kullanarak payload'u paketle
hw_fmw -d . -p -o HG8245X6_Root_Payload.bin
```

### 2. Payload'u Cihaza Göndermek
1. Windows bilgisayarınızı Ethernet kablosu ile cihaza bağlayın ve IP adresinizi statik yapın (`192.168.1.100`).
2. `ONT使能2.0.exe` (ONT Enable Tool) aracını yönetici olarak çalıştırın.
3. Ethernet kartınızı seçip, oluşturduğunuz `HG8245X6_Root_Payload.bin` dosyasını `Upgrade File` olarak yükleyip başlatın.
4. Cihaz paket aldıktan sonra yeniden başlayacaktır.
5. Port taramasında (Nmap) `22/tcp (Dropbear sshd)` ve `23/tcp (Huawei telnetd)` portlarının `OPEN` duruma geçtiğini göreceksiniz.

### 3. Sisteme Giriş
Terminal üzerinden:
```bash
telnet 192.168.1.1
```
* **Login:** `sUser`
* **Password:** `EP!99R4HLH9E`

Giriş yaptıktan sonra `WAP>` (Huawei Diagnostic Shell) konsoluna düşersiniz
1. `su` yazıp tekrar şifreyi girerek `SU_WAP>` moduna geçin.
2. `shell` yazarak **Dopra Linux** kök işletim sistemine (`#`) düşün.

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
* `reset` - Modemi donanımsal olarak yeniden başlatır (Reboot).
* `restore manufactory` - Cihazı fabrikadan çıktığı ilk güne döndürür (Tüm konfigürasyon silinir).
* `save data` - RAM'deki geçici değişiklikleri NAND Flash'a kalıcı olarak yazar.
