// Ext.cs - v3.3 / v3.4: Cinebench R15 + FurMark inditas, pontszam-kiolvasas, mag-orajel es GPU (NVIDIA) adatnaplo
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

public static class Ext
{
	private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

	// ---------------------------------------------------------------- keresés
	private static List<string> Roots()
	{
		List<string> r = new List<string>();
		try { r.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)); } catch { }
		try { r.Add(KnownDownloads()); } catch { }
		try { r.Add(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", "Downloads")); } catch { }
		try { r.Add(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", "Desktop")); } catch { }
		try { r.Add(Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? "", "Desktop")); } catch { }
		try { r.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); } catch { }
		try { r.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); } catch { }
		try
		{
			foreach (DriveInfo d in DriveInfo.GetDrives())
			{
				if (d.DriveType == DriveType.Removable || d.DriveType == DriveType.Fixed)
				{
					if (d.IsReady) { r.Add(d.RootDirectory.FullName); }
				}
			}
		}
		catch { }
		List<string> u = new List<string>();
		foreach (string s in r)
		{
			if (!string.IsNullOrEmpty(s) && Directory.Exists(s) && !u.Contains(s)) { u.Add(s); }
		}
		return u;
	}

	[DllImport("shell32.dll")]
	private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

	// Letöltések mappa a Windows saját útvonalából (OneDrive-os gépen máshol lehet)
	private static string KnownDownloads()
	{
		IntPtr p;
		if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
		{
			string s = Marshal.PtrToStringUni(p);
			Marshal.FreeCoTaskMem(p);
			return s;
		}
		return null;
	}

	// mélység-korlátos keresés, hogy ne járja be az egész lemezt
	private static string FindFile(string root, Func<string, bool> match, int depth)
	{
		try
		{
			foreach (string f in Directory.GetFiles(root))
			{
				if (match(Path.GetFileName(f))) { return f; }
			}
			if (depth <= 0) { return null; }
			foreach (string d in Directory.GetDirectories(root))
			{
				string n = Path.GetFileName(d).ToLowerInvariant();
				if (n == "windows" || n == "$recycle.bin" || n == "system volume information" || n == "appdata" || n == "node_modules") { continue; }
				string x = FindFile(d, match, depth - 1);
				if (x != null) { return x; }
			}
		}
		catch { }
		return null;
	}

	public static string FindCinebench()
	{
		// R15 / R11.5: "CINEBENCH Windows 64 Bit.exe" - csak ezek ertik a -cb_cpux / -cb_cpu1 kapcsolot.
		// (Az R20 / R23 / 2024 neve "Cinebench.exe", mas kapcsolokkal megy: azt nem inditjuk el, mert megallna a foablakban.)
		Func<string, bool> strict = delegate(string n)
		{
			string l = n.ToLowerInvariant();
			return l.StartsWith("cinebench") && l.EndsWith(".exe") && l.Contains("windows") && !l.Contains("uninst") && !l.Contains("setup");
		};
		foreach (string r in Roots())
		{
			bool drive = r.Length <= 3;
			// a meghajto gyokereben csak sekelyen (2 szint), a szokasos helyeken melyebben
			string x = FindFile(r, strict, drive ? 2 : 4);
			if (x != null) { return Prefer64(x); }
		}
		return null;
	}

	// ha a mappaban a 32 es a 64 bites valtozat is ott van, 64 bites Windowson a 64 bitest futtatjuk
	private static string Prefer64(string exe)
	{
		try
		{
			if (!Environment.Is64BitOperatingSystem) { return exe; }
			string n = Path.GetFileName(exe);
			if (n.IndexOf("32", StringComparison.Ordinal) < 0) { return exe; }
			string alt = Path.Combine(Path.GetDirectoryName(exe), n.Replace("32", "64"));
			if (File.Exists(alt)) { return alt; }
		}
		catch { }
		return exe;
	}

	public static string FindFurMark()
	{
		Func<string, bool> m2 = delegate(string n)
		{
			string l = n.ToLowerInvariant();
			return l == "furmark.exe";
		};
		string[] pf = new string[] {
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty };
		foreach (string p in pf)
		{
			if (string.IsNullOrEmpty(p)) { continue; }
			string g = Path.Combine(p, "Geeks3D");
			if (Directory.Exists(g))
			{
				string x = FindFile(g, m2, 5);
				if (x != null) { return x; }
			}
		}
		foreach (string r in Roots())
		{
			string x = FindFile(r, m2, r.Length <= 3 ? 2 : 4);
			if (x != null) { return x; }
		}
		return null;
	}

	// ---------------------------------------------------------------- konzol-olvasás (Cinebench a szülő konzolra ír)
	[StructLayout(LayoutKind.Sequential)] private struct COORD { public short X; public short Y; }
	[StructLayout(LayoutKind.Sequential)] private struct SMALL_RECT { public short L, T, R, B; }
	[StructLayout(LayoutKind.Sequential)]
	private struct CSBI
	{
		public COORD size; public COORD cursor; public ushort attr; public SMALL_RECT win; public COORD max;
	}
	[DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int n);
	[DllImport("kernel32.dll")] private static extern bool GetConsoleScreenBufferInfo(IntPtr h, out CSBI i);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "ReadConsoleOutputCharacterW")]
	private static extern bool ReadConsoleOutputCharacter(IntPtr h, StringBuilder sb, uint len, COORD c, out uint read);

	private static string ConsoleText()
	{
		try
		{
			IntPtr h = GetStdHandle(-11);
			CSBI i;
			if (!GetConsoleScreenBufferInfo(h, out i)) { return string.Empty; }
			int w = i.size.X;
			int rows = Math.Min((int)i.size.Y, i.cursor.Y + 2);
			StringBuilder all = new StringBuilder();
			for (int y = 0; y < rows; y += 40)
			{
				int n = Math.Min(40, rows - y);
				StringBuilder sb = new StringBuilder(w * n + 1);
				uint rd;
				COORD c = new COORD(); c.X = 0; c.Y = (short)y;
				if (ReadConsoleOutputCharacter(h, sb, (uint)(w * n), c, out rd))
				{
					string s = sb.ToString(0, (int)Math.Min(rd, (uint)sb.Length));
					for (int k = 0; k < n; k++)
					{
						int st = k * w;
						if (st < s.Length) { all.AppendLine(s.Substring(st, Math.Min(w, s.Length - st)).TrimEnd()); }
					}
				}
			}
			return all.ToString();
		}
		catch { return string.Empty; }
	}

	// pontszamok a szovegben, megjelenesi sorrendben. R15: "Rendering (Multiple CPU) : 1234.56 pts", R20+: "CB 1234.56 (0.00)"
	public static List<double> CbScores(string txt)
	{
		List<double> l = new List<double>();
		foreach (Match m in Regex.Matches(txt ?? string.Empty, @"\bCB\s+(\d+(?:[.,]\d+)?)|CPU\s*\)\s*:?\s*(\d+(?:[.,]\d+)?)\s*(?:pts|cb)\b|(\d+(?:[.,]\d+)?)\s*(?:pts|cb)\b", RegexOptions.IgnoreCase))
		{
			string v = m.Groups[1].Success ? m.Groups[1].Value : (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
			double d;
			if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, IC, out d) && d > 0) { l.Add(d); }
		}
		return l;
	}

	private static string Tail(string s, int n) { if (s == null) { return string.Empty; } return s.Length > n ? s.Substring(s.Length - n) : s; }

	// varakozas a folyamatra, 30 masodpercenkent eletjellel; false = idotullepes
	private static bool WaitProgress(Process p, int maxSec, Action<string> log, string label)
	{
		Stopwatch sw = Stopwatch.StartNew();
		int lastTick = 0;
		while (!p.WaitForExit(1000))
		{
			int sec = (int)sw.Elapsed.TotalSeconds;
			if (sec >= maxSec) { return false; }
			if (sec / 30 > lastTick) { lastTick = sec / 30; log("    ... " + label + " fut (" + (sec / 60) + ":" + (sec % 60).ToString("00") + ")"); }
		}
		return true;
	}

	private static void KillByName(string exe)
	{
		try
		{
			foreach (Process q in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
			{
				try { q.Kill(); } catch { }
			}
		}
		catch { }
	}

	// ---------------------------------------------------------------- mag-órajel mintavétel
	public class Clock
	{
		public int Threads;
		public int[] Eff;                       // EfficiencyClass logikai magonként (nagyobb = P-mag)
		public List<float[]> Samples = new List<float[]>();
		public List<string> PhaseName = new List<string>();
		public List<int> PhaseStart = new List<int>();
		public List<int> PhaseEnd = new List<int>();
		public int StepMs = 500;
		private Thread t;
		private volatile bool run;
		private System.Diagnostics.PerformanceCounter[] perf, freq;

		public bool Start()
		{
			try
			{
				PerformanceCounterCategory cat = new PerformanceCounterCategory("Processor Information");
				List<string> inst = new List<string>();
				foreach (string s in cat.GetInstanceNames())
				{
					if (s.IndexOf("_Total", StringComparison.Ordinal) < 0) { inst.Add(s); }
				}
				// "csoport,cpu" sorrend szerint
				inst.Sort(delegate(string a, string b)
				{
					string[] x = a.Split(','), y = b.Split(',');
					int ga = int.Parse(x[0]), gb = int.Parse(y[0]);
					if (ga != gb) { return ga.CompareTo(gb); }
					return int.Parse(x[1]).CompareTo(int.Parse(y[1]));
				});
				if (inst.Count == 0) { return false; }
				Threads = inst.Count;
				perf = new System.Diagnostics.PerformanceCounter[Threads];
				freq = new System.Diagnostics.PerformanceCounter[Threads];
				for (int i = 0; i < Threads; i++)
				{
					perf[i] = new System.Diagnostics.PerformanceCounter("Processor Information", "% Processor Performance", inst[i]);
					freq[i] = new System.Diagnostics.PerformanceCounter("Processor Information", "Processor Frequency", inst[i]);
					perf[i].NextValue(); freq[i].NextValue();
				}
				Eff = EfficiencyClasses(Threads);
				run = true;
				t = new Thread(Loop);
				t.IsBackground = true;
				t.Start();
				return true;
			}
			catch { return false; }
		}

		private void Loop()
		{
			Stopwatch sw = Stopwatch.StartNew();
			long next = 0;
			while (run)
			{
				try
				{
					float[] row = new float[Threads];
					for (int i = 0; i < Threads; i++)
					{
						row[i] = (float)Math.Round(freq[i].NextValue() * perf[i].NextValue() / 100.0);
					}
					lock (Samples) { Samples.Add(row); }
				}
				catch { }
				next += StepMs;
				int wait = (int)(next - sw.ElapsedMilliseconds);
				if (wait > 0) { Thread.Sleep(wait); }
			}
		}

		public void Phase(string name) { lock (Samples) { PhaseName.Add(name); PhaseStart.Add(Samples.Count); PhaseEnd.Add(-1); } }
		public void EndPhase() { lock (Samples) { if (PhaseEnd.Count > 0 && PhaseEnd[PhaseEnd.Count - 1] < 0) { PhaseEnd[PhaseEnd.Count - 1] = Samples.Count; } } }
		public void Stop() { run = false; try { if (t != null) { t.Join(2000); } } catch { } }

		public string Json()
		{
			lock (Samples)
			{
				StringBuilder b = new StringBuilder();
				b.Append("{\"stepMs\":").Append(StepMs).Append(",\"threads\":").Append(Threads).Append(",\"eff\":[");
				for (int i = 0; i < Threads; i++) { if (i > 0) { b.Append(','); } b.Append((Eff != null && i < Eff.Length) ? Eff[i] : 0); }
				b.Append("],\"phases\":[");
				for (int p = 0; p < PhaseName.Count; p++)
				{
					if (p > 0) { b.Append(','); }
					int e = PhaseEnd[p] < 0 ? Samples.Count : PhaseEnd[p];
					b.Append("{\"name\":\"").Append(PhaseName[p]).Append("\",\"from\":").Append(PhaseStart[p]).Append(",\"to\":").Append(e).Append('}');
				}
				b.Append("],\"cores\":[");
				for (int i = 0; i < Threads; i++)
				{
					if (i > 0) { b.Append(','); }
					b.Append('[');
					for (int s = 0; s < Samples.Count; s++)
					{
						if (s > 0) { b.Append(','); }
						float fv = Samples[s][i];
							b.Append(((float.IsNaN(fv) || fv < 0 || fv > 100000) ? 0 : (int)fv).ToString(IC));
					}
					b.Append(']');
				}
				b.Append("]}");
				return b.ToString();
			}
		}
	}

	[DllImport("kernel32.dll")]
	private static extern bool GetSystemCpuSetInformation(IntPtr info, uint len, out uint ret, IntPtr proc, uint flags);

	// P/E mag felismerés (Windows 10 2004+ / 11): EfficiencyClass
	private static int[] EfficiencyClasses(int n)
	{
		int[] eff = new int[n];
		try
		{
			uint need;
			GetSystemCpuSetInformation(IntPtr.Zero, 0, out need, IntPtr.Zero, 0);
			if (need == 0) { return eff; }
			IntPtr buf = Marshal.AllocHGlobal((int)need);
			try
			{
				uint got;
				if (!GetSystemCpuSetInformation(buf, need, out got, IntPtr.Zero, 0)) { return eff; }
				int off = 0, idx = 0;
				while (off < got && idx < n)
				{
					int size = Marshal.ReadInt32(buf, off);
					int type = Marshal.ReadInt32(buf, off + 4);
					if (size <= 0) { break; }
					if (type == 0) { eff[idx++] = Marshal.ReadByte(buf, off + 18); }
					off += size;
				}
			}
			finally { Marshal.FreeHGlobal(buf); }
		}
		catch { }
		return eff;
	}

	// ---------------------------------------------------------------- Cinebench R15
	public class CbResult { public double Multi = -1, Single = -1; public string Error; public string Path; public string ClockJson; public string Raw; }

	// A Maxon leirasa szerint Windowson igy ir a Cinebench a konzolra:  start /b /wait "parentconsole" "CINEBENCH Windows 64 Bit.exe" -cb_cpux
	// (v3.3 kozvetlenul inditotta, igy a pontszam nem jelent meg). A kimenetet ket helyrol olvassuk: atiranyitott csobol ES a konzol kepernyo-pufferebol.
	public static CbResult RunCinebench(string exe, bool doSingle, Action<string> log)
	{
		CbResult r = new CbResult();
		r.Path = exe;
		Clock clk = new Clock();
		bool clkOk = false;
		try { clkOk = clk.Start(); } catch { clkOk = false; }
		if (!clkOk) { log("  (az orajel-mintavetel nem indult el, a pontszam igy is megy)"); }
		StringBuilder rawAll = new StringBuilder();
		try
		{
			string[,] runs = new string[,] { { "-cb_cpux", "cbMulti" }, { "-cb_cpu1", "cbSingle" } };
			string comspec = Environment.GetEnvironmentVariable("ComSpec");
			if (string.IsNullOrEmpty(comspec) || !File.Exists(comspec)) { comspec = "cmd.exe"; }
			for (int k = 0; k < (doSingle ? 2 : 1); k++)
			{
				int before = CbScores(ConsoleText()).Count;
				log(k == 0 ? "  Cinebench R15 tobbszalu (kb. 1-3 perc, az ablak magatol bezarul; ha licenc-ablak jon, fogadd el)..." : "  Cinebench R15 egyszalu (kb. 1-5 perc)...");
				if (clkOk) { clk.Phase(runs[k, 1]); }
				ProcessStartInfo psi = new ProcessStartInfo(comspec, "/d /s /c \"start /b /wait \"parentconsole\" \"" + exe + "\" " + runs[k, 0] + "\"");
				psi.UseShellExecute = false;
				psi.WorkingDirectory = Path.GetDirectoryName(exe);
				psi.RedirectStandardOutput = true;
				psi.RedirectStandardError = true;
				StringBuilder outTxt = new StringBuilder();
				Stopwatch sw = Stopwatch.StartNew();
				Process p = Process.Start(psi);
				Process pq = p;
				Thread o1 = new Thread(delegate() { try { string l; while ((l = pq.StandardOutput.ReadLine()) != null) { lock (outTxt) { outTxt.AppendLine(l); } } } catch { } });
				Thread o2 = new Thread(delegate() { try { string l; while ((l = pq.StandardError.ReadLine()) != null) { lock (outTxt) { outTxt.AppendLine(l); } } } catch { } });
				o1.IsBackground = true; o2.IsBackground = true; o1.Start(); o2.Start();
				bool timedOut = !WaitProgress(p, k == 0 ? 600 : 900, log, "Cinebench");
				if (timedOut)
				{
					try { p.Kill(); } catch { }
					KillByName(exe);
					r.Error = (k == 0 ? "tobbszalu" : "egyszalu") + " futas: idotullepes (leallitottam)";
					log("  A Cinebench nem vegzett idoben - leallitottam.");
				}
				if (clkOk) { clk.EndPhase(); }
				double took = sw.Elapsed.TotalSeconds;
				Thread.Sleep(800);
				try { o1.Join(1500); o2.Join(500); } catch { }
				string piped;
				lock (outTxt) { piped = outTxt.ToString(); }
				string con = ConsoleText();
				rawAll.AppendLine("[" + runs[k, 0] + ", " + took.ToString("0", IC) + " mp]").AppendLine(Tail(piped, 1200));
				double v = -1;
				List<double> sp = CbScores(piped);
				if (sp.Count > 0) { v = sp[sp.Count - 1]; }
				else
				{
					List<double> after = CbScores(con);
					if (after.Count > before) { v = after[after.Count - 1]; }
				}
				if (v > 0)
				{
					if (k == 0) { r.Multi = v; } else { r.Single = v; }
					log("  -> " + (k == 0 ? "tobbszalu: " : "egyszalu: ") + v.ToString("0.0", IC) + " pont");
				}
				else
				{
					log("  (a pontszamot nem sikerult kiolvasni - a nyers kimenet bekerul az eredmenybe)");
					if (piped.Trim().Length == 0) { rawAll.AppendLine("[konzol vege]").AppendLine(Tail(con, 800)); }
					if (r.Error == null) { r.Error = took < 5 ? "a Cinebench azonnal kilepett (" + took.ToString("0.0", IC) + " mp) - nem indult el a meres" : "a pontszam nem olvashato ki a Cinebench kimenetebol"; }
				}
				if (timedOut) { break; }
			}
		}
		catch (Exception ex) { r.Error = ex.GetType().Name + ": " + ex.Message; }
		try { if (clkOk) { clk.Stop(); r.ClockJson = clk.Json(); } } catch (Exception ex2) { if (r.Error == null) { r.Error = "orajel-naplo: " + ex2.Message; } }
		r.Raw = Tail(rawAll.ToString(), 3000);
		return r;
	}

	// ---------------------------------------------------------------- FurMark
	public class FmResult
	{
		public string Path, Version, Error, Raw;
		public double Score = -1, Fps = -1;
		public string GpuLogJson;     // NVIDIA: [t, MHz, memMHz, °C, W, %]
		public string UtilJson;       // egyéb: kihasználtság % / mp
		public string CpuClockJson;   // futás alatti CPU-órajel
		public string GpuCsv;         // FurMark 2 saját GPU-naplója (--log-gpu-data), nyersen
	}

	private static string NvSmi()
	{
		string[] c = new string[] {
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
			Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? "", @"NVIDIA Corporation\NVSMI\nvidia-smi.exe") };
		foreach (string s in c) { if (File.Exists(s)) { return s; } }
		return null;
	}

	private static string GpuPrefKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

	public static FmResult RunFurMark(string exe, int seconds, Action<string> log)
	{
		FmResult r = new FmResult();
		r.Path = exe;
		string dir = Path.GetDirectoryName(exe);
		string tmp = Path.Combine(Path.GetTempPath(), "malak-furmark");
		try { if (Directory.Exists(tmp)) { Directory.Delete(tmp, true); } Directory.CreateDirectory(tmp); } catch { }
		string oldPref = null; bool prefSet = false;
		Process smi = null; List<string> smiLines = new List<string>();
		Clock clk = new Clock(); bool clkOk = false;
		try
		{
			// Optimus / hibrid grafika: a FurMark a nagy teljesitmenyu GPU-ra
			try
			{
				using (RegistryKey k = Registry.CurrentUser.CreateSubKey(GpuPrefKey))
				{
					object o = k.GetValue(exe);
					oldPref = o as string;
					k.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
					prefSet = true;
				}
			}
			catch { }

			// v2 (furmark.exe) vagy v1 (FurMark.exe /nogui)?
			bool v2 = File.Exists(Path.Combine(dir, "FurMark_GUI.exe")) || File.Exists(Path.Combine(dir, "furmark_gui.exe")) || File.Exists(Path.Combine(dir, "FurMark_GUI"));
			r.Version = v2 ? "2" : "1";
			string args = v2
				? "--demo furmark-gl --p1080 --benchmark --max-time " + seconds + " --no-score-box --log-gpu-data --log-gpu-data-filename \"" + Path.Combine(tmp, "gpu.csv") + "\""
				: "/width=1920 /height=1080 /max_time=" + (seconds * 1000) + " /nogui /nomenubar /noscore /run_mode=1 /log_score";

			string nv = NvSmi();
			if (nv != null)
			{
				try
				{
					ProcessStartInfo si = new ProcessStartInfo(nv, "--query-gpu=clocks.gr,clocks.mem,temperature.gpu,power.draw,utilization.gpu --format=csv,noheader,nounits -l 1");
					si.UseShellExecute = false; si.RedirectStandardOutput = true; si.CreateNoWindow = true;
					smi = Process.Start(si);
					Process sp = smi;
					Thread rd = new Thread(delegate()
					{
						try { string ln; while ((ln = sp.StandardOutput.ReadLine()) != null) { lock (smiLines) { smiLines.Add(ln); } } } catch { }
					});
					rd.IsBackground = true; rd.Start();
				}
				catch { smi = null; }
			}
			clkOk = clk.Start();
			if (clkOk) { clk.Phase("furmark"); }
			// GPU-kihasználtság (nem NVIDIA vagy kiegészítésül)
			List<float> util = new List<float>();
			bool utilRun = true;
			Thread ut = new Thread(delegate()
			{
				try
				{
					PerformanceCounterCategory cat = new PerformanceCounterCategory("GPU Engine");
					List<PerformanceCounter> pcs = new List<PerformanceCounter>();
					int tick = 0;
					while (utilRun)
					{
						if (tick % 5 == 0)
						{
							pcs.Clear();
							foreach (string n in cat.GetInstanceNames())
							{
								if (n.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									try { PerformanceCounter pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", n); pc.NextValue(); pcs.Add(pc); } catch { }
								}
							}
						}
						Thread.Sleep(1000);
						double sum = 0;
						foreach (PerformanceCounter pc in pcs) { try { sum += pc.NextValue(); } catch { } }
						lock (util) { util.Add((float)Math.Min(100.0, sum)); }
						tick++;
					}
				}
				catch { }
			});
			ut.IsBackground = true; ut.Start();

			log("  FurMark " + (v2 ? "2" : "1") + " fut (" + seconds + " mp)...");
			ProcessStartInfo fi = new ProcessStartInfo(exe, args);
			fi.UseShellExecute = false;
			fi.WorkingDirectory = dir;
			fi.RedirectStandardOutput = v2;
			fi.RedirectStandardError = v2;
			fi.CreateNoWindow = false;
			DateTime started = DateTime.Now;
			Process fp = Process.Start(fi);
			StringBuilder outTxt = new StringBuilder();
			if (v2)
			{
				Process fq = fp;
				Thread o1 = new Thread(delegate() { try { string l; while ((l = fq.StandardOutput.ReadLine()) != null) { lock (outTxt) { outTxt.AppendLine(l); } } } catch { } });
				Thread o2 = new Thread(delegate() { try { string l; while ((l = fq.StandardError.ReadLine()) != null) { lock (outTxt) { outTxt.AppendLine(l); } } } catch { } });
				o1.IsBackground = true; o2.IsBackground = true; o1.Start(); o2.Start();
			}
			if (!WaitProgress(fp, seconds + 120, log, "FurMark")) { try { fp.Kill(); } catch { } KillByName(exe); r.Error = "idotullepes (leallitottam)"; log("  A FurMark nem vegzett idoben - leallitottam."); }
			Thread.Sleep(500);
			utilRun = false;
			if (clkOk) { clk.EndPhase(); clk.Stop(); r.CpuClockJson = clk.Json(); }

			string raw;
			lock (outTxt) { raw = outTxt.ToString(); }
			// v1: FurMark-Score.txt; v2: konzolkimenet + a FurMark mappajaba irt naplo / pontszam fajlok (a futas ota modosultak)
			string gpuCsv = null;
			try
			{
				List<string> cand = new List<string>();
				foreach (string f in Directory.GetFiles(dir)) { cand.Add(f); }
				foreach (string sub in Directory.GetDirectories(dir)) { try { foreach (string f in Directory.GetFiles(sub)) { cand.Add(f); } } catch { } }
				foreach (string f in Directory.GetFiles(tmp, "*.*", SearchOption.AllDirectories)) { cand.Add(f); }
				foreach (string f in cand)
				{
					try
					{
						FileInfo fi2 = new FileInfo(f);
						if (fi2.LastWriteTime < started.AddSeconds(-2) || fi2.Length > 400000 || fi2.Length == 0) { continue; }
						if (!Regex.IsMatch(f, @"\.(txt|csv|json|log)$", RegexOptions.IgnoreCase)) { continue; }
						string body = File.ReadAllText(f);
						if (Regex.IsMatch(Path.GetFileName(f), @"gpu.*\.csv$", RegexOptions.IgnoreCase)) { gpuCsv = Tail(body, 8000); continue; }
						raw += "\n--- " + Path.GetFileName(f) + " ---\n" + Tail(body, 2500);
					}
					catch { }
				}
			}
			catch { }
			r.GpuCsv = gpuCsv;
			r.Raw = Tail(raw, 6000);
			Match m = null;
			foreach (string pat in new string[] { @"score\s*[:=]\s*(\d{2,7})", @"(\d{2,7})\s*points", @"points?\s*[:=]\s*(\d{2,7})", @"(?:score|points?)\D{0,20}(\d{2,7})" })
			{
				MatchCollection mc = Regex.Matches(raw, pat, RegexOptions.IgnoreCase);
				if (mc.Count > 0) { m = mc[mc.Count - 1]; break; }
			}
			double pv;
			if (m != null && double.TryParse(m.Groups[1].Value, NumberStyles.Float, IC, out pv)) { r.Score = pv; }
			Match mf = null;
			foreach (string pat in new string[] { @"(?:avg|average)[^\d\n]{0,20}(\d+(?:[.,]\d+)?)", @"(\d+(?:[.,]\d+)?)\s*fps" })
			{
				MatchCollection mc = Regex.Matches(raw, pat, RegexOptions.IgnoreCase);
				if (mc.Count > 0) { mf = mc[mc.Count - 1]; break; }
			}
			if (mf != null && double.TryParse(mf.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, IC, out pv)) { r.Fps = pv; }

			lock (util)
			{
				StringBuilder ub = new StringBuilder("[");
				for (int i = 0; i < util.Count; i++) { if (i > 0) { ub.Append(','); } ub.Append(((int)util[i]).ToString(IC)); }
				r.UtilJson = ub.Append(']').ToString();
			}
			if (smi != null)
			{
				try { smi.Kill(); } catch { }
				lock (smiLines)
				{
					StringBuilder gb = new StringBuilder("[");
					int n = 0;
					foreach (string ln in smiLines)
					{
						string[] p = ln.Split(',');
						if (p.Length < 5) { continue; }
						double[] v = new double[5];
						bool ok = true;
						for (int i = 0; i < 5; i++) { if (!double.TryParse(p[i].Trim(), NumberStyles.Float, IC, out v[i])) { ok = false; } }
						if (!ok) { continue; }
						if (n++ > 0) { gb.Append(','); }
						gb.Append('[').Append(n - 1).Append(',').Append(v[0].ToString("0", IC)).Append(',').Append(v[1].ToString("0", IC)).Append(',')
						  .Append(v[2].ToString("0", IC)).Append(',').Append(v[3].ToString("0.0", IC)).Append(',').Append(v[4].ToString("0", IC)).Append(']');
					}
					r.GpuLogJson = n > 0 ? gb.Append(']').ToString() : null;
				}
			}
			if (r.Score < 0) { log("  (a FurMark pontszamot nem sikerult biztosan kiolvasni, a nyers kimenetet elmentem)"); }
		}
		catch (Exception ex) { r.Error = ex.GetType().Name + ": " + ex.Message; }
		finally
		{
			try { if (smi != null && !smi.HasExited) { smi.Kill(); } } catch { }
			try
			{
				if (prefSet)
				{
					using (RegistryKey k = Registry.CurrentUser.CreateSubKey(GpuPrefKey))
					{
						if (oldPref == null) { k.DeleteValue(exe, false); } else { k.SetValue(exe, oldPref, RegistryValueKind.String); }
					}
				}
			}
			catch { }
		}
		return r;
	}

	// ---------------------------------------------------------------- JSON
	private static string J(string s)
	{
		if (s == null) { return "null"; }
		StringBuilder b = new StringBuilder("\"");
		foreach (char c in s)
		{
			if (c == '"' || c == '\\') { b.Append('\\').Append(c); }
			else if (c < ' ') { b.Append(' '); }
			else { b.Append(c); }
		}
		return b.Append('"').ToString();
	}
	private static string Nn(double d) { return (d < 0 || double.IsNaN(d) || double.IsInfinity(d)) ? "null" : d.ToString("0.##", IC); }

	public static string CbJson(CbResult r)
	{
		return ",\"cbMulti\":" + Nn(r.Multi) + ",\"cbSingle\":" + Nn(r.Single) + ",\"cbError\":" + J(r.Error)
			+ ",\"cbPath\":" + J(r.Path) + ",\"cbRaw\":" + J(r.Raw) + ",\"cbClk\":" + (r.ClockJson ?? "null");
	}

	public static string FmJson(FmResult r)
	{
		return ",\"fmScore\":" + Nn(r.Score) + ",\"fmFps\":" + Nn(r.Fps) + ",\"fmVer\":" + J(r.Version) + ",\"fmError\":" + J(r.Error)
			+ ",\"fmPath\":" + J(r.Path) + ",\"fmRaw\":" + J(r.Raw) + ",\"fmGpu\":" + (r.GpuLogJson ?? "null")
			+ ",\"fmGpuCsv\":" + J(r.GpuCsv) + ",\"fmUtil\":" + (r.UtilJson ?? "null") + ",\"fmCpuClk\":" + (r.CpuClockJson ?? "null");
	}
}
