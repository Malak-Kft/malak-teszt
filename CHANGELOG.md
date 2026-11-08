# Valtozasok

## v3.6.1 (2026-11-08)
- Az .exe valtozatlan (3.6.0). A kiadasba bekerult a **Mac-segedszkript** (`malak-teszt-mac.command`) es a **Windows B terv** (`malak-teszt.bat`) is, igy a malaktech.hu-rol nem kell semmilyen szkriptet letolteni.
- Mindketto, mint az .exe: sajat bongeszo-kodot general, megnyitja a malaktech.hu/teszt/?k=KOD oldalt, es oda kuldi az eredmenyt.
- `malak-teszt.bat`: a v3.6 gyujto (gepnev / felhasznalonev nelkul, MDM-bol csak a domain, `lock.mdm` osszesito).
- `malak-teszt-mac.command`: a system_profiler kimenetebol a gep neve es a felhasznalo neve kimarad.
- GitHub Actions: a .bat PowerShell-reszet Windowson, a Mac-szkriptet macOS-en szintaxis-ellenorzi a build elott.

## v3.6 (2026-11-08)
- A program **nem tolt le es nem futtat kulso kodot**: csak az .exe-be epitett (azzal egyutt alairt) gyujto-szkript fut. Korabban induláskor a malaktech.hu-rol toltotte le a friss valtozatot.
- **Energiaellatas a meres idejere maxon, az egesz rendszerre:** sajat ideiglenes sema (Ultimate Performance / Nagy teljesitmeny masolata, vagy ha a gepen nincs ilyen - pl. Modern Standby laptopok -, a jelenlegi sema maxra hangolt masolata): CPU min/max 100%, boost agressziv, EPP 0, magparkolas ki, PCIe ASPM ki, alvas/lemez/kepernyo-idozito ki, halozatrol es akkurol is. Emellett a Windows 10/11 energiamod-csuszka: Legjobb teljesitmeny. A vegen minden visszaall; ha a program felbeszakad, a kovetkezo inditas allitja vissza.
- Induláskor kiirja, mit allit at es milyen adatot kuld el.
- Adatvedelem: a gep neve es a Windows-felhasznalo neve nem kerul a jelentesbe; az MDM-regisztraciobol es a dsregcmd kimenetbol csak a ceg domainje marad (*@ceg.hu).
- MDM-zar: uj `lock.mdm` osszesito (`active`, `activeDomains`, `leftoverDomains`, `leftoverCount`) es regisztracionkent `active`/`tasks`. Aktiv = beiratkozott allapot ES van szinkronizalo utemezett feladat; a regi, maradvany bejegyzes nem szamit zarnak.
- Nyilt forraskod (MIT), GitHub Actions build, ZIP-es kiadas SHA-256-tal.

## v3.5 es korabbi - JSON-kimenet
`native` objektum: cpu1, cpuN, threads, sha256MBs, gflops, stress[], throttlePct, baseMhz/stressMhz/peakMhz/endMhz/boostOk, memBw, memBwMT, memLatNs, memTest*, stab* (3-as mod), gpu*, diskW/diskR/disk4kIops, powerSwitched, engine. A gyujto altal irt JSON-ba a `"native": null` helyere kerul.


## v3.3 JSON-kimenet (uj mezok a `native` objektumban)
`cbMulti`, `cbSingle`, `cbClk` ({stepMs, threads, eff[], phases[{name,from,to}], cores[][] MHz}), `fmScore`, `fmFps`, `fmVer`, `fmGpu` ([mp, GPU MHz, mem MHz, C, W, %] NVIDIA-n), `fmUtil` (GPU 3D kihasznaltsag %/mp), `fmCpuClk`, `fmRaw` (nyers kimenet, hibakereseshez).
Cinebench R15: a program `-cb_cpux` / `-cb_cpu1` kapcsolokkal indul, a pontszamot a sajat konzol kepernyo-pufferebol olvassa ki (a Cinebench a szulo konzolra ir).


## v3.4 (2026-10-01) – hibatures, naplo, szakaszos kuldes
- Minden meresi szakasz (CPU, Cinebench, RAM, memoriateszt, stabilitas, GPU, FurMark, SSD) kulon vedett: ha az egyik elszall, a tobbi lefut, a hiba a `native.errors` tombbe kerul.
- Minden szakasz utan fajlba irja a reszeredmenyt (ha a gep lefagy / kikapcsol, az addigi eredmeny megmarad az Asztal\malak-teszt mappaban).
- Naplo: `Asztal\malak-teszt\malak-teszt-naplo.txt` (a teljes konzol-kimenet idobelyeggel + a hibak reszletei).
- Kuldes a bongeszobe harom lepesben: (1) gepadatok rogton az adatgyujtes utan (`"partial": true`), (2) CPU/RAM reszeredmeny a GPU-terheles elott, (3) vegeredmeny. Hiba eseten is elmegy, ami megvan (`native.complete=false`). 3 probalkozas, HTTP-hibakod kiirasa.
- Ha a fajlnevben nincs bongeszo-kod (pl. pendrive-rol futtatott peldany), a program rakerdez.
- Cinebench R15: a Maxon leirasa szerinti inditas (`start /b /wait "parentconsole" "CINEBENCH Windows 64 Bit.exe" -cb_cpux`), a kimenet atiranyitott csobol es a konzol pufferebol is olvasva; R15 ("Rendering (Multiple CPU) : x pts") es R20+ ("CB x") formatum; idokorlat (10 / 15 perc); csak az R15 / R11.5 nevu exe-t fogadja el; 64 bites valtozat elonyben; nyers kimenet: `native.cbRaw`.
- FurMark 2: `--export-dir` (nem letezo kapcsolo) helyett `--log-gpu-data --log-gpu-data-filename`; a FurMark mappajaba a futas alatt irt naplo/pontszam fajlok is bekerulnek a `fmRaw`-ba; a GPU-naplo nyersen: `native.fmGpuCsv`.
- Uj `native` mezok: `complete`, `lastStage`, `errors[]`, `cbRaw`, `fmGpuCsv`.
- Rejtett kapcsolo a hibakezeles kiprobalasahoz: `/hibateszt` (a CPU-szakasz utan szandekos hiba).


## v3.4.1 (2026-10-01) – forditasi javitas
A v3.4 mono `mcs`-sel a mono sajat mscorlib-je ellen fordult, ezert harom olyan metodusra hivatkozott, ami a Windows .NET Framework 4.x-ben nincs meg: `string.TrimStart(char)`, `string.Split(char, StringSplitOptions)` es `string.TrimEnd()`. Windowson minden meresi szakasz MissingMethodException-nel allt le (lasd a 87-es teszt `native.errors` mezojet). A forraskod nem valtozott: a `build.sh` mostantol a .NET Framework 4.8 referencia-szerelvenyei (`ref/`) ellen fordit, igy a fordito csak Windowson is letezo metodust valaszthat.

## v3.5 (2026-11-06) – kod nelkuli futtatas
- Ha a fajlnevben (`_KOD.exe`) es a parancssorban (`/kod:KOD`) sincs bongeszo-kod, a program **nem kerdez**: sajat 8 karakteres kodot general, es megnyitja a `https://malaktech.hu/teszt/?k=KOD` oldalt (explorer.exe-n keresztul, igy a bongeszo nem rendszergazdai jogon indul). Az emelt jogu ujraindulas a kodot `/kod:`-dal, a megnyitast `/megnyitva`-val kapja meg, hogy ne nyiljon ket fül.
- Az oldal a kodot atveszi, var az eredmenyre, beolvassa es kb. 15 mp utan magatol elmenti a gep szeriaszamara (a mentes utan a Malak Keszlet a szeriaszam alapjan parositja: ha a gep a keszletben van es nincs eladva, felkerul ra a teszt).
- Igy az .exe barhonnan futtathato (pendrive, 1tst mappa, Google Drive), nem kell az oldalrol letolteni.
