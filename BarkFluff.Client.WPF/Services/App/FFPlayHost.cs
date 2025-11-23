using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Media3D;

namespace BarkFluff.Client.WPF.Services.App
{
    class FFPlayHost : HwndHost
    {
        private Process _ffplay;
        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            // Запуск ffplay
            Process ffplay = new Process();
            ffplay.StartInfo.FileName = "ffplay.exe";
            ffplay.StartInfo.Arguments = "-i C:\\Users\\daske\\Desktop\\test1\\original.mp4 -noborder -window_title FFPlayWindow";
            ffplay.StartInfo.CreateNoWindow = false;
            ffplay.Start();

            // Ждать дескриптор окна (polling, чтобы не блокировать надолго)
            int timeout = 5000; // 5 сек таймаут
            while (_ffplay.MainWindowHandle == IntPtr.Zero && timeout > 0)
            {
                Thread.Sleep(100);
                timeout -= 100;
                _ffplay.Refresh(); // Обновить свойства процесса
            }

            if (_ffplay.MainWindowHandle == IntPtr.Zero)
            {
                throw new Exception("Не удалось получить дескриптор окна ffplay.");
            }

            SetParent(_ffplay.MainWindowHandle, hwndParent.Handle);
            MoveWindow(_ffplay.MainWindowHandle, 0, 0, (int)ActualWidth, (int)ActualHeight, true);

            return new HandleRef(this, _ffplay.MainWindowHandle); // Возвращаем валидный дескриптор
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (_ffplay != null && !_ffplay.HasExited)
            {
                _ffplay.Kill();
                _ffplay.Dispose();
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (_ffplay != null && _ffplay.MainWindowHandle != IntPtr.Zero)
            {
                MoveWindow(_ffplay.MainWindowHandle, 0, 0, (int)sizeInfo.NewSize.Width, (int)sizeInfo.NewSize.Height, true);
            }
        }
    }
}
