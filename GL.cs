using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

public static class GL
{
	private struct PFD
	{
		public ushort nSize;

		public ushort nVersion;

		public uint dwFlags;

		public byte iPixelType;

		public byte cColorBits;

		public byte cRedBits;

		public byte cRedShift;

		public byte cGreenBits;

		public byte cGreenShift;

		public byte cBlueBits;

		public byte cBlueShift;

		public byte cAlphaBits;

		public byte cAlphaShift;

		public byte cAccumBits;

		public byte cAccumRedBits;

		public byte cAccumGreenBits;

		public byte cAccumBlueBits;

		public byte cAccumAlphaBits;

		public byte cDepthBits;

		public byte cStencilBits;

		public byte cAuxBuffers;

		public byte iLayerType;

		public byte bReserved;

		public uint dwLayerMask;

		public uint dwVisibleMask;

		public uint dwDamageMask;
	}

	private struct MSG
	{
		public IntPtr hwnd;

		public uint message;

		public IntPtr wParam;

		public IntPtr lParam;

		public uint time;

		public int x;

		public int y;
	}

	private struct RECT
	{
		public int l;

		public int t;

		public int r;

		public int b;
	}

	private delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WNDCLASSEX
	{
		public uint cbSize;

		public uint style;

		public WndProc lpfnWndProc;

		public int cbClsExtra;

		public int cbWndExtra;

		public IntPtr hInstance;

		public IntPtr hIcon;

		public IntPtr hCursor;

		public IntPtr hbrBackground;

		public string lpszMenuName;

		public string lpszClassName;

		public IntPtr hIconSm;
	}

	public class Result
	{
		public string vendor;

		public string renderer;

		public string version;

		public string error;

		public double mpix;

		public double fps;

		public int layers;

		public int w;

		public int h;

		public double sec;
	}

	private static WndProc keep;

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr p, IntPtr m, IntPtr i, IntPtr lp);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);

	[DllImport("user32.dll")]
	private static extern IntPtr GetDC(IntPtr h);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(IntPtr h, IntPtr dc);

	[DllImport("user32.dll")]
	private static extern bool DestroyWindow(IntPtr h);

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(IntPtr h, out RECT r);

	[DllImport("user32.dll")]
	private static extern bool PeekMessage(out MSG m, IntPtr h, uint a, uint b, uint rem);

	[DllImport("user32.dll")]
	private static extern bool TranslateMessage(ref MSG m);

	[DllImport("user32.dll")]
	private static extern IntPtr DispatchMessage(ref MSG m);

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetModuleHandle(IntPtr n);

	[DllImport("gdi32.dll")]
	private static extern int ChoosePixelFormat(IntPtr dc, ref PFD p);

	[DllImport("gdi32.dll")]
	private static extern bool SetPixelFormat(IntPtr dc, int f, ref PFD p);

	[DllImport("gdi32.dll")]
	private static extern bool SwapBuffers(IntPtr dc);

	[DllImport("opengl32.dll")]
	private static extern IntPtr wglCreateContext(IntPtr dc);

	[DllImport("opengl32.dll")]
	private static extern bool wglMakeCurrent(IntPtr dc, IntPtr rc);

	[DllImport("opengl32.dll")]
	private static extern bool wglDeleteContext(IntPtr rc);

	[DllImport("opengl32.dll")]
	private static extern IntPtr glGetString(uint n);

	[DllImport("opengl32.dll")]
	private static extern void glViewport(int x, int y, int w, int h);

	[DllImport("opengl32.dll")]
	private static extern void glClearColor(float r, float g, float b, float a);

	[DllImport("opengl32.dll")]
	private static extern void glClear(uint m);

	[DllImport("opengl32.dll")]
	private static extern void glEnable(uint c);

	[DllImport("opengl32.dll")]
	private static extern void glBlendFunc(uint s, uint d);

	[DllImport("opengl32.dll")]
	private static extern void glColor4f(float r, float g, float b, float a);

	[DllImport("opengl32.dll")]
	private static extern void glBegin(uint m);

	[DllImport("opengl32.dll")]
	private static extern void glEnd();

	[DllImport("opengl32.dll")]
	private static extern void glVertex2f(float x, float y);

	[DllImport("opengl32.dll")]
	private static extern void glFinish();

	private static string Str(uint n)
	{
		IntPtr intPtr = glGetString(n);
		return (!(intPtr == IntPtr.Zero)) ? Marshal.PtrToStringAnsi(intPtr) : null;
	}

	public static Result Run(int seconds, Action<string> log)
	{
		Result result = new Result();
		IntPtr intPtr = IntPtr.Zero;
		IntPtr intPtr2 = IntPtr.Zero;
		IntPtr intPtr3 = IntPtr.Zero;
		try
		{
			keep = DefWindowProc;
			WNDCLASSEX wNDCLASSEX = default(WNDCLASSEX);
			wNDCLASSEX.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
			wNDCLASSEX.style = 35u;
			wNDCLASSEX.lpfnWndProc = keep;
			wNDCLASSEX.hInstance = GetModuleHandle(IntPtr.Zero);
			wNDCLASSEX.lpszClassName = "MalakGlBench";
			WNDCLASSEX wc = wNDCLASSEX;
			RegisterClassEx(ref wc);
			intPtr = CreateWindowEx(0u, "MalakGlBench", "Malak Tech - GPU teszt (OpenGL) - ne zard be, magatol bezarul", 282001408u, 60, 60, 1296, 759, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
			if (intPtr == IntPtr.Zero)
			{
				result.error = "nem nyilt ablak";
				return result;
			}
			intPtr2 = GetDC(intPtr);
			PFD pFD = default(PFD);
			pFD.nSize = (ushort)Marshal.SizeOf(typeof(PFD));
			pFD.nVersion = 1;
			pFD.dwFlags = 37u;
			pFD.iPixelType = 0;
			pFD.cColorBits = 32;
			pFD.cDepthBits = 24;
			PFD p = pFD;
			int num = ChoosePixelFormat(intPtr2, ref p);
			if (num == 0 || !SetPixelFormat(intPtr2, num, ref p))
			{
				result.error = "pixel format hiba";
				return result;
			}
			intPtr3 = wglCreateContext(intPtr2);
			if (intPtr3 == IntPtr.Zero || !wglMakeCurrent(intPtr2, intPtr3))
			{
				result.error = "OpenGL context hiba";
				return result;
			}
			result.vendor = Str(7936u);
			result.renderer = Str(7937u);
			result.version = Str(7938u);
			log("  GPU: " + result.renderer + " (" + result.vendor + "), OpenGL " + result.version);
			GetClientRect(intPtr, out var r);
			result.w = r.r - r.l;
			result.h = r.b - r.t;
			glViewport(0, 0, result.w, result.h);
			glEnable(3042u);
			glBlendFunc(770u, 771u);
			int num2 = 16;
			long num3 = 0L;
			long num4 = 0L;
			Stopwatch stopwatch = Stopwatch.StartNew();
			Stopwatch stopwatch2 = Stopwatch.StartNew();
			long num5 = 0L;
			while (stopwatch.Elapsed.TotalSeconds < (double)seconds)
			{
				MSG m;
				while (PeekMessage(out m, IntPtr.Zero, 0u, 0u, 1u))
				{
					TranslateMessage(ref m);
					DispatchMessage(ref m);
				}
				glClearColor(0.05f, 0.05f, 0.08f, 1f);
				glClear(16640u);
				float num6 = (float)stopwatch.Elapsed.TotalSeconds;
				glBegin(7u);
				for (int i = 0; i < num2; i++)
				{
					float num7 = (float)i / (float)num2;
					glColor4f(0.5f + 0.5f * (float)Math.Sin(num6 + num7 * 6f), 0.5f + 0.5f * (float)Math.Sin((double)num6 * 1.3 + (double)(num7 * 4f)), 0.5f + 0.5f * (float)Math.Sin((double)num6 * 0.7 + (double)(num7 * 9f)), 0.06f);
					glVertex2f(-1f, -1f);
					glVertex2f(1f, -1f);
					glVertex2f(1f, 1f);
					glVertex2f(-1f, 1f);
				}
				glEnd();
				glFinish();
				num3++;
				num5++;
				num4 += (long)num2 * (long)result.w * result.h;
				if (num3 % 8 == 0)
				{
					SwapBuffers(intPtr2);
				}
				if (stopwatch2.ElapsedMilliseconds > 500)
				{
					double num8 = (double)num5 / stopwatch2.Elapsed.TotalSeconds;
					if (num8 > 60.0 && num2 < 4096)
					{
						num2 *= 2;
					}
					else if (num8 < 15.0 && num2 > 4)
					{
						num2 /= 2;
					}
					stopwatch2.Restart();
					num5 = 0L;
				}
			}
			result.sec = stopwatch.Elapsed.TotalSeconds;
			result.mpix = (double)num4 / result.sec / 1000000.0;
			result.fps = (double)num3 / result.sec;
			result.layers = num2;
		}
		catch (Exception ex)
		{
			result.error = ex.Message;
		}
		finally
		{
			try
			{
				if (intPtr3 != IntPtr.Zero)
				{
					wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
					wglDeleteContext(intPtr3);
				}
				if (intPtr2 != IntPtr.Zero)
				{
					ReleaseDC(intPtr, intPtr2);
				}
				if (intPtr != IntPtr.Zero)
				{
					DestroyWindow(intPtr);
				}
			}
			catch
			{
			}
		}
		return result;
	}
}
