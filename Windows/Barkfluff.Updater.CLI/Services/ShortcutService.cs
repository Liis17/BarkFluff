using Barkfluff.Updater.CLI.UI;

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Barkfluff.Updater.CLI.Services
{
    /// <summary>
    /// Сервис для создания ярлыков в меню "Пуск"
    /// </summary>
    public class ShortcutService
    {
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out WIN32_FIND_DATA pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PROPERTYKEY pkey);
            void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT : IDisposable
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public IntPtr p;
            public int p2;

            public static PROPVARIANT FromString(string val)
            {
                var prop = new PROPVARIANT();
                prop.vt = 31; // VT_LPWSTR
                prop.p = Marshal.StringToCoTaskMemUni(val);
                return prop;
            }

            public void Dispose()
            {
                PropVariantClear(ref this);
            }

            [DllImport("ole32.dll")]
            private static extern int PropVariantClear(ref PROPVARIANT pvar);
        }

        private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new PROPERTYKEY
        {
            fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            pid = 5
        };

        private const string AppUserModelId = "BarkFluff.Messenger";

        /// <summary>
        /// Создает ярлык в меню "Пуск"
        /// Требует прав администратора
        /// </summary>
        /// <param name="targetPath">Путь к исполняемому файлу BarkFluff</param>
        public void CreateStartMenuShortcut(string targetPath)
        {
            try
            {
                // Получаем путь к папке Programs в меню Пуск текущего пользователя
                var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                var programsPath = Path.Combine(startMenuPath, "Programs");
                var shortcutPath = Path.Combine(programsPath, "BarkFluff.lnk");

                // Проверяем, существует ли ярлык и совпадает ли путь
                if (File.Exists(shortcutPath))
                {
                    var existingPath = GetShortcutTargetPath(shortcutPath);
                    if (!string.IsNullOrEmpty(existingPath))
                    {
                        // Нормализуем пути для сравнения
                        var normalizedTargetPath = NormalizePath(targetPath);
                        var normalizedExistingPath = NormalizePath(existingPath);

                        if (string.Equals(normalizedTargetPath, normalizedExistingPath, StringComparison.OrdinalIgnoreCase))
                        {
                            ConsoleUI.PrintProgress("Start Menu shortcut is already up to date");
                            return;
                        }

                        ConsoleUI.PrintProgress("Shortcut path mismatch detected. Updating shortcut...");
                        ConsoleUI.PrintProgress($"  Old: {existingPath}");
                        ConsoleUI.PrintProgress($"  New: {targetPath}");
                    }
                }
                else
                {
                    ConsoleUI.PrintProgress("Creating Start Menu shortcut...");
                }

                CreateShortcut(shortcutPath, targetPath, AppUserModelId);

                ConsoleUI.PrintSuccess($"Shortcut created: {shortcutPath}");
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Failed to create shortcut: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Получает путь назначения из существующего ярлыка
        /// </summary>
        /// <param name="shortcutPath">Путь к файлу ярлыка</param>
        /// <returns>Путь назначения или null в случае ошибки</returns>
        private string GetShortcutTargetPath(string shortcutPath)
        {
            try
            {
                var link = (IShellLinkW)new ShellLink();
                var file = (IPersistFile)link;

                file.Load(shortcutPath, 0);

                var path = new StringBuilder(260);
                link.GetPath(path, path.Capacity, out _, 0);

                var result = path.ToString();
                return string.IsNullOrEmpty(result) ? null : result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Нормализует путь для сравнения (убирает лишние слэши, приводит к нижнему регистру)
        /// </summary>
        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path).ToLowerInvariant();
            }
            catch
            {
                return path.ToLowerInvariant();
            }
        }

        /// <summary>
        /// Создает ярлык с указанными параметрами
        /// </summary>
        private void CreateShortcut(string shortcutPath, string targetPath, string appUserModelId)
        {
            var link = (IShellLinkW)new ShellLink();

            link.SetPath(targetPath);
            link.SetDescription("BarkFluff Messenger");
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath));

            var propStore = (IPropertyStore)link;

            PROPVARIANT propVar = PROPVARIANT.FromString(appUserModelId);
            try
            {
                PROPERTYKEY pkeyCopy = PKEY_AppUserModel_ID;
                propStore.SetValue(ref pkeyCopy, ref propVar);
                propStore.Commit();
            }
            finally
            {
                propVar.Dispose();
            }

            var persistFile = (IPersistFile)link;
            persistFile.Save(shortcutPath, true);
        }

        /// <summary>
        /// Удаляет ярлык из меню "Пуск"
        /// </summary>
        public void RemoveStartMenuShortcut()
        {
            try
            {
                ConsoleUI.PrintProgress("Removing Start Menu shortcut...");

                var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                var programsPath = Path.Combine(startMenuPath, "Programs");
                var shortcutPath = Path.Combine(programsPath, "BarkFluff.lnk");

                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    ConsoleUI.PrintSuccess("Shortcut removed successfully");
                }
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Failed to remove shortcut: {ex.Message}");
                throw;
            }
        }
    }
}
