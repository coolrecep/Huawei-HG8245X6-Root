; HekzOntRace — Inno Setup kurulum betiği (v2.0.1 NewRelase)
; Derleme: ISCC.exe HekzOntRace.iss  (Inno Setup 6+ gerekir)
;
; NOT: Önce build.ps1 çalıştırın → publish_final\HekzOntRace.exe oluşur,
;      ardından bu .iss ile setup.exe derleyin.

#define UygulamaAdi    "HekzOntRace"
#define UygulamaVersiyon "2.0.1"
#define Yayinlayan     "HEKZ"
#define YayinKlasoru   "publish_final"

[Setup]
AppId={{8F4A2E1B-7C3D-4E5F-9A6B-1D2E3F4A5B6C}
AppName={#UygulamaAdi}
AppVersion={#UygulamaVersiyon}
AppPublisher={#Yayinlayan}
DefaultDirName={autopf}\{#UygulamaAdi}
DefaultGroupName={#UygulamaAdi}
UninstallDisplayName={#UygulamaAdi}
OutputBaseFilename=HekzOntRace_v2.0.1_Setup
OutputDir=installer
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
SetupIconFile=icon.ico

[Tasks]
Name: "desktopicon";    Description: "Masaüstü kısayolu oluştur";      GroupDescription: "Ek kısayollar:"
Name: "quicklaunchicon"; Description: "Görev çubuğuna sabitle";         GroupDescription: "Ek kısayollar:"; Flags: unchecked

[Files]
; Tek-dosya self-contained publish çıktısı — sadece .exe gerekli
Source: "{#YayinKlasoru}\HekzOntRace.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#UygulamaAdi}";          Filename: "{app}\HekzOntRace.exe"; WorkingDir: "{app}"
Name: "{group}\{#UygulamaAdi} Kaldır";   Filename: "{uninstallexe}"
Name: "{autodesktop}\{#UygulamaAdi}";    Filename: "{app}\HekzOntRace.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\{#UygulamaAdi}"; Filename: "{app}\HekzOntRace.exe"; WorkingDir: "{app}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\HekzOntRace.exe"; Description: "{#UygulamaAdi} başlat"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
