#!/bin/bash
# Malak Tech - laptop teszt segedszkript (macOS)  -  malaktech.hu/teszt  v3.6
# Forras: https://github.com/Malak-Kft/malak-teszt (MIT)
# Inditas (letoltes utan, Terminalban): bash ~/Downloads/malak-teszt-mac.command
# Nem telepit es nem modosit semmit. Kimenet: ~/Desktop/malak-teszt/malak-teszt-mac-<serial>.json
export LC_ALL=C  # pontos szamformatum + angol kimenet: magyar locale-ban az awk tizedesvesszot irt (8,6), ami ervenytelen JSON volt
OUT="$HOME/Desktop/malak-teszt"; mkdir -p "$OUT"
KOD=$(printf '%s' "${1:-}" | tr '[:lower:]' '[:upper:]' | tr -cd 'A-Z0-9')
case "$KOD" in [A-Z0-9][A-Z0-9][A-Z0-9][A-Z0-9][A-Z0-9][A-Z0-9][A-Z0-9][A-Z0-9]) ;; *) KOD="" ;; esac
if [ -z "$KOD" ]; then
  # sajat bongeszo-kod (mint az .exe-nel): megnyitjuk vele a /teszt oldalt, oda erkezik az eredmeny, kodot nem kell beirni
  KOD=$(LC_ALL=C tr -dc 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789' </dev/urandom 2>/dev/null | head -c 8)
  if [ ${#KOD} -eq 8 ]; then
    echo "Bongeszo-kod: $KOD - megnyitom a malaktech.hu/teszt oldalt, oda erkezik az eredmeny."
    open "https://malaktech.hu/teszt/?k=$KOD" 2>/dev/null
  else KOD=""; fi
fi
echo "Malak Tech - adatgyujtes (10-30 mp)..."
js(){ printf '"%s"' "$(printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e 's/\t/ /g' | awk 'NR>1{printf "\\n"} {printf "%s",$0}')"; }
SP=$(system_profiler -json SPHardwareDataType SPSoftwareDataType SPDisplaysDataType SPMemoryDataType SPStorageDataType SPNVMeDataType SPSerialATADataType SPPowerDataType SPBluetoothDataType SPCameraDataType SPAirPortDataType SPAudioDataType 2>/dev/null)
[ -z "$SP" ] && SP='{}'
# v3.6 adatvedelem: a gep neve es a felhasznalo neve nem kerul a jelentesbe
SP=$(printf '%s' "$SP" | sed -E 's/("(local_host_name|user_name|computer_name|host_name)"[[:space:]]*:[[:space:]]*)"[^"]*"/\1""/g')
BATT=$(ioreg -r -c AppleSmartBattery -a 2>/dev/null | plutil -convert json -o - - 2>/dev/null); [ -z "$BATT" ] && BATT='null'
SN=$(system_profiler SPHardwareDataType 2>/dev/null | awk -F': ' '/Serial Number/{print $2}' | tr -d ' ')
MDM=$(profiles status -type enrollment 2>&1)
FV=$(fdesetup status 2>&1)
SIP=$(csrutil status 2>&1)
OSV=$(sw_vers -productVersion 2>/dev/null)
CPU=$(sysctl -n machdep.cpu.brand_string 2>/dev/null)
NCPU=$(sysctl -n hw.ncpu 2>/dev/null); MEM=$(sysctl -n hw.memsize 2>/dev/null); [ -z "$NCPU" ] && NCPU=0; [ -z "$MEM" ] && MEM=0
SMART=$(diskutil info disk0 2>/dev/null | awk -F': *' '/SMART Status/{print $2}')
FWPW=$(firmwarepasswd -check 2>&1 | head -1)
UPT=$(uptime 2>/dev/null)
TOUCHID=$(bioutil -c 2>&1 | head -3 | tr '\n' ' ')
ANS=""; if [ -r /dev/tty ]; then echo "Nativ benchmark? (CPU, RAM, memoriateszt; kb. 1-2 perc) [i/N]: "; read -r ANS </dev/tty; fi
NATIVE='null'
if [[ "$ANS" =~ ^[iIyY] ]]; then
  echo "Benchmark..."; T0=$(date +%s)
  # CPU: bash-aritmetika (lassu, de determinisztikus) - egy szal, majd minden mag parhuzamosan
  ms(){ perl -MTime::HiRes=time -e 'printf "%.0f", time*1000'; }
  work(){ local n=$1 h=2166136261 i; for ((i=0;i<n;i++)); do h=$(( (h ^ i) * 16777619 & 0xFFFFFFFF )); done; }
  N=600000
  S1=$(ms); work $N; S2=$(ms); D=$(( S2 - S1 )); [ "$D" -lt 1 ] && D=1
  CPU1=$(( N / D ))
  S1=$(ms); for ((t=0;t<NCPU;t++)); do work $N & done; wait; S2=$(ms); D=$(( S2 - S1 )); [ "$D" -lt 1 ] && D=1
  CPUN=$(( N * NCPU / D ))
  # RAM + memoriateszt: dd-vel /dev/zero -> memoria (tmpfs nincs), ezert ramfs helyett /dev/null masolas: savszelesseg becsles
  BW=$(dd if=/dev/zero of=/dev/null bs=64m count=64 2>&1 | awk '/bytes/{for(i=1;i<=NF;i++) if($i=="bytes/sec)") print $(i-1)}' | tr -d '(')
  [ -z "$BW" ] && BW=$(dd if=/dev/zero of=/dev/null bs=64m count=64 2>&1 | tail -1 | grep -o '[0-9]* bytes/sec' | awk '{print $1}')
  BWGB=$(awk -v b="$BW" 'BEGIN{printf "%.1f", b/1e9}' | tr ',' '.')
  # SSD: 1 GB iras (fsync) + olvasas (purge nelkul, cache-elt lehet)
  DW=$(dd if=/dev/zero of="$OUT/.disktest" bs=8m count=128 conv=fsync 2>&1 | tail -1 | grep -o '[0-9]* bytes/sec' | awk '{print int($1/1048576)}')
  DR=$(dd if="$OUT/.disktest" of=/dev/null bs=8m 2>&1 | tail -1 | grep -o '[0-9]* bytes/sec' | awk '{print int($1/1048576)}'); rm -f "$OUT/.disktest"
  [ -z "$BWGB" ] && BWGB=0; [ -z "$DW" ] && DW=0; [ -z "$DR" ] && DR=0
  NATIVE="{\"engine\":\"bash\",\"cpu1\":$CPU1,\"cpuN\":$CPUN,\"threads\":$NCPU,\"memBw\":$BWGB,\"diskW\":$DW,\"diskR\":$DR,\"unit\":\"kit/s (bash)\",\"note\":\"bash-benchmark: csak macek egymas kozt hasonlithatok, a bongeszos meres a merv ado\",\"secs\":$(( $(date +%s) - T0 ))}"
fi
[ -z "$SN" ] && SN=nosn
F="$OUT/malak-teszt-mac-$SN.json"
{
printf '{"platform":"mac","tool":"malaktech.hu/teszt mac v3.6","generated":"%s","serial":"%s","osVersion":"%s","cpuBrand":%s,"ncpu":%s,"memBytes":%s,"smart":%s,"mdm":%s,"filevault":%s,"sip":%s,"firmwarePw":%s,"uptime":%s,"touchid":%s,"native":%s,"sp":%s,"batteryRaw":%s}\n' \
 "$(date +%Y-%m-%dT%H:%M:%S)" "$SN" "$OSV" "$(js "$CPU")" "$NCPU" "$MEM" "$(js "$SMART")" "$(js "$MDM")" "$(js "$FV")" "$(js "$SIP")" "$(js "$FWPW")" "$(js "$UPT")" "$(js "$TOUCHID")" "$NATIVE" "$SP" "$BATT"
} > "$F"
echo "Kesz: $F"
if [ -n "$KOD" ]; then
  OK=""; HC=""
  for i in 1 2 3; do
    HC=$(LC_ALL=C tr -d '\000-\010\013-\037' < "$F" | curl -sS -m 90 -o /dev/null -w '%{http_code}' -X POST -H "Content-Type: text/plain; charset=utf-8" --data-binary @- "https://malaktech.hu/wp-json/malak-teszt/v1/relay/put?code=$KOD&kind=json" 2>/dev/null)
    [ "$HC" = "200" ] && { OK=1; break; }; sleep 2
  done
  [ -z "$HC" ] && HC="nincs kapcsolat"
  if [ -n "$OK" ]; then echo "Elkuldve a bongeszobe (kod: $KOD): ha a malaktech.hu/teszt oldal nyitva van, magatol beolvassa."; else echo "Automatikus kuldes nem sikerult (HTTP $HC) - huzd a fajlt a malaktech.hu/teszt oldalra (1. lepes)."; fi
else echo "Huzd a fajlt a malaktech.hu/teszt oldalra (1. lepes)."; fi
open "$OUT" 2>/dev/null
