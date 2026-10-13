using System;
using System.Runtime.InteropServices;

namespace FrcSim
{
    // Windows 原生「開啟檔案」視窗(comdlg32),不需要額外套件。
    public static class NativeDialogs
    {
        // 全部用 IntPtr(blittable),字串自己配置非託管記憶體:
        // 舊版用 string 欄位 + [In,Out] 回傳,Mono 會在第一個 '\0' 把回傳字串截斷,後面 IndexOf('\0') = -1 → Substring 丟例外 → 一律回傳 null(選了檔案也沒反應)。
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileNameW
        {
            public int lStructSize;
            public IntPtr hwndOwner, hInstance, lpstrFilter, lpstrCustomFilter;
            public int nMaxCustFilter, nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public IntPtr lpstrFileTitle;
            public int nMaxFileTitle;
            public IntPtr lpstrInitialDir, lpstrTitle;
            public int Flags;
            public short nFileOffset, nFileExtension;
            public IntPtr lpstrDefExt, lCustData, lpfnHook, lpTemplateName, pvReserved;
            public int dwReserved, FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW")]
        static extern bool GetOpenFileNameW(ref OpenFileNameW ofn);
        [DllImport("comdlg32.dll")]
        static extern int CommDlgExtendedError();

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

        // 最近一次 OpenFile 的結果說明(給日誌/提示用):"ok" / "cancelled" / "error: ..."
        public static string LastResult = "";

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

        // filter 例: "GLB 模型 (*.glb)\0*.glb\0所有檔案 (*.*)\0*.*\0"(同一項裡多個副檔名用 ';' 分隔;結尾的雙 \0 這裡會自動補)
        public static string OpenFile(string title, string filter)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            IntPtr pFilter = IntPtr.Zero, pFile = IntPtr.Zero, pTitle = IntPtr.Zero;
            try
            {
                const int MaxFile = 4096;
                string f = filter.EndsWith("\0") ? filter + "\0" : filter + "\0\0";
                pFilter = Marshal.AllocHGlobal((f.Length + 1) * 2);
                for (int i = 0; i < f.Length; i++) Marshal.WriteInt16(pFilter, i * 2, (short)f[i]);   // 逐字寫入,保留中間的 '\0'
                Marshal.WriteInt16(pFilter, f.Length * 2, 0);
                pFile = Marshal.AllocHGlobal(MaxFile * 2);
                for (int i = 0; i < MaxFile; i++) Marshal.WriteInt16(pFile, i * 2, 0);
                pTitle = Marshal.StringToHGlobalUni(title ?? "");
                var o = new OpenFileNameW
                {
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = pFilter, nFilterIndex = 1,
                    lpstrFile = pFile, nMaxFile = MaxFile,
                    lpstrTitle = pTitle,
                    Flags = 0x00001000 | 0x00000800 | 0x00080000 | 0x00000008   // FILEMUSTEXIST | PATHMUSTEXIST | EXPLORER | NOCHANGEDIR
                };
                o.lStructSize = Marshal.SizeOf(typeof(OpenFileNameW));
                if (GetOpenFileNameW(ref o))
                {
                    string path = Marshal.PtrToStringUni(pFile);
                    LastResult = string.IsNullOrEmpty(path) ? "error: dialog returned an empty path" : "ok";
                    UnityEngine.Debug.Log("[OpenFile] picked: " + path);
                    return string.IsNullOrEmpty(path) ? null : path;
                }
                int err = CommDlgExtendedError();
                LastResult = err == 0 ? "cancelled" : "error: CommDlgExtendedError=0x" + err.ToString("X");
                UnityEngine.Debug.Log("[OpenFile] " + LastResult);
            }
            catch (Exception e) { LastResult = "error: " + e.Message; UnityEngine.Debug.LogError("OpenFile: " + e); }
            finally { if (pFilter != IntPtr.Zero) Marshal.FreeHGlobal(pFilter); if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile); if (pTitle != IntPtr.Zero) Marshal.FreeHGlobal(pTitle); }
#endif
            return null;
        }
    }
}
