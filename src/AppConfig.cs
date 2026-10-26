namespace HekzOntRace;

public static class AppConfig
{
#if LITE
    public static readonly bool IsLite = true;
    public const string AppTitle = "HekzOntRace Lite v2.0.1 — Huawei LG8245X6-50 / HG8245X6 Kolay Bilgi ve Şifre Aracı";
    public const string EditionLabel = "Lite (Kolay Mod)";
#else
    public static readonly bool IsLite = false;
    public const string AppTitle = "HekzOntRace v2.0.1 — Huawei LG8245X6-50 / HG8245X6 Tool";
    public const string EditionLabel = "Standart Sürüm";
#endif
}
