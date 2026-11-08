using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

public static class MB
{
	public static readonly string[] PatNames = new string[7] { "0x00", "0xFF", "0xAA", "0x55", "cim-fuggo", "veletlen", "vandorlo 1-es" };

	public static double Work(long n)
	{
		uint num = 2166136261u;
		double num2 = 1.0001;
		for (long num3 = 0L; num3 < n; num3++)
		{
			num ^= (uint)(int)num3;
			num *= 16777619;
			num2 = num2 * 1.0000001 + Math.Sqrt(num & 0x3FFu) * 1E-07;
			if (num2 > 1000000.0)
			{
				num2 = 1.0;
			}
		}
		return (double)num + num2;
	}

	public static double CpuOne(int ms)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		long num = 0L;
		double num2 = 0.0;
		while (stopwatch.ElapsedMilliseconds < ms)
		{
			num2 += Work(1000000L);
			num += 1000000;
		}
		return (double)num / stopwatch.Elapsed.TotalMilliseconds / 1000.0 + (double)((num2 > 1E+300) ? 1 : 0);
	}

	public static double CpuAll(int ms)
	{
		int processorCount = Environment.ProcessorCount;
		double[] r = new double[processorCount];
		Thread[] array = new Thread[processorCount];
		for (int j = 0; j < processorCount; j++)
		{
			int i = j;
			array[j] = new Thread((ThreadStart)delegate
			{
				r[i] = CpuOne(ms);
			});
			array[j].Start();
		}
		Thread[] array2 = array;
		foreach (Thread thread in array2)
		{
			thread.Join();
		}
		double num = 0.0;
		double[] array3 = r;
		foreach (double num2 in array3)
		{
			num += num2;
		}
		return num;
	}

	public static List<double> Stress(int sec, Action<int, double> progress)
	{
		int processorCount = Environment.ProcessorCount;
		long total = 0L;
		bool run = true;
		Thread[] array = new Thread[processorCount];
		for (int i = 0; i < processorCount; i++)
		{
			array[i] = new Thread((ThreadStart)delegate
			{
				double num5 = 0.0;
				while (Volatile.Read(ref run))
				{
					num5 += Work(250000L);
					Interlocked.Add(ref total, 250000L);
				}
				if (num5 > 1E+300)
				{
					Console.Write(string.Empty);
				}
			});
			array[i].Priority = ThreadPriority.BelowNormal;
			array[i].Start();
		}
		List<double> list = new List<double>();
		Stopwatch stopwatch = Stopwatch.StartNew();
		long num = 0L;
		double num2 = 0.0;
		for (int j = 5; j <= sec; j += 5)
		{
			while (stopwatch.Elapsed.TotalSeconds < (double)j)
			{
				Thread.Sleep(50);
			}
			long num3 = Interlocked.Read(ref total);
			double totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
			double num4 = (double)(num3 - num) / (totalMilliseconds - num2) / 1000.0;
			list.Add(num4);
			num = num3;
			num2 = totalMilliseconds;
			progress(j, num4);
		}
		Volatile.Write(ref run, value: false);
		Thread[] array2 = array;
		foreach (Thread thread in array2)
		{
			thread.Join();
		}
		return list;
	}

	public static double Sha(int ms)
	{
		byte[] array = new byte[16777216];
		new Random(3).NextBytes(array);
		using (SHA256 sHA = SHA256.Create())
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			long num = 0L;
			while (stopwatch.ElapsedMilliseconds < ms)
			{
				sHA.ComputeHash(array);
				num += array.Length;
			}
			return (double)num / 1048576.0 / stopwatch.Elapsed.TotalSeconds;
		}
	}

	public static double Gflops(int ms)
	{
		int processorCount = Environment.ProcessorCount;
		double[] r = new double[processorCount];
		Thread[] array = new Thread[processorCount];
		for (int j = 0; j < processorCount; j++)
		{
			int i = j;
			array[j] = new Thread((ThreadStart)delegate
			{
				double[] array4 = new double[65536];
				double[] array5 = new double[65536];
				double[] array6 = new double[65536];
				Random random = new Random(i);
				for (int m = 0; m < 65536; m++)
				{
					array4[m] = random.NextDouble();
					array5[m] = random.NextDouble();
				}
				Stopwatch stopwatch = Stopwatch.StartNew();
				long num3 = 0L;
				while (stopwatch.ElapsedMilliseconds < ms)
				{
					for (int n = 0; n < 256; n++)
					{
						int num4 = n * 256;
						for (int num5 = 0; num5 < 256; num5++)
						{
							array6[num4 + num5] = 0.0;
						}
						for (int num6 = 0; num6 < 256; num6++)
						{
							double num7 = array4[num4 + num6];
							int num8 = num6 * 256;
							for (int num9 = 0; num9 < 256; num9++)
							{
								array6[num4 + num9] += num7 * array5[num8 + num9];
							}
						}
					}
					num3 += 33554432;
				}
				r[i] = (double)num3 / stopwatch.Elapsed.TotalSeconds / 1000000000.0;
			});
			array[j].Start();
		}
		Thread[] array2 = array;
		foreach (Thread thread in array2)
		{
			thread.Join();
		}
		double num = 0.0;
		double[] array3 = r;
		foreach (double num2 in array3)
		{
			num += num2;
		}
		return num;
	}

	public static double MemBw(int mb)
	{
		int num = mb * 1024 * 1024 / 8;
		double[] array = new double[num];
		double[] array2 = new double[num];
		for (int i = 0; i < num; i += 512)
		{
			array[i] = i;
		}
		Array.Copy(array, array2, num);
		double num2 = 0.0;
		for (int j = 0; j < 5; j++)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			for (int k = 0; k < 4; k++)
			{
				Array.Copy(array, array2, num);
				Array.Copy(array2, array, num);
			}
			double num3 = 8.0 * (double)num * 16.0 / stopwatch.Elapsed.TotalSeconds / 1000000000.0;
			if (num3 > num2)
			{
				num2 = num3;
			}
		}
		return num2;
	}

	public static double MemBwMT(int mbPerThread)
	{
		int num = Math.Max(2, Environment.ProcessorCount);
		int len = mbPerThread * 1024 * 1024 / 8;
		double[][] srcs = new double[num][];
		double[][] dsts = new double[num][];
		for (int i = 0; i < num; i++)
		{
			srcs[i] = new double[len];
			dsts[i] = new double[len];
		}
		double num2 = 0.0;
		for (int j = 0; j < 4; j++)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			Parallel.For(0, num, delegate(int t)
			{
				for (int k = 0; k < 4; k++)
				{
					Array.Copy(srcs[t], dsts[t], len);
					Array.Copy(dsts[t], srcs[t], len);
				}
			});
			double num3 = 8.0 * (double)len * 16.0 * (double)num / stopwatch.Elapsed.TotalSeconds / 1000000000.0;
			if (num3 > num2)
			{
				num2 = num3;
			}
		}
		return num2;
	}

	public static double LatencyNs()
	{
		int num = 16777216;
		int[] array = new int[num];
		for (int i = 0; i < num; i++)
		{
			array[i] = i;
		}
		Random random = new Random(7);
		for (int num2 = num - 1; num2 > 0; num2--)
		{
			int num3 = random.Next(num2);
			int num4 = array[num2];
			array[num2] = array[num3];
			array[num3] = num4;
		}
		int num5 = 0;
		for (int j = 0; j < 1000000; j++)
		{
			num5 = array[num5];
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		for (int k = 0; k < 20000000; k++)
		{
			num5 = array[num5];
		}
		double num6 = stopwatch.Elapsed.TotalMilliseconds * 1000000.0 / 20000000.0;
		return (num5 >= 0) ? num6 : (-1.0);
	}

	private static ulong Pat(int k, int chunk, long i, ref ulong x)
	{
		switch (k)
		{
		case 0:
			return 0uL;
		case 1:
			return ulong.MaxValue;
		case 2:
			return 12297829382473034410uL;
		case 3:
			return 6148914691236517205uL;
		case 4:
			return (ulong)(((long)chunk << 40) ^ i) ^ 0x9E3779B97F4A7C15uL;
		case 5:
			x ^= x << 13;
			x ^= x >> 7;
			x ^= x << 17;
			return x;
		default:
			return (ulong)(1L << (int)((i + chunk) & 0x3F));
		}
	}

	public static long MemTest(List<ulong[]> chunks, Action<string> log)
	{
		long num = 0L;
		for (int i = 0; i < PatNames.Length; i++)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			Parallel.For(0, chunks.Count, delegate(int c)
			{
				ulong[] array2 = chunks[c];
				ulong x2 = 0x2545F4914F6CDD1DuL ^ (ulong)((c + 1) * -7046029254386353131L);
				for (long num4 = 0L; num4 < array2.Length; num4++)
				{
					array2[num4] = Pat(i, c, num4, ref x2);
				}
			});
			long e = 0L;
			Parallel.For(0, chunks.Count, delegate(int c)
			{
				ulong[] array = chunks[c];
				ulong x = 0x2545F4914F6CDD1DuL ^ (ulong)((c + 1) * -7046029254386353131L);
				long num2 = 0L;
				for (long num3 = 0L; num3 < array.Length; num3++)
				{
					if (array[num3] != Pat(i, c, num3, ref x))
					{
						num2++;
					}
				}
				if (num2 > 0)
				{
					Interlocked.Add(ref e, num2);
				}
			});
			num += e;
			log("  minta " + (i + 1) + "/" + PatNames.Length + " (" + PatNames[i] + "): " + ((e != 0) ? ("HIBA: " + e) : "OK") + "  " + stopwatch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " mp");
		}
		return num;
	}

	public static double[] Disk(string path, int mb)
	{
		byte[] array = new byte[8388608];
		new Random(1).NextBytes(array);
		Stopwatch stopwatch = Stopwatch.StartNew();
		using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, FileOptions.WriteThrough))
		{
			for (int i = 0; i < mb / 8; i++)
			{
				fileStream.Write(array, 0, array.Length);
			}
			fileStream.Flush(flushToDisk: true);
		}
		double num = (double)mb / stopwatch.Elapsed.TotalSeconds;
		stopwatch.Restart();
		long num2 = 0L;
		try
		{
			using (FileStream fileStream2 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1048576, (FileOptions)536870912))
			{
				int num3;
				while ((num3 = fileStream2.Read(array, 0, array.Length)) > 0)
				{
					num2 += (int)array[0];
				}
			}
		}
		catch
		{
			stopwatch.Restart();
			using (FileStream fileStream3 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1048576, FileOptions.SequentialScan))
			{
				int num4;
				while ((num4 = fileStream3.Read(array, 0, array.Length)) > 0)
				{
					num2 += (int)array[0];
				}
			}
		}
		double num5 = (double)mb / stopwatch.Elapsed.TotalSeconds;
		double num6 = 0.0;
		try
		{
			byte[] buffer = new byte[4096];
			Random random = new Random(5);
			long num7 = (long)mb * 256L;
			using (FileStream fileStream4 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 4096, (FileOptions)536870912))
			{
				stopwatch.Restart();
				int num8 = 0;
				while (stopwatch.ElapsedMilliseconds < 3000)
				{
					fileStream4.Position = (long)(random.NextDouble() * (double)num7) * 4096;
					fileStream4.Read(buffer, 0, 4096);
					num8++;
				}
				num6 = (double)num8 / stopwatch.Elapsed.TotalSeconds;
			}
		}
		catch
		{
		}
		File.Delete(path);
		return new double[4] { num, num5, num6, num2 };
	}
}
