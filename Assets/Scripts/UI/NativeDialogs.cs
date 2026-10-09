using System;
using System.Runtime.InteropServices;

namespace FrcSim
{
    // Windows 原生「開啟檔案」視窗(comdlg32),不需要額外套件。
    public static class NativeDialogs
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        class OpenFileName
        {
            public int lStructSize = Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public string lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Auto)]
        static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct BrowseInfo
        {
            public IntPtr hwndOwner, pidlRoot;
            public IntPtr pszDisplayName;
            public string lpszTitle;
            public uint ulFlags;
            public IntPtr lpfn, lParam;
            public int iImage;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Auto)] static extern IntPtr SHBrowseForFolder(ref BrowseInfo bi);
        [DllImport("shell32.dll", CharSet = CharSet.Auto)] static extern bool SHGetPathFromIDList(IntPtr pidl, System.Text.StringBuilder path);

        // 選資料夾(機器人程式專案資料夾)
        public static string PickFolder(string title)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var bi = new BrowseInfo
                {
                    hwndOwner = GetActiveWindow(),
                    pszDisplayName = Marshal.AllocHGlobal(1024),
                    lpszTitle = title,
                    ulFlags = 0x0040 | 0x0010 | 0x0001   // NEWDIALOGSTYLE | EDITBOX | RETURNONLYFSDIRS
                };
                IntPtr pidl = SHBrowseForFolder(ref bi);
                Marshal.FreeHGlobal(bi.pszDisplayName);
                if (pidl == IntPtr.Zero) return null;
                var sb = new System.Text.StringBuilder(1024);
                return SHGetPathFromIDList(pidl, sb) ? sb.ToString() : null;
            }
            catch (Exception e) { UnityEngine.Debug.LogError("PickFolder: " + e.Message); }
#endif
            return null;
        }
        // filter 例: "GLB 模型 (*.glb)\0*.glb\0所有檔案 (*.*)\0*.*\0"(結尾要有兩個 \0)
        public static string OpenFile(string title, string filter)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var o = new OpenFileName
                {
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = filter + "\0",
                    nFilterIndex = 1,
                    lpstrFile = new string('\0', 1024),
                    nMaxFile = 1024,
                    lpstrFileTitle = new string('\0', 260),
                    nMaxFileTitle = 260,
                    lpstrTitle = title,
                    Flags = 0x00001000 | 0x00000800 | 0x00080000   // FILEMUSTEXIST | PATHMUSTEXIST | EXPLORER
                };
                if (GetOpenFileName(o))
                    return o.lpstrFile.Substring(0, o.lpstrFile.IndexOf('\0'));
            }
            catch (Exception e) { UnityEngine.Debug.LogError("OpenFile: " + e.Message); }
#endif
            return null;
        }
    }
}
