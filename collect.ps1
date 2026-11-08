#MALAK-COLLECT v2
#PSSTART
$ErrorActionPreference='SilentlyContinue'
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'malak-teszt'
New-Item -ItemType Directory -Force -Path $out | Out-Null
function G($cls,$ns){ if($ns){ Get-CimInstance -Namespace $ns -ClassName $cls } else { Get-CimInstance -ClassName $cls } }
function Dec($arr){ if($arr){ -join ($arr | ? {$_ -ne 0} | % {[char]$_}) } }
$bios=G Win32_BIOS; $cs=G Win32_ComputerSystem; $csp=G Win32_ComputerSystemProduct; $os=G Win32_OperatingSystem; $cpu=G Win32_Processor
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$memType=@{20='DDR';21='DDR2';24='DDR3';26='DDR4';27='LPDDR';28='LPDDR2';29='LPDDR3';30='LPDDR4';34='DDR5';35='LPDDR5'}
$ram = G Win32_PhysicalMemory | % { [pscustomobject]@{ gb=[math]::Round($_.Capacity/1GB,1); speed=$_.ConfiguredClockSpeed; type=$memType[[int]$_.SMBIOSMemoryType]; maker=$_.Manufacturer; part=($_.PartNumber -replace '\s+$',''); slot=$_.DeviceLocator; ff=$_.FormFactor } }
$phys = Get-PhysicalDisk | % { $r = $_ | Get-StorageReliabilityCounter; [pscustomobject]@{ name=$_.FriendlyName; media=[string]$_.MediaType; bus=[string]$_.BusType; gb=[math]::Round($_.Size/1GB); health=[string]$_.HealthStatus; serial=$_.SerialNumber; wear=$r.Wear; powerOnHours=$r.PowerOnHours; temp=$r.Temperature; readErr=$r.ReadErrorsTotal; idx=[int]$_.DeviceId } }
# --- SMART: NVMe egeszseg-lap (IOCTL, rendszergazda nelkul is sokszor megy) + SATA/ATA attributumok (WMI) ---
try {
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles;
public static class MalakSmart {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFileW(string n, uint a, uint s, IntPtr sa, uint d, uint f, IntPtr t);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle h, uint c, byte[] i, int il, byte[] o, int ol, out int r, IntPtr ov);
  public static byte[] NvmeLog(int drive, int page) {
    uint[] acc = new uint[] { 0u, 0x80000000u, 0xC0000000u };
    foreach (uint a in acc) {
      SafeFileHandle h = CreateFileW("\\\\.\\PhysicalDrive" + drive, a, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
      if (h.IsInvalid) continue;
      try {
        byte[] b = new byte[8 + 40 + 512];
        Array.Copy(BitConverter.GetBytes(50), 0, b, 0, 4);
        Array.Copy(BitConverter.GetBytes(3), 0, b, 8, 4);
        Array.Copy(BitConverter.GetBytes(2), 0, b, 12, 4);
        Array.Copy(BitConverter.GetBytes(page), 0, b, 16, 4);
        Array.Copy(BitConverter.GetBytes(40), 0, b, 24, 4);
        Array.Copy(BitConverter.GetBytes(512), 0, b, 28, 4);
        int r;
        if (DeviceIoControl(h, 0x2D1400u, b, b.Length, b, b.Length, out r, IntPtr.Zero)) {
          int off = 8 + BitConverter.ToInt32(b, 24); int len = BitConverter.ToInt32(b, 28);
          if (off < 8 || len < 64 || off + len > b.Length) { off = 48; len = 512; }
          byte[] o = new byte[512]; Array.Copy(b, off, o, 0, Math.Min(512, len)); return o;
        }
      } finally { h.Close(); }
    }
    return null;
  }
}
'@ -ErrorAction Stop
} catch {}
function AtaAttr($vs){ $a=@{}; if(-not $vs -or $vs.Count -lt 362){ return $a }; for($i=0;$i -lt 30;$i++){ $o=2+$i*12; $id=[int]$vs[$o]; if($id -eq 0){ continue }; $raw=[uint64]0; for($k=5;$k -ge 0;$k--){ $raw=($raw*256)+[uint64]$vs[$o+5+$k] }; $a[$id]=@{ v=[int]$vs[$o+3]; w=[int]$vs[$o+4]; raw=$raw } }; $a }
function TbOf($name,$raw){ $n=([string]$name).ToUpper(); $mul=512; $u='LBA*512'; if($n -match 'INTEL|SOLIDIGM'){ $mul=33554432; $u='32MiB' } elseif($n -match 'SANDISK|WDC|WD |WESTERN|WD_'){ $mul=1073741824; $u='GiB' }; $tb=[double]$raw*$mul/1e12; if($tb -gt 20000){ $tb=[double]$raw*512/1e12; $u='LBA*512?' }; [pscustomobject]@{ tb=[math]::Round($tb,2); unit=$u } }
$dd=@(); try { $dd=@(G Win32_DiskDrive) } catch {}; $fp=@(); try { $fp=@(G MSStorageDriver_FailurePredictData root\wmi) } catch {}
$phys = @($phys | % { $d=$_; $sm=$null
  try {
    if($d.bus -eq 'NVMe' -and ('MalakSmart' -as [type])){ $b=[MalakSmart]::NvmeLog([int]$d.idx,2)
      if($b -and @($b | ? { $_ -ne 0 }).Count -gt 0){ $k=[int][BitConverter]::ToUInt16($b,1); $used=[int]$b[5]
        $sm=[pscustomobject]@{ src='nvme'; crit=[int]$b[0]; tempC=$(if($k -gt 200){ $k-273 }); spare=[int]$b[3]; spareTh=[int]$b[4]; used=$used; lifeLeft=[math]::Max(0,100-$used); tbRead=[math]::Round([BitConverter]::ToUInt64($b,32)*512000/1e12,2); tbWritten=[math]::Round([BitConverter]::ToUInt64($b,48)*512000/1e12,2); powerCycles=[int64][BitConverter]::ToUInt64($b,112); poh=[int64][BitConverter]::ToUInt64($b,128); unsafe=[int64][BitConverter]::ToUInt64($b,144); mediaErr=[int64][BitConverter]::ToUInt64($b,160); errLog=[int64][BitConverter]::ToUInt64($b,176); unit='nvme' } } }
    if(-not $sm){ $w=$dd | ? { [int]$_.Index -eq [int]$d.idx } | select -First 1
      if($w){ $pnp=([string]$w.PNPDeviceID).ToUpper(); $f=$fp | ? { (([string]$_.InstanceName) -replace '_0$','').ToUpper() -eq $pnp } | select -First 1
        if($f -and $f.VendorSpecific){ $a=AtaAttr $f.VendorSpecific
          if($a.Count -gt 0){ $life=$null; foreach($id in 231,233,177,202,173,169){ if($a.ContainsKey($id) -and $a[$id].v -ge 1 -and $a[$id].v -le 100){ $life=$a[$id].v; break } }
            $tw=$null; $tr=$null; $u=$null; foreach($id in 241,246){ if($a.ContainsKey($id) -and $a[$id].raw -gt 0){ $t=TbOf $d.name $a[$id].raw; $tw=$t.tb; $u=$t.unit; break } }; if($a.ContainsKey(242) -and $a[242].raw -gt 0){ $tr=(TbOf $d.name $a[242].raw).tb }
            $tp=$null; foreach($id in 194,190){ if($a.ContainsKey($id)){ $t2=[int]($a[$id].raw -band 0xFFFF); if($t2 -gt 200){ $t2=[int]($a[$id].raw -band 0xFF) }; if($t2 -gt 0 -and $t2 -lt 120){ $tp=$t2; break } } }
            $poh=$null; if($a.ContainsKey(9)){ $poh=[int64]($a[9].raw -band 0xFFFFFFFF) }
            $sm=[pscustomobject]@{ src='ata'; crit=0; tempC=$tp; used=$(if($null -ne $life){ 100-$life }); lifeLeft=$life; tbRead=$tr; tbWritten=$tw; unit=$u; poh=$poh; powerCycles=$(if($a.ContainsKey(12)){ [int64]($a[12].raw -band 0xFFFFFFFF) }); realloc=$(if($a.ContainsKey(5)){ [int64]($a[5].raw -band 0xFFFFFFFF) }); pending=$(if($a.ContainsKey(197)){ [int64]($a[197].raw -band 0xFFFFFFFF) }); uncorr=$(if($a.ContainsKey(198)){ [int64]($a[198].raw -band 0xFFFFFFFF) } elseif($a.ContainsKey(187)){ [int64]($a[187].raw -band 0xFFFFFFFF) }); crc=$(if($a.ContainsKey(199)){ [int64]($a[199].raw -band 0xFFFFFFFF) }); attrs=@($a.Keys | sort | % { [pscustomobject]@{ id=$_; v=$a[$_].v; w=$a[$_].w; raw=$a[$_].raw } }) } } } } }
  } catch {}
  if($sm){ $d | Add-Member -NotePropertyName smart -NotePropertyValue $sm -Force
    if(($null -eq $d.wear -or $d.wear -eq 0) -and $null -ne $sm.used){ $d.wear=$sm.used }
    if(-not $d.powerOnHours -and $sm.poh){ $d.powerOnHours=$sm.poh }
    if(-not $d.temp -and $sm.tempC){ $d.temp=$sm.tempC } }
  $d })
# --- PCIe link (NVMe): generacio es savszelesseg a vezerlo PnP-tulajdonsagaibol (rendszergazda nelkul is megy) ---
if(Get-Command Get-PnpDeviceProperty -ErrorAction SilentlyContinue){
$phys = @($phys | % { $d=$_
  try { if($d.bus -eq 'NVMe'){ $w=$dd | ? { [int]$_.Index -eq [int]$d.idx } | select -First 1
    if($w){ $par=[string](Get-PnpDeviceProperty -InstanceId $w.PNPDeviceID -KeyName 'DEVPKEY_Device_Parent' -ErrorAction Stop).Data
      if($par -and $par -like 'PCI\*'){ $g=@{}; Get-PnpDeviceProperty -InstanceId $par -KeyName 'DEVPKEY_PciDevice_CurrentLinkSpeed','DEVPKEY_PciDevice_CurrentLinkWidth','DEVPKEY_PciDevice_MaxLinkSpeed','DEVPKEY_PciDevice_MaxLinkWidth','DEVPKEY_NAME' -ErrorAction Stop | % { $g[$_.KeyName]=$_.Data }
        $pcie=[pscustomobject]@{ gen=[int]$g['DEVPKEY_PciDevice_CurrentLinkSpeed']; lanes=[int]$g['DEVPKEY_PciDevice_CurrentLinkWidth']; maxGen=[int]$g['DEVPKEY_PciDevice_MaxLinkSpeed']; maxLanes=[int]$g['DEVPKEY_PciDevice_MaxLinkWidth']; ctrl=[string]$g['DEVPKEY_NAME'] }
        if($pcie.gen -gt 0 -or $pcie.maxGen -gt 0){ $d | Add-Member -NotePropertyName pcie -NotePropertyValue $pcie -Force } } } } } catch {}
  $d })
}
$gpus = G Win32_VideoController | % { [pscustomobject]@{ name=$_.Name; driver=$_.DriverVersion; date=$_.DriverDate; ram=[math]::Round($_.AdapterRAM/1GB,1); res="$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)@$($_.CurrentRefreshRate)"; status=$_.Status } }
$mons = G WmiMonitorID root\wmi | % { $p = G WmiMonitorBasicDisplayParams root\wmi | ? InstanceName -eq $_.InstanceName; $w=$p.MaxHorizontalImageSize; $h=$p.MaxVerticalImageSize; [pscustomobject]@{ name=(Dec $_.UserFriendlyName); maker=(Dec $_.ManufacturerName); serial=(Dec $_.SerialNumberID); year=$_.YearOfManufacture; cm="$w x $h"; inch=$(if($w){[math]::Round([math]::Sqrt($w*$w+$h*$h)/2.54,1)}) } }
$bat = G Win32_Battery | select Name, EstimatedChargeRemaining, BatteryStatus, DesignVoltage
$bstat = G BatteryStaticData root\wmi; $bfull = G BatteryFullChargedCapacity root\wmi; $bcyc = G BatteryCycleCount root\wmi
$battery = [pscustomobject]@{ present=[bool]$bat; name=$bat.Name; percent=$bat.EstimatedChargeRemaining; status=$bat.BatteryStatus; design=$bstat.DesignedCapacity; full=$bfull.FullChargedCapacity; cycles=$bcyc.CycleCount; maker=$bstat.ManufactureName; serial=$bstat.SerialNumber; chem=$bstat.Chemistry }
$pnpAll = Get-PnpDevice -PresentOnly
$badDev = $pnpAll | ? { $_.Status -ne 'OK' } | % { [pscustomobject]@{ name=$_.FriendlyName; cls=$_.Class; status=[string]$_.Status; problem=$_.ProblemDescription; id=$_.InstanceId } }
$touch = ($pnpAll | ? { $_.Class -eq 'HIDClass' -and $_.FriendlyName -match 'touch screen|touchscreen|erint' }).FriendlyName
$pen = ($pnpAll | ? { $_.FriendlyName -match 'pen|toll|stylus|wacom' -and $_.Class -match 'HIDClass|Mouse' }).FriendlyName
$bio = ($pnpAll | ? { $_.Class -eq 'Biometric' }).FriendlyName
$cams = ($pnpAll | ? { $_.Class -match 'Camera|Image' }).FriendlyName
$bt = ($pnpAll | ? { $_.Class -eq 'Bluetooth' }).FriendlyName
$tpad = ($pnpAll | ? { $_.FriendlyName -match 'touchpad|touch pad|precision' }).FriendlyName
$net = Get-NetAdapter | % { [pscustomobject]@{ name=$_.Name; desc=$_.InterfaceDescription; status=[string]$_.Status; speed=$_.LinkSpeed } }
$tpm = Get-Tpm; $tpmw = G Win32_Tpm root\cimv2\security\microsofttpm
$bl = Get-BitLockerVolume | % { [pscustomobject]@{ vol=$_.MountPoint; prot=[string]$_.ProtectionStatus; status=[string]$_.VolumeStatus; pct=$_.EncryptionPercentage; method=[string]$_.EncryptionMethod } }
$sb = Confirm-SecureBootUEFI
$lic = G SoftwareLicensingProduct | ? { $_.PartialProductKey -and $_.ApplicationID -eq '55c92734-d682-4d71-983e-d6ec3f16059f' } | select -First 1 Name, Description, LicenseStatus
$dsreg = (& dsregcmd /status 2>&1) -join [string][char]10
# v3.6 adatvedelem: e-mail-cimekbol csak a domain marad (*@ceg.hu), a gepnev es a fiokneve sorok kimaradnak
$dsreg = (($dsreg -split [string][char]10) | ? { $_ -notmatch '^\s*(Device Name|Executing Account Name|UserEmail|User Name)\s*:' }) -join [string][char]10
$dsreg = $dsreg -replace '[A-Za-z0-9._%+\-]+@([A-Za-z0-9.\-]+\.[A-Za-z]{2,})','*@$1'

$ap = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Provisioning\Diagnostics\AutoPilot'
$apc = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Provisioning\Diagnostics\AutoPilot\EstablishedCorrelations'
$autopilot = [pscustomobject]@{ tenantId=$ap.CloudAssignedTenantId; tenantDomain=$ap.CloudAssignedTenantDomain; oobeConfig=$ap.CloudAssignedOobeConfig; ztdId=$apc.ZtdRegistrationId; entDfId=$apc.EntDMID; keyExists=[bool](Test-Path 'HKLM:\SOFTWARE\Microsoft\Provisioning\Diagnostics\AutoPilot') }
# v3.6 MDM: csak a ceg domainje kerul be (a teljes e-mail nem); minden regisztracional kulon jelezzuk, AKTIV-e:
# aktiv = EnrollmentState 1 (beiratkozott) ES van hozza EnterpriseMgmt utemezett feladat (a kliens tenylegesen szinkronizal).
# Ami csak maradvany (regi registry-bejegyzes, nincs feladat / nem beiratkozott allapot), az NEM szamit zarnak.
$enr = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Enrollments' | % {
  $p = Get-ItemProperty $_.PSPath
  if($p.UPN -or $p.ProviderID -or $p.DiscoveryServiceFullURL){
    $dom = $null; if($p.UPN -and ([string]$p.UPN) -match '@(.+)$'){ $dom = $Matches[1].ToLower() }
    if(-not $dom -and $p.DiscoveryServiceFullURL -and ([string]$p.DiscoveryServiceFullURL) -match '^https?://([^/]+)'){ $dom = $Matches[1].ToLower() }
    $tasks = @(Get-ScheduledTask -TaskPath ('\Microsoft\Windows\EnterpriseMgmt\' + $_.PSChildName + '\') -ErrorAction SilentlyContinue).Count
    $act = ([int]$p.EnrollmentState -eq 1) -and ($tasks -gt 0)
    [pscustomobject]@{ id=$_.PSChildName; upn=$(if($dom){ '*@' + $dom }); domain=$dom; provider=$p.ProviderID; type=$p.EnrollmentType; state=$p.EnrollmentState; url=$p.DiscoveryServiceFullURL; tasks=$tasks; active=$act }
  }
}
$mdmSum = [pscustomobject]@{
  active = [bool](@($enr | ? { $_.active }).Count -gt 0)
  activeDomains = @($enr | ? { $_.active -and $_.domain } | % { $_.domain } | select -Unique)
  leftoverDomains = @($enr | ? { -not $_.active -and $_.domain } | % { $_.domain } | select -Unique)
  leftoverCount = @($enr | ? { -not $_.active }).Count
}
$mdmTasks = @(Get-ScheduledTask -TaskPath '\Microsoft\Windows\EnterpriseMgmt\*' -ErrorAction SilentlyContinue).Count
$ct = [pscustomobject]@{ rpcnetService=[bool](Get-Service rpcnet, rpcnetp -ErrorAction SilentlyContinue); rpcnetFile=(Test-Path "$env:windir\System32\rpcnet.exe"); absoluteFile=(Test-Path "$env:windir\System32\rpcnetp.exe") }
$biosPw = $null
try { $l = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_BiosPasswordSettings -ErrorAction Stop; if($l){ $biosPw = 'Lenovo PasswordState=' + $l.PasswordState + ' (0=nincs, 1=power-on, 2=supervisor, 4=HDD user, 8=HDD master, 16=system mgmt)' } } catch {}
try { $h = Get-CimInstance -Namespace root\HP\InstrumentedBIOS -ClassName HP_BIOSPassword -ErrorAction Stop | % { $_.Name + '=' + $_.IsSet }; if($h){ $biosPw = 'HP ' + ($h -join ', ') } } catch {}
try { $d = Get-CimInstance -Namespace root\dcim\sysman\wmisecurity -ClassName PasswordObject -ErrorAction Stop | % { $_.NameId + '=' + $_.IsPasswordSet }; if($d){ $biosPw = 'Dell ' + ($d -join ', ') } } catch {}
if(-not $biosPw){ try { $h2 = Get-CimInstance -Namespace root\HP\InstrumentedBIOS -ClassName HP_BIOSSetting -ErrorAction Stop | ? { $_.Name -match 'Password' -and $_.PSObject.Properties['IsSet'] } | % { $_.Name + '=' + $_.IsSet }; if($h2){ $biosPw = 'HP ' + ($h2 -join ', ') } } catch {} }
if(-not $biosPw){ try { $d2 = Get-CimInstance -Namespace root\dcim\sysman -ClassName DCIM_BIOSPassword -ErrorAction Stop | % { $_.AttributeName + '=' + $_.IsSet }; if($d2){ $biosPw = 'Dell ' + ($d2 -join ', ') } } catch {} }
if(-not $biosPw){ if($isAdmin){ $biosPw = 'nem kiolvashato: ' + $cs.Manufacturer + ' - ennel a gyartonal nincs lekerdezheto BIOS-WMI (csak Lenovo/HP/Dell), nezd meg a BIOS-ban' } else { $biosPw = 'nem kiolvashato: rendszergazdai jog kell (futtasd ujra, Igen a UAC-ra)' } }
$hf = Get-HotFix | sort InstalledOn -Descending | select -First 1 HotFixID, InstalledOn
$dispVer = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').DisplayVersion
# --- asztali PC / hardver-reszletek: haztipus, alaplap, RAM nevleges vs beallitott, VRAM, monitor-kimenet, energia, inditas ---
$pcx = $null
try {
  $enc = G Win32_SystemEnclosure; $chassis = @($enc.ChassisTypes | % { [int]$_ })
  $isDesk = [bool]($chassis | ? { $_ -in 3,4,5,6,7,13,15,16,17,23,24,35,36 }); $isLap = [bool]($chassis | ? { $_ -in 8,9,10,11,14,30,31,32 })
  $bb = G Win32_BaseBoard; $pma = G Win32_PhysicalMemoryArray
  $ramx = G Win32_PhysicalMemory | % { [pscustomobject]@{ slot=$_.DeviceLocator; bank=$_.BankLabel; gb=[math]::Round($_.Capacity/1GB,1); rated=$_.Speed; cfg=$_.ConfiguredClockSpeed; type=$memType[[int]$_.SMBIOSMemoryType]; maker=$_.Manufacturer; part=($_.PartNumber -replace '\s+$',''); mv=$_.ConfiguredVoltage; mvMin=$_.MinVoltage; mvMax=$_.MaxVoltage; ff=$_.FormFactor } }
  $vram = @(); try { $vram = Get-ChildItem 'HKLM:\SYSTEM\ControlSet001\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}' -ErrorAction Stop | ? { $_.PSChildName -match '^\d{4}$' } | % { $p = Get-ItemProperty $_.PSPath; if($p.DriverDesc){ [pscustomobject]@{ name=$p.DriverDesc; vramGb=$(if($p.'HardwareInformation.qwMemorySize'){ [math]::Round([double]$p.'HardwareInformation.qwMemorySize'/1GB,1) } elseif($p.'HardwareInformation.MemorySize' -is [int]){ [math]::Round($p.'HardwareInformation.MemorySize'/1GB,1) }) } } } } catch {}
  $vot = @{ '-2'='ismeretlen'; '-1'='egyeb'; '0'='VGA'; '1'='S-Video'; '2'='kompozit'; '4'='DVI'; '5'='HDMI'; '6'='LVDS'; '10'='DisplayPort'; '11'='DP (belso)'; '14'='SDI'; '15'='DP'; '16'='DP (beepitett)'; '2147483648'='belso kijelzo'; '4294967295'='ismeretlen' }
  $monc = G WmiMonitorConnectionParams root\wmi | % { $t=[string]$_.VideoOutputTechnology; [pscustomobject]@{ inst=$_.InstanceName; conn=$(if($vot.ContainsKey($t)){ $vot[$t] } else { $t }) } }
  $scheme = ((powercfg /getactivescheme) -join ' ') -replace '^.*?\(','' -replace '\)\s*$',''
  $fast = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -ErrorAction SilentlyContinue).HiberbootEnabled
  $bootMs = $null; try { $ev = Get-WinEvent -FilterHashtable @{ LogName='Microsoft-Windows-Diagnostics-Performance/Operational'; Id=100 } -MaxEvents 1 -ErrorAction Stop; $bootMs = [int](([xml]$ev.ToXml()).Event.EventData.Data | ? { $_.Name -eq 'BootTime' }).'#text' } catch {}
  $pf = G Win32_PerfFormattedData_Counters_ProcessorInformation | ? { $_.Name -eq '_Total' }
  $usb = @(G Win32_USBController | % { $_.Name }); $snd = @(G Win32_SoundDevice | % { $_.Name })
  $wifi = @($net | ? { $_.desc -match 'Wi-?Fi|Wireless|WLAN|802\.11' }).Count -gt 0
  $pcx = [pscustomobject]@{ chassis=$chassis; isDesktop=$isDesk; isLaptop=$isLap; board=[pscustomobject]@{ maker=$bb.Manufacturer; model=$bb.Product; version=$bb.Version; serial=$bb.SerialNumber }; biosVersion=$bios.SMBIOSBIOSVersion; biosDate=$(if($bios.ReleaseDate){ $bios.ReleaseDate.ToString('yyyy-MM-dd') }); memSlots=$pma.MemoryDevices; memMaxGb=$(if($pma.MaxCapacityEx){ [math]::Round($pma.MaxCapacityEx/1MB) } elseif($pma.MaxCapacity){ [math]::Round($pma.MaxCapacity/1MB) }); ram=@($ramx); vram=@($vram); monitorConn=@($monc); powerScheme=$scheme; fastStartup=$fast; bootMs=$bootMs; cpuPerfIdle=$(if($pf){ [int]$pf.PercentProcessorPerformance }); cpuMhzIdle=$(if($pf){ [int]$pf.ProcessorFrequency }); cpuMaxMhz=$cpu.MaxClockSpeed; usb=$usb; audio=$snd; wifi=$wifi; bluetooth=(@($bt).Count -gt 0) }
} catch {}
$native = $null
$obj = [ordered]@{
  generated=(Get-Date).ToString('s'); tool='malaktech.hu/teszt bat v2'; platform='windows'; admin=$isAdmin
  computer=[pscustomobject]@{ maker=$cs.Manufacturer; model=$cs.Model; family=$cs.SystemFamily; sku=$cs.SystemSKUNumber; version=$csp.Version; serial=$bios.SerialNumber; serial2=$csp.IdentifyingNumber; biosVer=$bios.SMBIOSBIOSVersion; biosDate=$bios.ReleaseDate; pcType=$cs.PCSystemType; biosPassword=$biosPw }
  cpu=[pscustomobject]@{ name=($cpu.Name -replace '\s+',' ').Trim(); cores=$cpu.NumberOfCores; threads=$cpu.NumberOfLogicalProcessors; mhz=$cpu.MaxClockSpeed }
  ram=[pscustomobject]@{ totalGb=[math]::Round($cs.TotalPhysicalMemory/1GB,1); slots=(G Win32_PhysicalMemoryArray).MemoryDevices; modules=@($ram) }
  disks=@($phys); gpus=@($gpus); monitors=@($mons); battery=$battery
  devices=[pscustomobject]@{ total=$pnpAll.Count; bad=@($badDev); touch=@($touch); pen=@($pen); biometric=@($bio); cameras=@($cams); bluetooth=@($bt); touchpad=@($tpad) }
  network=@($net)
  os=[pscustomobject]@{ name=$os.Caption; version=$os.Version; build=$os.BuildNumber; display=$dispVer; installed=$os.InstallDate; boot=$os.LastBootUpTime; arch=$os.OSArchitecture; activation=$lic; lastHotfix=$hf }
  security=[pscustomobject]@{ tpmPresent=$tpm.TpmPresent; tpmReady=$tpm.TpmReady; tpmVersion=$tpmw.SpecVersion; secureBoot=$sb; bitlocker=@($bl); computrace=$ct }
  lock=[pscustomobject]@{ dsregcmd=$dsreg; autopilot=$autopilot; enrollments=@($enr); mdmTasks=$mdmTasks; mdm=$mdmSum }
  native=$native
  pc=$pcx
}
$sn = ($bios.SerialNumber -replace '[^A-Za-z0-9\-]','')
if(-not $sn){ $sn='nosn' }
$json = Join-Path $out ("malak-teszt-" + $sn + ".json")
[IO.File]::WriteAllText($json, ($obj | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding $false))
& powercfg /batteryreport /output (Join-Path $out 'battery-report.html') | Out-Null
Write-Host ("Kesz: " + $json)
if($env:MALAK_KOD -and -not $env:MALAK_NOUPLOAD){ try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $ru = 'https://malaktech.hu/wp-json/malak-teszt/v1/relay/put?code=' + $env:MALAK_KOD; $brp = Join-Path $out 'battery-report.html'; if(Test-Path $brp){ Invoke-RestMethod -Uri ($ru + '&kind=battery') -Method Post -ContentType 'text/html; charset=utf-8' -Body ([IO.File]::ReadAllBytes($brp)) -TimeoutSec 60 -ErrorAction Stop | Out-Null }; Invoke-RestMethod -Uri ($ru + '&kind=json') -Method Post -ContentType 'application/json; charset=utf-8' -Body ([IO.File]::ReadAllBytes($json)) -TimeoutSec 60 -ErrorAction Stop | Out-Null; Write-Host 'Elkuldve a bongeszobe: ha a malaktech.hu/teszt oldal nyitva van, magatol beolvassa.' -ForegroundColor Green } catch { Write-Host 'Automatikus kuldes nem sikerult - huzd be kezzel a fajlokat az oldalra.' -ForegroundColor Yellow } }
[IO.File]::WriteAllText((Join-Path $env:TEMP 'malak-teszt-path.txt'), $json)
