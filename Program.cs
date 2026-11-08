using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

public static class Program
{
	[StructLayout(LayoutKind.Sequential)]
	private class MEMSTAT
	{
		public uint len = 64u;

		public uint load;

		public ulong total;

		public ulong avail;

		public ulong totPage;

		public ulong availPage;

		public ulong totVirt;

		public ulong availVirt;

		public ulong availExt;
	}

	private delegate bool CtrlHandler(int ctrlType);

	private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

	private static string pwOrig = null;

	private static string pwTmp = null;

	private static bool pwOk = false;

	private static bool pwEver = false;

	private static CtrlHandler ctrlH;

	private const string HIGH_PERF = "8c5e7fda-e8bf-4a96-9a85-cf48a53c7635";

	[DllImport("kernel32.dll")]
	private static extern bool GlobalMemoryStatusEx([In][Out] MEMSTAT m);

	private static string N(double d, string f = "0.#")
	{
		return (!double.IsNaN(d) && !double.IsInfinity(d)) ? d.ToString(f, IC) : "null";
	}

	private static string J(string s)
	{
		if (s == null)
		{
			return "null";
		}
		StringBuilder stringBuilder = new StringBuilder("\"");
		foreach (char c in s)
		{
			if (c == '"' || c == '\\')
			{
				stringBuilder.Append('\\').Append(c);
			}
			else if (c < ' ')
			{
				stringBuilder.Append(' ');
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.Append('"').ToString();
	}

	private static void W(string s)
	{
		try
		{
			Console.WriteLine(s);
		}
		catch
		{
		}
		try
		{
			lock (logBuf)
			{
				logBuf.Append('[').Append(DateTime.Now.ToString("HH:mm:ss", IC)).Append("] ").AppendLine(s);
			}
		}
		catch
		{
		}
	}

	private static bool Ask(string q, bool def)
	{
		Console.Write(q + ((!def) ? " [i/N]: " : " [I/n]: "));
		string text = string.Empty;
		try
		{
			text = (Console.ReadLine() ?? string.Empty).Trim();
		}
		catch
		{
		}
		try
		{
			lock (logBuf)
			{
				logBuf.AppendLine("  ? " + q + " -> " + ((text == string.Empty) ? "(Enter)" : text));
			}
		}
		catch
		{
		}
		if (text == string.Empty)
		{
			return def;
		}
		return text.StartsWith("i", StringComparison.OrdinalIgnoreCase) || text.StartsWith("y", StringComparison.OrdinalIgnoreCase);
	}

	private static void Post(string url, string ct, byte[] body)
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
		httpWebRequest.Method = "POST";
		httpWebRequest.ContentType = ct;
		httpWebRequest.Timeout = 60000;
		httpWebRequest.ReadWriteTimeout = 60000;
		httpWebRequest.UserAgent = "malak-teszt_es-bench/" + VER;
		httpWebRequest.ContentLength = body.Length;
		using (Stream stream = httpWebRequest.GetRequestStream())
		{
			stream.Write(body, 0, body.Length);
		}
		using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
		{
			if (httpWebResponse.StatusCode >= HttpStatusCode.MultipleChoices)
			{
				throw new Exception("HTTP " + (int)httpWebResponse.StatusCode);
			}
		}
	}

	private static int RunCmd(string exe, string args, out string output)
	{
		output = string.Empty;
		try
		{
			Process process = new Process();
			process.StartInfo = new ProcessStartInfo(exe, args)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			process.Start();
			output = process.StandardOutput.ReadToEnd();
			process.WaitForExit(20000);
			return process.ExitCode;
		}
		catch
		{
			return -1;
		}
	}

	[DllImport("kernel32.dll")]
	private static extern bool SetConsoleCtrlHandler(CtrlHandler h, bool add);

	private static string Guid1(string s)
	{
		Match match = Regex.Match(s ?? string.Empty, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
		return (!match.Success) ? null : match.Value;
	}

	// ===== v3.6 energiaellatas: az egesz rendszer maximalis teljesitmenyen a meres idejere =====
	// 1) sajat ideiglenes sema: Ultimate Performance masolata, ha nincs: Nagy teljesitmeny, ha az sincs (Modern Standby gepek): a jelenlegi masolata
	// 2) a sajat semaban (halozatrol es akkurol is): CPU min/max 100%, boost agressziv, EPP 0, magparkolas ki, PCIe ASPM ki, lemez/alvas/kepernyo-idozito ki
	// 3) Windows 10/11 energiamod-csuszka (overlay): Legjobb teljesitmeny
	// A vegen (es megszakitaskor) minden visszaall; ha a program elszallna, a kovetkezo inditas allitja vissza (jelzofajl).
	private static Guid pwOverlayOrig = Guid.Empty;
	private static bool pwOverlayOk = false;
	private static readonly Guid OverlayBest = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

	[DllImport("powrprof.dll", EntryPoint = "PowerGetActualOverlayScheme")]
	private static extern uint PowerGetActualOverlayScheme(out Guid overlay);

	[DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
	private static extern uint PowerSetActiveOverlayScheme(Guid overlay);

	private static string PwMarker()
	{
		return Path.Combine(Path.GetTempPath(), "malak-teszt-energia.txt");
	}

	private static void PwSet(string sub, string setting, int val)
	{
		string o;
		RunCmd("powercfg", "/setacvalueindex " + pwTmp + " " + sub + " " + setting + " " + val, out o);
		RunCmd("powercfg", "/setdcvalueindex " + pwTmp + " " + sub + " " + setting + " " + val, out o);
	}

	private static void PowerMax()
	{
		PowerRecover();
		string output;
		RunCmd("powercfg", "/getactivescheme", out output);
		pwOrig = Guid1(output);
		string level = null;
		string[][] bases = new string[][] {
			new string[] { "e9a42b02-d5df-448d-aa00-03f14749eb61", "Ultimate Performance" },
			new string[] { "8c5e7fda-e8bf-4a96-9a85-cf48a53c7635", "Nagy teljesitmeny" },
			new string[] { pwOrig, "a jelenlegi sema maxra hangolt masolata" } };
		foreach (string[] b in bases)
		{
			if (b[0] == null) continue;
			RunCmd("powercfg", "-duplicatescheme " + b[0], out output);
			string g = Guid1(output);
			if (g != null && !g.Equals(b[0], StringComparison.OrdinalIgnoreCase)) { pwTmp = g; level = b[1]; break; }
		}
		if (pwTmp != null)
		{
			RunCmd("powercfg", "/changename " + pwTmp + " \"Malak teszt - max (ideiglenes)\"", out output);
			PwSet("SUB_PROCESSOR", "PROCTHROTTLEMIN", 100);
			PwSet("SUB_PROCESSOR", "PROCTHROTTLEMAX", 100);
			PwSet("SUB_PROCESSOR", "PERFBOOSTMODE", 2);
			PwSet("SUB_PROCESSOR", "PERFEPP", 0);
			PwSet("SUB_PROCESSOR", "PERFEPP1", 0);
			PwSet("SUB_PROCESSOR", "CPMINCORES", 100);
			PwSet("SUB_PROCESSOR", "CPMINCORES1", 100);
			PwSet("SUB_PCIEXPRESS", "ASPM", 0);
			PwSet("SUB_DISK", "DISKIDLE", 0);
			PwSet("SUB_SLEEP", "STANDBYIDLE", 0);
			PwSet("SUB_SLEEP", "HIBERNATEIDLE", 0);
			PwSet("SUB_VIDEO", "VIDEOIDLE", 0);
			if (RunCmd("powercfg", "/setactive " + pwTmp, out output) == 0)
			{
				pwOk = true;
				pwEver = true;
			}
			else
			{
				RunCmd("powercfg", "-delete " + pwTmp, out output);
				pwTmp = null;
			}
		}
		try
		{
			Guid cur;
			if (PowerGetActualOverlayScheme(out cur) == 0)
			{
				pwOverlayOrig = cur;
				if (PowerSetActiveOverlayScheme(OverlayBest) == 0)
				{
					pwOverlayOk = true;
					pwEver = true;
				}
			}
		}
		catch
		{
		}
		try
		{
			if (pwOk || pwOverlayOk)
			{
				File.WriteAllText(PwMarker(), (pwOrig ?? "") + "\n" + (pwTmp ?? "") + "\n" + (pwOverlayOk ? pwOverlayOrig.ToString() : ""));
			}
		}
		catch
		{
		}
		if (pwOk)
		{
			W("Energiasema a meres idejere: " + level + " (CPU 100%, boost, alvas/kepernyo-kikapcsolas tiltva).");
		}
		else
		{
			W("Az energiasemat nem sikerult atallitani, a jelenlegivel merek.");
		}
		W(pwOverlayOk ? "Windows energiamod: Legjobb teljesitmeny." : "A Windows energiamod-csuszkat nem sikerult allitani (regebbi Windows vagy tiltva).");
		if (pwOk || pwOverlayOk)
		{
			W("A meres vegen minden energiabeallitast visszaallitok az eredetire.");
		}
	}

	private static void PowerRestore()
	{
		string output;
		try
		{
			if (pwOk && pwOrig != null)
			{
				RunCmd("powercfg", "/setactive " + pwOrig, out output);
			}
			if (pwTmp != null)
			{
				RunCmd("powercfg", "-delete " + pwTmp, out output);
			}
		}
		catch
		{
		}
		try
		{
			if (pwOverlayOk)
			{
				PowerSetActiveOverlayScheme(pwOverlayOrig);
			}
		}
		catch
		{
		}
		if (pwOk || pwOverlayOk)
		{
			W("Energiabeallitasok visszaallitva.");
		}
		try
		{
			File.Delete(PwMarker());
		}
		catch
		{
		}
		pwOk = false;
		pwOverlayOk = false;
		pwTmp = null;
	}

	// Ha egy korabbi futas felbeszakadt (lefagyas, kilovés), az ott hagyott ideiglenes semat es csuszka-allast helyreallitja.
	private static void PowerRecover()
	{
		try
		{
			string m = PwMarker();
			if (!File.Exists(m)) return;
			string[] l = File.ReadAllText(m).Replace("\r", "").Split('\n');
			string output;
			if (l.Length > 0 && Guid1(l[0]) != null) RunCmd("powercfg", "/setactive " + Guid1(l[0]), out output);
			if (l.Length > 1 && Guid1(l[1]) != null) RunCmd("powercfg", "-delete " + Guid1(l[1]), out output);
			if (l.Length > 2 && Guid1(l[2]) != null) { try { PowerSetActiveOverlayScheme(new Guid(Guid1(l[2]))); } catch { } }
			File.Delete(m);
			W("Egy korabbi, felbeszakadt meres energiabeallitasait visszaallitottam.");
		}
		catch
		{
		}
	}

	private static bool IsAdmin()
	{
		try
		{
			return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	// ======================================================================= v3.4: allapot, naplo, reszeredmeny, kuldes
	private const string VER = "3.6";

	private static readonly StringBuilder logBuf = new StringBuilder();

	private static readonly List<string> parts = new List<string>();      // a "native" objektum darabjai: "kulcs":ertek

	private static readonly List<string> errors = new List<string>();     // szakasz-hibak (a JSON-ba is bekerulnek)

	private static string relay = null;       // 8 karakteres bongeszo-kod

	private static string jsonPath = null;    // a gyujto altal irt JSON (Asztal\malak-teszt\...)

	private static string baseJson = null;    // a gyujto JSON-ja, ahogy a gyujto megirta ("native": null)

	private static bool benchOn = false;

	private static bool batterySent = false;

	private static string stage = "indulas";

	private static int finished = 0;

	private static bool sentOk = false;

	private static Stopwatch swAll = Stopwatch.StartNew();

	private static string OutDir()
	{
		try
		{
			if (jsonPath != null)
			{
				return Path.GetDirectoryName(jsonPath);
			}
		}
		catch
		{
		}
		string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "malak-teszt");
		try
		{
			Directory.CreateDirectory(d);
		}
		catch
		{
		}
		return d;
	}

	// A teljes konzol-naplo fajlba: Asztal\malak-teszt\malak-teszt-naplo.txt (hibakereseshez)
	private static void SaveLog()
	{
		try
		{
			string t;
			lock (logBuf)
			{
				t = logBuf.ToString();
			}
			File.WriteAllText(Path.Combine(OutDir(), "malak-teszt-naplo.txt"), t, new UTF8Encoding(false));
		}
		catch
		{
		}
	}

	private static void Err(string where, Exception ex)
	{
		string m = where + ": " + ((ex == null) ? "ismeretlen hiba" : (ex.GetType().Name + ": " + ex.Message));
		lock (errors)
		{
			errors.Add(m);
		}
		W("  HIBA - " + m);
		try
		{
			if (ex != null)
			{
				lock (logBuf)
				{
					logBuf.AppendLine(ex.ToString());
				}
			}
		}
		catch
		{
		}
	}

	private static void Part(string p)
	{
		if (string.IsNullOrEmpty(p))
		{
			return;
		}
		lock (parts)
		{
			parts.Add(p.TrimStart(','));
		}
	}

	// Egy meresi szakasz: ha elszall, a tobbi szakasz es a kuldes attol meg lefut.
	private static void Stage(string name, Action a)
	{
		stage = name;
		try
		{
			a();
		}
		catch (Exception ex)
		{
			Err(name, ex);
		}
		try
		{
			GC.Collect();
		}
		catch
		{
		}
		WriteResult(false);
		SaveLog();
	}

	private static string NativeJson(bool complete)
	{
		StringBuilder b = new StringBuilder("{");
		lock (parts)
		{
			foreach (string p in parts)
			{
				b.Append(p).Append(',');
			}
		}
		b.Append("\"powerSwitched\":").Append((!pwEver) ? "false" : "true");
		b.Append(",\"complete\":").Append((!complete) ? "false" : "true");
		b.Append(",\"lastStage\":").Append(J(stage));
		b.Append(",\"errors\":[");
		lock (errors)
		{
			for (int i = 0; i < errors.Count; i++)
			{
				if (i > 0)
				{
					b.Append(',');
				}
				b.Append(J(errors[i]));
			}
		}
		b.Append("],\"engine\":\".NET/C# (malak-teszt_es-bench.exe v" + VER + ")\"}");
		return b.ToString();
	}

	private static readonly Regex NativeNull = new Regex("\"native\"\\s*:\\s*null");

	// A teljes eredmeny-JSON szovege (gyujto + native). partial = az oldal tudja, hogy a meres meg fut.
	private static string ResultText(bool complete, bool partial)
	{
		string nat = (benchOn && (parts.Count > 0 || errors.Count > 0 || complete)) ? NativeJson(complete) : "null";
		string pre = (!partial) ? string.Empty : "\"partial\": true, ";
		if (baseJson != null)
		{
			Match m = NativeNull.Match(baseJson);
			if (m.Success)
			{
				return baseJson.Substring(0, m.Index) + pre + "\"native\": " + nat + baseJson.Substring(m.Index + m.Length);
			}
			if (nat == "null")
			{
				return baseJson;
			}
		}
		return "{\"platform\":\"windows\"," + pre + "\"native\":" + nat + "}";
	}

	// Minden szakasz utan fajlba irjuk, ami eddig megvan: ha a gep lefagy / kikapcsol, a reszeredmeny megmarad.
	private static void WriteResult(bool complete)
	{
		if (!benchOn)
		{
			return;
		}
		try
		{
			if (jsonPath == null)
			{
				jsonPath = Path.Combine(OutDir(), "malak-bench.json");
			}
			string tmp = jsonPath + ".tmp";
			File.WriteAllText(tmp, ResultText(complete, false), new UTF8Encoding(false));
			try
			{
				if (File.Exists(jsonPath))
				{
					File.Delete(jsonPath);
				}
			}
			catch
			{
			}
			File.Move(tmp, jsonPath);
		}
		catch (Exception ex)
		{
			W("  (az eredmenyfajl irasa nem sikerult: " + ex.Message + ")");
		}
	}

	private static void PostRetry(string url, string ct, byte[] body)
	{
		Exception last = null;
		for (int i = 0; i < 3; i++)
		{
			try
			{
				Post(url, ct, body);
				return;
			}
			catch (WebException ex)
			{
				last = ex;
				HttpWebResponse r = ex.Response as HttpWebResponse;
				if (r != null)
				{
					last = new Exception("HTTP " + (int)r.StatusCode + " " + r.StatusDescription);
					try
					{
						r.Close();
					}
					catch
					{
					}
				}
			}
			catch (Exception ex2)
			{
				last = ex2;
			}
			Thread.Sleep(2000);
		}
		throw last ?? new Exception("ismeretlen hiba");
	}

	// Kuldes a nyitott /teszt oldalra. partial: koztes allapot (gepadatok / reszeredmeny), a vegen jon a teljes.
	private static bool Send(bool complete, bool partial, string what)
	{
		if (relay == null)
		{
			return false;
		}
		try
		{
			try
			{
				ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
			}
			catch
			{
				ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
			}
			ServicePointManager.Expect100Continue = false;
			string u = "https://malaktech.hu/wp-json/malak-teszt/v1/relay/put?code=" + relay;
			if (!batterySent)
			{
				try
				{
					string bp = Path.Combine(OutDir(), "battery-report.html");
					if (File.Exists(bp))
					{
						PostRetry(u + "&kind=battery", "text/html; charset=utf-8", File.ReadAllBytes(bp));
						batterySent = true;
					}
				}
				catch (Exception ex)
				{
					W("  (az akkujelentes kuldese nem sikerult: " + ex.Message + ")");
				}
			}
			byte[] body = new UTF8Encoding(false).GetBytes(ResultText(complete, partial));
			PostRetry(u + "&kind=json", "application/json; charset=utf-8", body);
			W("  Elkuldve a bongeszobe: " + what + " (" + Math.Max(1, body.Length / 1024) + " KB, kod: " + relay + ").");
			return true;
		}
		catch (Exception ex2)
		{
			W("  A KULDES NEM SIKERULT (" + what + "): " + ex2.Message);
			lock (errors)
			{
				errors.Add("kuldes (" + what + "): " + ex2.Message);
			}
			return false;
		}
	}

	// A vege: akarmi tortent, ami megvan, azt fajlba irjuk es elkuldjuk. Csak egyszer fut le.
	private static void Finish(bool crashed)
	{
		if (Interlocked.Exchange(ref finished, 1) != 0)
		{
			return;
		}
		try
		{
			PowerRestore();
		}
		catch
		{
		}
		WriteResult(!crashed);
		SaveLog();
		if (relay != null && (jsonPath != null || benchOn))
		{
			W("\nKuldes a bongeszobe (malaktech.hu/teszt)...");
			sentOk = Send(!crashed, false, crashed ? "reszeredmeny (a program hibaval allt le)" : "vegeredmeny");
			if (sentOk)
			{
				W("  Ha a malaktech.hu/teszt oldal nyitva van, par masodperc alatt magatol beolvassa.");
			}
		}
		W("\n=============================================================");
		W(" " + (crashed ? "HIBAVAL ALLT LE" : "Kesz") + " (" + N(swAll.Elapsed.TotalMinutes, "0.0") + " perc). Eredmeny: " + (jsonPath ?? "(nem keszult)"));
		int ne;
		lock (errors)
		{
			ne = errors.Count;
			if (ne > 0)
			{
				W(" Hibak a futas kozben (" + ne + "):");
				foreach (string e in errors)
				{
					W("   - " + e);
				}
			}
		}
		if (sentOk)
		{
			W(" Az eredmeny mar a bongeszoben van (malaktech.hu/teszt).");
		}
		else
		{
			if (relay == null)
			{
				W(" Nem volt bongeszo-kod, ezert nem kuldtem el automatikusan.");
			}
			W(" Huzd a megnyilt mappa .json es battery-report.html fajljat");
			W(" a malaktech.hu/teszt oldalra.");
		}
		W(" Naplo (hibakereseshez): " + Path.Combine(OutDir(), "malak-teszt-naplo.txt"));
		W("=============================================================");
		SaveLog();
		try
		{
			Process.Start("explorer.exe", "\"" + OutDir() + "\"");
		}
		catch
		{
		}
	}

	// v3.5 (2026-11-06): ha nincs kod, a program maga general egyet, es megnyitja a malaktech.hu/teszt oldalt
	// ezzel a koddal (?k=KOD). Az oldal a koddal varja az eredmenyt, beolvassa es elmenti a gep szeriaszamara,
	// a Keszlet ebbol parositja a gephez. Igy nem kell kodot beirni, es az .exe-t nem kell az oldalrol letolteni.
	private static string NewCode()
	{
		const string A = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
		byte[] b = new byte[8];
		using (System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create())
		{
			rng.GetBytes(b);
		}
		char[] o = new char[8];
		for (int i = 0; i < 8; i++)
		{
			o[i] = A[b[i] % 32];
		}
		return new string(o);
	}

	private static void OpenPage(string code)
	{
		string url = "https://malaktech.hu/teszt/?k=" + code;
		try
		{
			// explorer.exe-n keresztul: a bongeszo akkor is normal (nem rendszergazdai) jogokkal indul, ha mi mar emelt jogon futunk
			Process.Start("explorer.exe", "\"" + url + "\"");
			W("  Megnyitottam a bongeszoben: " + url);
		}
		catch (Exception ex)
		{
			W("  A bongeszot nem tudtam megnyitni (" + ex.Message + "). Nyisd meg kezzel: " + url);
		}
	}

	private static string ReadCode()
	{
		string t = string.Empty;
		try
		{
			t = (Console.ReadLine() ?? string.Empty).ToUpperInvariant();
		}
		catch
		{
		}
		t = Regex.Replace(t, "[^A-Z0-9]", string.Empty);
		return (t.Length != 8) ? null : t;
	}

	public static int Main(string[] args)
	{
		AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
		{
			// hatterszal vagy elkaphatatlan hiba: mentes + kuldes, mielott a Windows bezarja a programot
			try
			{
				Err("varatlan hiba (" + stage + ")", e.ExceptionObject as Exception);
				Finish(true);
				Console.Write("\nEnter a kilepeshez...");
				Console.ReadLine();
			}
			catch
			{
			}
		};
		int rc = 0;
		try
		{
			rc = Run(args);
		}
		catch (Exception ex)
		{
			Err("varatlan hiba (" + stage + ")", ex);
			Finish(true);
			rc = 1;
		}
		if (rc == 99)
		{
			return 0;
		}
		Console.Write("\nEnter a kilepeshez...");
		try
		{
			Console.ReadLine();
		}
		catch
		{
		}
		return rc;
	}

	private static int Run(string[] args)
	{
		try
		{
			Console.Title = "Malak Tech - laptop / PC teszt es benchmark";
		}
		catch
		{
		}
		bool flag = Array.IndexOf(args, "/noelev") >= 0;
		bool crashTest = Array.IndexOf(args, "/hibateszt") >= 0;   // rejtett kapcsolo: a hibakezeles kiprobalasa (a CPU-szakasz utan szandekos hiba)
		try
		{
			string text2 = typeof(Program).Assembly.Location;
			if (string.IsNullOrEmpty(text2))
			{
				text2 = Process.GetCurrentProcess().MainModule.FileName;
			}
			Match match = Regex.Match(Path.GetFileName(text2).ToUpperInvariant(), "_([A-Z0-9]{8})(?![A-Z0-9])");
			if (match.Success)
			{
				relay = match.Groups[1].Value;
			}
		}
		catch
		{
		}
		foreach (string text3 in args)
		{
			Match match2 = Regex.Match(text3.ToUpperInvariant(), "^/KOD:([A-Z0-9]{8})$");
			if (match2.Success)
			{
				relay = match2.Groups[1].Value;
			}
		}
		bool opened = Array.IndexOf(args, "/megnyitva") >= 0;
		if (relay == null)
		{
			// v3.5: nincs kod a fajlnevben es parancssorban sem -> sajat kod + az oldal megnyitasa (nem kerdezunk semmit)
			relay = NewCode();
			W("Sajat kodot generaltam: " + relay + " - megnyitom a malaktech.hu/teszt oldalt, oda erkezik az eredmeny.");
			OpenPage(relay);
			opened = true;
		}
		if (!IsAdmin() && !flag)
		{
			W("Rendszergazdai jogot kerek a teljes adathoz (akku, BitLocker, SMART, BIOS-jelszo)...");
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo(typeof(Program).Assembly.Location, "/noelev" + ((relay == null) ? string.Empty : (" /kod:" + relay)) + (opened ? " /megnyitva" : string.Empty) + ((!crashTest) ? string.Empty : " /hibateszt"));
				processStartInfo.UseShellExecute = true;
				processStartInfo.Verb = "runas";
				Process.Start(processStartInfo);
				return 99;
			}
			catch
			{
				W("Rendszergazdai jog nelkul folytatom, kevesebb adattal.\n");
			}
		}
		W("=============================================================");
		W(" Malak Tech - laptop / PC teszt es benchmark  v" + VER + "   (malaktech.hu/teszt)");
		W(" Nem telepit semmit. Kimenet: Asztal\\malak-teszt   Forras: github.com/Malak-Kft/malak-teszt");
		W("=============================================================");
		W(" Mit csinal a program:");
		W("  - a meres idejere az energiaellatast maxra allitja (CPU 100%, Windows energiamod:");
		W("    Legjobb teljesitmeny, hibrid grafikanal a nagy GPU), a vegen MINDENT visszaallit;");
		W("  - kiolvassa a gep adatait: tipus, szeriaszam, CPU/RAM/GPU/SSD (SMART), akku, BIOS-jelszo");
		W("    allapota, BitLocker, Windows-licenc, ceges (MDM/Autopilot) regisztracio - ebbol csak");
		W("    a ceg domainjet (a gep nevet es a felhasznalo nevet NEM);");
		W("  - az eredmenyt elkuldi a malaktech.hu/teszt oldalra (a gep szeriaszamahoz mentve).");
		W("    Szemelyes fajlokat, jelszavakat, bongeszoadatot NEM olvas es NEM kuld.");
		W(" Ha ezzel nem ertesz egyet, zard be most az ablakot.");
		W("=============================================================\n");
		W("Bongeszo-kod: " + relay + "  (a malaktech.hu/teszt oldalon ugyanennek kell latszania)");
		W("Az eredmenyt az oldal a gep szeriaszamara menti; ha a gep a Malak keszletben van, oda is felkerul.\n");
		W("Mit futtassak?");
		W("  1 = Teljes teszt: adatgyujtes + CPU, RAM (memoriateszt), GPU benchmark  (kb. 5-8 perc)  [ajanlott]");
		W("  2 = Csak adatgyujtes (gepadatok, akku, zarak, eszkozok)  (kb. 30 mp)");
		W("  3 = Teljes teszt + hosszu stabilitasteszt: 10 perc CPU-terheles + folyamatos memoriateszt (asztali PC-hez ajanlott)");
		Console.Write("Valasztas [1]: ");
		string text4 = string.Empty;
		try
		{
			text4 = (Console.ReadLine() ?? string.Empty).Trim();
		}
		catch
		{
		}
		benchOn = text4 != "2";
		bool flag3 = text4 == "3";
		bool flag4 = false;
		bool flag5 = true;
		bool cbOn = false;
		bool fmOn = false;
		string cbExe = null;
		string fmExe = null;
		if (benchOn)
		{
			flag5 = Ask("GPU terheles (megnyilik egy ablak kb. 20 mp-re)?", def: true);
			flag4 = Ask("SSD iras/olvasas teszt (1 GB-ot ir a lemezre, majd torli)?", def: false);
			try
			{
				cbExe = Ext.FindCinebench();
			}
			catch (Exception ex0)
			{
				Err("Cinebench kereses", ex0);
			}
			if (cbExe != null)
			{
				W("  Cinebench R15 megtalalva: " + cbExe);
				cbOn = Ask("Cinebench R15 futtatasa (tobbszalu + egyszalu, osszesen kb. 2-6 perc, kozben orajel-grafikon)?", def: true);
			}
			else
			{
				W("  (Cinebench R15 nem talalhato: Letoltesek / Asztal / 1tst mappa - kihagyom)");
			}
			try
			{
				fmExe = Ext.FindFurMark();
			}
			catch (Exception ex1)
			{
				Err("FurMark kereses", ex1);
			}
			if (fmExe != null)
			{
				W("  FurMark megtalalva: " + fmExe);
				fmOn = Ask("FurMark futtatasa (60 mp, a melegedest es a GPU-orajelet is merjuk)?", def: true);
			}
			else
			{
				W("  (FurMark nem talalhato telepitve - a sajat OpenGL terhelest hasznalom)");
			}
			W("\nKozben ne hasznald a gepet, es ha lehet, legyen toltore dugva (a Windows energiamod: Legjobb teljesitmeny).\n");
		}
		swAll = Stopwatch.StartNew();
		if (!IsAdmin())
		{
			W("FIGYELEM: rendszergazdai jog nelkul a BIOS-jelszo, a BitLocker es a reszletes akkuadat nem olvashato ki.\n   Ajanlott: zard be, inditsd ujra, es a Windows kerdesere (UAC) valaszolj Igen-nel.\n");
		}
		stage = "adatgyujtes";
		W("[1] Adatgyujtes... (10-30 mp)");
		try
		{
			string text6 = Path.Combine(Path.GetTempPath(), "malak-teszt-collect.ps1");
			string path = Path.Combine(Path.GetTempPath(), "malak-teszt-path.txt");
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
			// v3.6: csak a beagyazott (az .exe-vel egyutt alairt) gyujto fut, a program nem tolt le es nem futtat kulso kodot.
			using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream("collect.ps1"))
			{
				using (FileStream destination = File.Create(text6))
				{
					stream.CopyTo(destination);
				}
			}
			ProcessStartInfo processStartInfo2 = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + text6 + "\"");
			processStartInfo2.UseShellExecute = false;
			processStartInfo2.EnvironmentVariables["MALAK_NOUPLOAD"] = "1";
			Process process = Process.Start(processStartInfo2);
			if (!process.WaitForExit(300000))
			{
				W("  Az adatgyujtes 5 perc alatt sem vegzett - leallitom, es megyek tovabb.");
				try
				{
					process.Kill();
				}
				catch
				{
				}
				lock (errors)
				{
					errors.Add("adatgyujtes: idotullepes (5 perc)");
				}
			}
			try
			{
				File.Delete(text6);
			}
			catch
			{
			}
			if (File.Exists(path))
			{
				string p5 = File.ReadAllText(path).Trim();
				if (p5.Length > 0 && File.Exists(p5))
				{
					jsonPath = p5;
					baseJson = File.ReadAllText(p5);
				}
			}
		}
		catch (Exception ex)
		{
			Err("adatgyujtes", ex);
		}
		if (jsonPath == null)
		{
			W("  Figyelem: a gepadat-JSON nem keszult el" + ((!benchOn) ? "." : ", a benchmark kulon fajlba kerul."));
		}
		else
		{
			W("  Gepadatok: " + jsonPath);
		}
		SaveLog();
		if (benchOn && relay != null && jsonPath != null)
		{
			// a gepadatok mar most atmennek: rogton latszik az oldalon, hogy el a kapcsolat
			Send(false, true, "gepadatok (a meres meg fut)");
		}
		if (benchOn)
		{
			W(string.Empty);
			PowerMax();
			try
			{
				ctrlH = delegate
				{
					// ablak bezarasa / Ctrl+C: energiasema vissza, es ami megvan, az fajlban marad
					PowerRestore();
					try
					{
						lock (errors)
						{
							errors.Add("megszakitva (" + stage + ")");
						}
						WriteResult(false);
						SaveLog();
					}
					catch
					{
					}
					return false;
				};
				SetConsoleCtrlHandler(ctrlH, add: true);
			}
			catch
			{
			}
			AppDomain.CurrentDomain.ProcessExit += delegate
			{
				PowerRestore();
			};
			int processorCount = Environment.ProcessorCount;
			Stage("CPU", delegate
			{
				W("\n[2] CPU");
				W("  egyszalu (10 mp)...");
				double a = MB.CpuOne(10000);
				W("  tobbszalu, " + processorCount + " szal (10 mp)...");
				double a2 = MB.CpuAll(10000);
				W("  SHA-256 (3 mp)...");
				double d = MB.Sha(3000);
				W("  matrixszorzas, " + processorCount + " szal (6 mp)...");
				double d2 = MB.Gflops(6000);
				Part("\"cpu1\":" + N(Math.Round(a), "0") + ",\"cpuN\":" + N(Math.Round(a2), "0") + ",\"threads\":" + processorCount + ",\"sha256MBs\":" + N(d, "0") + ",\"gflops\":" + N(d2, "0.0"));
				W("  tartos terheles 60 mp (hutes / lassulas, orajel)...");
				int baseMhz = 0;
				try
				{
					RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0");
					if (registryKey != null)
					{
						baseMhz = Convert.ToInt32(registryKey.GetValue("~MHz"));
						registryKey.Close();
					}
				}
				catch
				{
				}
				PerformanceCounter perf = null;
				try
				{
					perf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
					perf.NextValue();
				}
				catch
				{
					perf = null;
				}
				List<double> mhzList = new List<double>();
				List<double> list2 = MB.Stress(60, delegate(int s, double sc)
				{
					double num18 = 0.0;
					try
					{
						if (perf != null && baseMhz > 0)
						{
							num18 = (double)((float)baseMhz * perf.NextValue()) / 100.0;
							mhzList.Add(num18);
						}
					}
					catch
					{
					}
					W("    " + s.ToString().PadLeft(2) + ". mp: " + Math.Round(sc) + ((!(num18 > 0.0)) ? string.Empty : ("   ~" + Math.Round(num18) + " MHz")));
				});
				double num = (list2.Count == 0) ? 0.0 : Math.Max(list2[0], (list2.Count <= 1) ? 0.0 : list2[1]);
				double num2 = 0.0;
				int num3 = Math.Min(3, list2.Count);
				for (int j = list2.Count - num3; j < list2.Count; j++)
				{
					num2 += list2[j];
				}
				if (num3 > 0)
				{
					num2 /= (double)num3;
				}
				double num4 = ((!(num > 0.0)) ? 0.0 : ((num2 / num - 1.0) * 100.0));
				W("  CPU: " + Math.Round(a) + " / " + Math.Round(a2) + " pont, SHA-256 " + N(d, "0") + " MB/s, " + N(d2, "0.0") + " GFLOPS, tartos terheles: " + ((!(num4 >= 0.0)) ? string.Empty : "+") + N(num4, "0") + " %");
				Part("\"stress\":[" + string.Join(",", list2.ConvertAll((double x) => N(x, "0")).ToArray()) + "],\"stressSec\":60,\"throttlePct\":" + N(num4, "0"));
				if (mhzList.Count > 0)
				{
					double num5 = 0.0;
					double num6 = 0.0;
					foreach (double item in mhzList)
					{
						if (item > num5)
						{
							num5 = item;
						}
					}
					int num7 = Math.Min(3, mhzList.Count);
					for (int k = mhzList.Count - num7; k < mhzList.Count; k++)
					{
						num6 += mhzList[k];
					}
					num6 /= (double)num7;
					W("  orajel terheles alatt: csucs ~" + Math.Round(num5) + " MHz, a vegen ~" + Math.Round(num6) + " MHz (alap: " + baseMhz + " MHz)" + ((!(num5 > (double)baseMhz * 1.05)) ? "  - FIGYELEM: nem megy az alap orajel fole (energiaseama / BIOS / hutes?)" : "  - a boost mukodik"));
					Part("\"baseMhz\":" + baseMhz + ",\"stressMhz\":[" + string.Join(",", mhzList.ConvertAll((double x) => N(x, "0")).ToArray()) + "],\"peakMhz\":" + N(num5, "0") + ",\"endMhz\":" + N(num6, "0") + ",\"boostOk\":" + ((!(num5 > (double)baseMhz * 1.05)) ? "false" : "true"));
				}
			});
			if (crashTest)
			{
				throw new InvalidOperationException("szandekos hiba (/hibateszt)");
			}
			if (cbOn)
			{
				Stage("Cinebench", delegate
				{
					W("\n[2b] Cinebench R15");
					Ext.CbResult cbr = Ext.RunCinebench(cbExe, true, W);
					Part(Ext.CbJson(cbr));
				});
			}
			long memTotalMb = 0;
			Stage("RAM", delegate
			{
				W("\n[3] RAM");
				W("  savszelesseg (1 szal, 512 MB)...");
				double d3 = 0.0;
				try
				{
					d3 = MB.MemBw(512);
				}
				catch (OutOfMemoryException)
				{
					GC.Collect();
					W("  (keves a szabad memoria, 128 MB-tal merek)");
					d3 = MB.MemBw(128);
				}
				W("  savszelesseg (osszes szal)...");
				double d4 = 0.0;
				try
				{
					d4 = MB.MemBwMT(128);
				}
				catch
				{
				}
				GC.Collect();
				W("  kesleltetes (veletlen hozzaferes, 256 MB)...");
				double d5 = MB.LatencyNs();
				Part("\"memBw\":" + N(d3, "0.0") + ",\"memBwMT\":" + N(d4, "0.0") + ",\"memLatNs\":" + N(d5, "0"));
				W("  RAM: " + N(d3, "0.0") + " GB/s (1 szal), " + N(d4, "0.0") + " GB/s (osszes), " + N(d5, "0") + " ns");
			});
			Stage("memoriateszt", delegate
			{
				GC.Collect();
				MEMSTAT mEMSTAT = new MEMSTAT();
				bool flag6 = false;
				try
				{
					flag6 = GlobalMemoryStatusEx(mEMSTAT);
				}
				catch
				{
				}
				if (!flag6)
				{
					mEMSTAT.total = 0uL;
					mEMSTAT.avail = 2147483648uL;
					try
					{
						PerformanceCounter val = new PerformanceCounter("Memory", "Available MBytes");
						try
						{
							mEMSTAT.avail = (ulong)val.NextValue() * 1048576;
						}
						finally
						{
							((IDisposable)val)?.Dispose();
						}
					}
					catch
					{
					}
				}
				memTotalMb = (long)(mEMSTAT.total / 1048576);
				long num8 = (long)((double)(mEMSTAT.avail / 1048576) * 0.85) - 300;
				if (num8 < 256)
				{
					num8 = 256L;
				}
				List<ulong[]> list3 = new List<ulong[]>();
				try
				{
					while ((long)list3.Count * 128L < num8)
					{
						list3.Add(new ulong[16777216]);
					}
				}
				catch (OutOfMemoryException)
				{
					if (list3.Count > 2)
					{
						list3.RemoveAt(list3.Count - 1);
					}
					GC.Collect();
				}
				long num9 = (long)list3.Count * 128L;
				W("  memoriateszt: " + num9 + " MB teleirasa es visszaellenorzese, " + MB.PatNames.Length + " mintazat (a szabad RAM ~85%-a; osszesen " + memTotalMb + " MB)...");
				Stopwatch stopwatch2 = Stopwatch.StartNew();
				long num10 = MB.MemTest(list3, W);
				double totalSeconds = stopwatch2.Elapsed.TotalSeconds;
				list3.Clear();
				list3 = null;
				GC.Collect();
				W("  memoriateszt: " + ((num10 != 0) ? ("HIBA: " + num10) : "HIBATLAN"));
				Part("\"memTestMb\":" + num9 + ",\"memTestThreads\":" + processorCount + ",\"memTestPatterns\":" + MB.PatNames.Length + ",\"memTestSec\":" + N(totalSeconds, "0") + ",\"memTestErrors\":" + num10 + ",\"memTotalMb\":" + memTotalMb);
			});
			if (flag3)
			{
				Stage("stabilitasteszt", delegate
				{
					W("\n[3b] Hosszu stabilitasteszt: 10 perc CPU-terheles + folyamatos memoriateszt (a szabad RAM ~40%-an)...");
					MEMSTAT mEMSTAT2 = new MEMSTAT();
					bool flag7 = false;
					try
					{
						flag7 = GlobalMemoryStatusEx(mEMSTAT2);
					}
					catch
					{
					}
					long num11 = (long)((!flag7) ? 2048 : (mEMSTAT2.avail / 1048576));
					long num12 = Math.Max(256L, (long)((double)num11 * 0.4));
					List<ulong[]> ch2 = new List<ulong[]>();
					try
					{
						while ((long)ch2.Count * 128L < num12)
						{
							ch2.Add(new ulong[16777216]);
						}
					}
					catch (OutOfMemoryException)
					{
						if (ch2.Count > 1)
						{
							ch2.RemoveAt(ch2.Count - 1);
						}
						GC.Collect();
					}
					long num13 = (long)ch2.Count * 128L;
					long stabErr = 0L;
					int passes = 0;
					bool runMem = true;
					string thErr = null;
					Thread thread = new Thread((ThreadStart)delegate
					{
						try
						{
							while (Volatile.Read(ref runMem))
							{
								long value = MB.MemTest(ch2, delegate
								{
								});
								Interlocked.Add(ref stabErr, value);
								Interlocked.Increment(ref passes);
							}
						}
						catch (Exception exT)
						{
							thErr = exT.Message;
						}
					});
					thread.IsBackground = true;
					thread.Start();
					List<double> list4 = MB.Stress(600, delegate(int s, double sc)
					{
						if (s % 60 == 0)
						{
							W("    " + s / 60 + ". perc: CPU " + Math.Round(sc) + ", memoriateszt-korok: " + Volatile.Read(ref passes) + ", hibak: " + Interlocked.Read(ref stabErr));
						}
					});
					Volatile.Write(ref runMem, value: false);
					thread.Join(120000);
					ch2.Clear();
					GC.Collect();
					if (thErr != null)
					{
						lock (errors)
						{
							errors.Add("stabilitasteszt memoriaszal: " + thErr);
						}
					}
					double num14 = (list4.Count == 0) ? 0.0 : Math.Max(list4[0], (list4.Count <= 1) ? 0.0 : list4[1]);
					double num15 = 0.0;
					int num16 = Math.Min(6, list4.Count);
					for (int l = list4.Count - num16; l < list4.Count; l++)
					{
						num15 += list4[l];
					}
					if (num16 > 0)
					{
						num15 /= (double)num16;
					}
					double num17 = ((!(num14 > 0.0)) ? 0.0 : ((num15 / num14 - 1.0) * 100.0));
					W("  Stabilitas: " + ((stabErr != 0) ? ("MEMORIAHIBA: " + stabErr) : "HIBATLAN") + ", " + passes + " memoriateszt-kor (" + num13 + " MB), CPU a vegen " + ((!(num17 >= 0.0)) ? string.Empty : "+") + N(num17, "0") + " %");
					Part("\"stabSec\":600,\"stabMemMb\":" + num13 + ",\"stabMemPasses\":" + passes + ",\"stabMemErrors\":" + stabErr + ",\"stabThrottlePct\":" + N(num17, "0"));
				});
			}
			if ((flag5 || fmOn) && relay != null)
			{
				// GPU-terheles elott koztes kuldes: ha a gep a GPU-teszt alatt fagy le, a CPU/RAM eredmeny mar az oldalon van
				stage = "koztes kuldes";
				Send(false, true, "CPU/RAM reszeredmeny (a GPU-teszt meg hatravan)");
			}
			if (flag5)
			{
				Stage("GPU", delegate
				{
					W("\n[4] GPU (OpenGL kitoltes, 20 mp)");
					GL.Result result = GL.Run(20, W);
					if (result.error != null)
					{
						W("  GPU teszt hiba: " + result.error);
					}
					else
					{
						W("  GPU: " + N(result.mpix, "0") + " Mpixel/s (" + result.layers + " reteg, " + result.w + "x" + result.h + ")");
					}
					bool flag8 = result.renderer != null && result.renderer.IndexOf("GDI Generic", StringComparison.OrdinalIgnoreCase) >= 0;
					if (flag8)
					{
						W("  FIGYELEM: 'GDI Generic' = nincs rendes videokartya-driver telepitve!");
					}
					Part("\"gpuRenderer\":" + J(result.renderer) + ",\"gpuVendor\":" + J(result.vendor) + ",\"gpuGlVersion\":" + J(result.version) + ",\"gpuMpix\":" + ((result.error != null) ? "null" : N(result.mpix, "0")) + ",\"gpuNoDriver\":" + ((!flag8) ? "false" : "true") + ",\"gpuError\":" + J(result.error));
				});
			}
			if (fmOn)
			{
				Stage("FurMark", delegate
				{
					W("\n[4b] FurMark");
					Ext.FmResult fmr = Ext.RunFurMark(fmExe, 60, W);
					if (fmr.Score >= 0)
					{
						W("  FurMark: " + fmr.Score.ToString("0", IC) + " pont" + ((fmr.Fps < 0) ? string.Empty : (", atlag " + N(fmr.Fps, "0.0") + " FPS")));
					}
					Part(Ext.FmJson(fmr));
				});
			}
			if (flag4)
			{
				Stage("SSD", delegate
				{
					W("\n[5] SSD (1 GB iras + olvasas + 4K veletlen olvasas)");
					double[] array = MB.Disk(Path.Combine(Path.GetTempPath(), "malak-disk-test.bin"), 1024);
					if (array != null)
					{
						W("  SSD: iras " + N(array[0], "0") + " MB/s, olvasas " + N(array[1], "0") + " MB/s, 4K " + N(array[2], "0") + " IOPS");
						Part("\"diskW\":" + N(array[0], "0") + ",\"diskR\":" + N(array[1], "0") + ",\"disk4kIops\":" + N(array[2], "0"));
					}
				});
			}
			else
			{
				W("\n[5] SSD teszt kihagyva.");
			}
			stage = "befejezes";
		}
		Finish(false);
		return 0;
	}
}
