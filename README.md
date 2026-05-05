# Huawei HG8245X6 (and Similar ONT) Reverse Engineering & Root Guide

Bu depo, Superonline (veya diğer ISP'ler) tarafından kilitlenmiş Huawei HG8245X6 (ve benzeri V500R021 serisi) Fiber ONT / Router cihazlarında tam root erişimi (Telnet/SSH) elde etme, donanımsal NAND dökümü alma, şifreli Firmware yapısını (whwh/HWNP) çözme ve konfigürasyon dosyalarını manipüle etme süreçlerini adım adım açıklamaktadır.

⚠️ **Uyarı:** Bu repodaki işlemler cihazınızı garanti dışı bırakabilir veya kullanılamaz (brick) hale getirebilir. Tüm sorumluluk size aittir.

## 📌 İçindekiler
- [Donanım ve Yazılım Bilgileri](#donanım-ve-yazılım-bilgileri)
- [Ganimet: Şifreler ve Kritik Bilgiler](#ganimet-şifreler-ve-kritik-bilgiler)
- [Yöntem 1: Özel Payload ile Telnet/SSH Açma (Önerilen)](#yöntem-1-özel-payload-ile-telnetssh-açma)
- [Yöntem 2: Cihaz İçinden Canlı Firmware Dökümü (Live Dump)](#yöntem-2-cihaz-içinden-canlı-firmware-dökümü)
- [NAND ve SquashFS Analizi (Tersine Mühendislik Notları)](#nand-ve-squashfs-analizi)

---

## 💻 Donanım ve Yazılım Bilgileri
* **Cihaz Modeli:** Huawei HG8245X6 (Dahili ONT'li Wi-Fi 6 Router)
* **NAND Flash Çipi:** TC58CVG2S0HRAIG
* **İşletim Sistemi:** Dopra Linux (BusyBox v1.31.1)
* **Test Edilen Firmware Sürümü:** `V500R021C00SPC128B125`

Başlangıçta cihazın 22 (SSH) ve 23 (Telnet) portları dışarıdan erişime tamamen kapalıdır (Filtered/Closed). Web arayüzü (`admin` hesabı) üzerinden Telnet açma veya yapılandırma indirme menüleri gizlenmiştir.

---

## 🗝️ Ganimet: Şifreler ve Kritik Bilgiler
NAND dökümünün UBIFS bölümlerinden (`volume_9.raw` ve `hw_ctree.xml`) elde edilen açık metin şifreler ve kimlik bilgileri:

| Tür | Kullanıcı Adı | Şifre / Değer |
| :--- | :--- | :--- |
| **Telnet / CLI (Root)** | `sUser` / `root` | `EP!99R4HLH9E` |
| **Web Arayüzü (Kısıtlı)** | `admin` | `superonline` |
| **PPPoE (Fiber Auth)** | `110244098437@fiber` | `$2x$*n/Ok~~FYPf;'$q;...` (AES Encrypted) |
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
```
