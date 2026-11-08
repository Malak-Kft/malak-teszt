# malak-teszt

A [malaktech.hu/teszt](https://malaktech.hu/teszt) oldal Windows-os segedprogramja (`malak-teszt_es-bench.exe`):
gepadat-gyujtes + CPU / RAM / memoriateszt / GPU (OpenGL) / SSD benchmark, hosszu stabilitasteszt asztali gephez,
Cinebench R15 es FurMark inditasa (ha telepitve vannak), az eredmeny feltoltese a malaktech.hu/teszt oldalra.

A [Malak Kft.](https://malaktech.hu) felujitott laptopokat arul; ezzel a programmal teszteljuk a gepeket eladas elott,
es a vevok is lefuttathatjak a sajat gepukon.

## Letoltes
A legfrissebb valtozat: [Releases](../../releases/latest).

- **Windows:** `malak-teszt_es-bench.exe` (vagy a `.zip`: exe + leiras + SHA-256). Nem kell telepiteni: duplaklikk, majd a Windows kerdesere (UAC) Igen.
- **Windows, B terv:** `malak-teszt.bat` - ugyanaz az adatgyujtes egy belenezheto parancsfajlban, ha az .exe nem indul (rovidebb benchmark).
- **Mac:** `malak-teszt-mac.command` - letoltes utan Terminalban: `bash ~/Downloads/malak-teszt-mac.command` (a bongeszobol letoltott fajl dupla kattintasra nem futtathato, mert a letoltes nem tartja meg a futtatasi jogot).

Mindharom maga general egy bongeszo-kodot, megnyitja vele a malaktech.hu/teszt oldalt, es oda kuldi az eredmenyt.

## Mit csinal a program
- **Kiolvassa:** gep tipusa, szeriaszama, CPU / RAM / GPU / SSD (SMART, kopas), akkumulator (battery report),
  BIOS-jelszo allapota, BitLocker, Windows-licenc, ceges MDM / Autopilot regisztracio
  (csak a ceg domainje, es hogy aktiv-e vagy csak regi maradvany). A gep nevet es a felhasznalo nevet NEM.
- **Mer:** CPU (egy/tobb szal, tartos terheles, orajel), RAM savszelesseg/kesleltetes, memoriateszt, GPU, SSD.
- **Atallit (a meres idejere, utana visszaallit):** energiaellatas maxra (sajat ideiglenes sema + Windows energiamod:
  Legjobb teljesitmeny), hibrid grafikanal a nagy GPU a Cinebench / FurMark idejere.
- **Elkuld:** az eredmenyt a `https://malaktech.hu/wp-json/malak-teszt/v1/relay/` cimre, a gep szeriaszamahoz mentve.
- **Nem:** nem telepit semmit, nem olvas szemelyes fajlokat, jelszavakat vagy bongeszoadatot, nem tolt le es nem futtat kulso kodot.

Eredmeny es naplo helyben: `Asztal\malak-teszt`.

## Fajlok
- `Program.cs` - keret: UAC, menu (1 teljes / 2 csak adatgyujtes / 3 + 10 perces stabilitas), energiaellatas, gyujto futtatasa, JSON, feltoltes
- `MB.cs` - benchmark: CPU, RAM, memoriateszt, SSD
- `GL.cs` - OpenGL kitoltes-teszt
- `Ext.cs` - Cinebench R15 es FurMark inditasa, pontszam, mag-orajel mintavetel, NVIDIA GPU naplo, hibrid grafika
- `collect.ps1` - a gepadat-gyujto PowerShell-szkript (az exe-be agyazva)
- `malak-teszt.bat` - Windows B terv: ugyanaz a gyujto + rovid benchmark, .exe nelkul
- `malak-teszt-mac.command` - Mac segedszkript (system_profiler, akku, MDM/DEP, FileVault, opcionalis rovid benchmark)
- `build.sh` - forditas mono `mcs`-sel a .NET Framework 4.8 referencia-szerelvenyei ellen
- `.github/workflows/build.yml` - automatikus forditas es kiadas

## Forditas
```
./build.sh        # Linux / macOS: apt install mono-mcs | brew install mono
```
Kimenet: `out/malak-teszt_es-bench.exe` + `out/SHA256.txt`. Kiadas: verzio-tag (pl. `v3.6.0`) push-olasa.

## Kodalairasi szabalyzat (Code signing policy)
Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- Committers and reviewers: [Malak-Kft](https://github.com/Malak-Kft)
- Approvers: [Malak-Kft](https://github.com/Malak-Kft)
- Minden alairt kiadas ebbol a nyilvanos forraskodbol, a GitHub Actions-ben fordul; minden alairast kezzel hagyunk jova.

## Adatvedelem
A program csak a fenti "Kiolvassa" pontban felsorolt adatokat kuldi el, kizarolag a malaktech.hu/teszt oldalra
(a gep tesztlapjahoz). Mas szamitogepre / szolgaltatonak nem kuld adatot.
Kapcsolat: info@malaktech.hu

## Licenc
MIT - lasd [LICENSE](LICENSE). Valtozasok: [CHANGELOG.md](CHANGELOG.md).
