using System;
using System.Text.RegularExpressions;
using HekzOntRace.Core;

namespace HekzOntRace;

public static class LiteLogUyarlayici
{
    public static string Uyarlayici(string m)
    {
        if (string.IsNullOrWhiteSpace(m)) return null;

        // --- 1. Önce dönüştürülecek bilinen kilometre taşları (Dönüşüm Kuralları) ---

        if (m.Contains("=== HekzOntRace"))
        {
            return m.Replace("HekzOntRace v2.3", "HekzOntRace Lite v2.0.1 (Kolay Mod)")
                    .Replace("HekzOntRace v2.0.0", "HekzOntRace Lite v2.0.1 (Kolay Mod)")
                    .Replace("HekzOntRace v2.0.1", "HekzOntRace Lite v2.0.1 (Kolay Mod)");
        }

        if (m.Contains("=== Web Konfigürasyon Enjeksiyonu"))
        {
            return m.Replace("Web Konfigürasyon Enjeksiyonu (Önerilen)", "Yeni sUser Parolası Belirleme");
        }

        if (m.Contains("sUser yönetici oturumu doğrulandı") || m.Contains("sUser oturumu OK"))
        {
            return "[İşlem] sUser yönetici oturumu doğrulandı ✓";
        }

        if (m.Contains("Yapılandırma indirildi") || (m.Contains("config indirildi:") && m.Contains("InternetGatewayDevice")))
        {
            return "[İşlem] Modem konfigürasyonu güvenli şekilde indirildi ✓";
        }

        if (m.Contains("öz-doğrulama: yeni şifre formatı DOĞRULANDI"))
        {
            return "[İşlem] Yeni sUser parola formatı hazırlandı ve doğrulandı ✓";
        }

        if (m.Contains("YEDEK:") && m.Contains("suser_web_yedek"))
        {
            return "[Yedek] Orijinal konfigürasyon Masaüstü\\HekzONT klasörüne arşivlendi ✓";
        }

        if (m.Contains("UPLOAD OK — cihaz config'i işliyor"))
        {
            return "[Yükleme] Yeni konfigürasyon modeme başarıyla yüklendi ✓";
        }

        if (m.Contains("[Web Enjeksiyonu] ✓ YENİ PAROLA ATANDI:"))
        {
            return "[Sonuç] ✓ YENİ sUser PAROLANIZ TANIMLANDI: ******** (Sonuçlar sekmesinden görüntüleyebilirsiniz)";
        }

        if (m.Contains("[SFTP] SFTP dosya transfer servisi başlatıldı"))
        {
            return "[İşlem] Güvenli veri transfer kanalı hazırlandı ✓";
        }

        if (m.Contains("[Erişim] Fabrika parametreleri") && m.Contains("köprüsü oluşturuldu"))
        {
            return "[İşlem] Cihaz fabrika parametreleri okunuyor...";
        }

        if (m.Contains("[Fabrika Parametreleri] Cihaz donanım bloğu okundu"))
        {
            return "[İşlem] Donanım parametreleri başarıyla alındı ✓";
        }

        if (m.Contains("[Konfigürasyon] Cihaz ana ayar veritabanı") && m.Contains("okunuyor"))
        {
            return "[İşlem] Modem konfigürasyon veritabanı alınıyor...";
        }

        if (m.Contains("[Konfigürasyon] Ayar veritabanı AES-256 ile çözüldü") || m.Contains("Ayar veritabanı çözüldü"))
        {
            return "[İşlem] Modem konfigürasyonu ve şifreler başarıyla çözüldü ✓";
        }

        if (m.Contains("[Depolama] Geçici çalışma dosyaları başarıyla temizlendi"))
        {
            return "[Temizlik] Geçici oturum dosyaları temizlendi ✓";
        }

        if (m.Contains("[Depolama] Veri yedekleri kaydedildi:"))
        {
            return "[Yedek] Dosya yedekleri Masaüstü\\HekzONT klasörüne kaydedildi ✓";
        }

        if (m.Contains("[Doğrulama] ✓ sUser yönetici şifresi doğrulandı"))
        {
            return "[Yönetici] ✓ sUser yönetici şifreniz tespit edildi ve doğrulandı";
        }

        if (m.Contains("[PPPoE] ✓"))
        {
            return LogMaskeleyici.Temizle(m.Replace("[PPPoE] ✓", "[İnternet] ✓ PPPoE:"));
        }

        if (m.Contains("[WiFi] 2.4G") || m.Contains("[WiFi] 5G"))
        {
            return LogMaskeleyici.Temizle(m.Replace("[WiFi]", "[Kablosuz Ağ]"));
        }

        if (m.Contains("[Sistem] Tüm cihaz parametreleri ve konfigürasyon modülleri başarıyla okundu"))
        {
            return "[Tamamlandı] ✓ Tüm bilgiler başarıyla hazırlandı! Ekrandaki kutulardan görüntüleyip kopyalayabilirsiniz.";
        }

        // --- 2. Filtrelenecek iç ve teknik detay mesajları (Gizleme Kuralları) ---

        // Arka plan servisleri ve dosya paylaşım detayları
        if (m.Contains("Servis kimlik doğrulaması hazır:") ||
            m.Contains("Paylaşım hesabı yapılandırıldı:") ||
            m.Contains("Samba dosya paylaşım servisi başlatıldı") ||
            m.Contains("Ayar veritabanı bloğu alındı") ||
            m.Contains("Blok bütünlüğü doğrulandı") ||
            m.Contains("Servis bağlantı ve dizin erişim testi başarılı") ||
            m.Contains("Dosya erişim köprüsü oluşturuldu") ||
            m.Contains("Aktarım yardımcısı") ||
            m.Contains("Script:") ||
            m.Contains("YAKALANDI:") ||
            m.Contains("HMAC geçerli"))
        {
            return null;
        }

        // sUser web şifre yazma iç script logları
        if (m.Contains("sUSER ŞİFRE YAZMA") ||
            m.Contains("sUser girişi...") ||
            m.Contains("Ana parola özeti güncellendi") ||
            m.Contains("Doğrulama parola özeti güncellendi") ||
            m.Contains("Telnet/SSH bayrakları") ||
            m.Contains("Komut satırı (CLI) yönetici parolası") ||
            m.Contains("upload: HTTP 200") ||
            m.Contains("upload: <!DOCTYPE") ||
            m.Contains("UPLOAD OK — cihaz") ||
            m.Contains("***") ||
            m.Contains("Hesap temizliği") ||
            m.Contains("sUSER WEB YAZMA TAMAM") ||
            Regex.IsMatch(m, @"^\s*\[\[\s*\d+([.,]\d+)?s\]") ||
            Regex.IsMatch(m, @"^\s*Yeni yönetici parolası hazırlandı:"))
        {
            return null;
        }

        return LogMaskeleyici.Temizle(m);
    }
}
