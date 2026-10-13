using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -accttest:登入/帳號邏輯自動檢查(Prefs.Isolated:只用記憶體,不寫登錄檔)。結果寫到 persistentDataPath\accttest.txt
    public static class AcctTest
    {
        public static void Run()
        {
            Prefs.Isolated = true;   // 全程只用記憶體,不碰登錄檔(使用者的模擬器開著也安全)
            var sb = new StringBuilder(); int fail = 0;
            void Chk(string n, bool ok) { sb.AppendLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
            try
            {
                bool n1, n2, n3, n4;
                Account.SignInGoogle("Tester A", "a@test.invalid", out n1); int slotA = Profile.Index; string uidA = Account.Uid;
                Chk("A first sign-in has UID (8 digits)", Friends.ValidUid(uidA));
                Chk("A first sign-in is new (tutorial)", n1);
                Profile.SetInt("tutSeen", 1); Account.SetSignature("hello sig"); Profile.SetInt("acctXp", 120);
                Account.SignInGoogle("Tester B", "b@test.invalid", out n2); int slotB = Profile.Index; string uidB = Account.Uid;
                Chk("B is new", n2); Chk("B has its own slot", slotB != slotA); Chk("B UID differs", uidB != uidA && Friends.ValidUid(uidB)); Chk("B signature empty", Account.Signature == ""); Chk("B xp not A's", Account.Xp != 120);
                Account.SignInGoogle("Tester A", "A@Test.Invalid", out n3);
                Chk("A returns: not new (no tutorial)", !n3); Chk("A same slot", Profile.Index == slotA); Chk("A same UID", Account.Uid == uidA); Chk("A signature kept", Account.Signature == "hello sig"); Chk("A xp kept", Account.Xp == 120);
                Chk("demo email is not creator", !Account.IsCreator);
                Account.SetGuest();
                Chk("guest has no UID", Account.Uid == "" && Account.UidText == "GUEST"); Chk("guest is not creator", !Account.IsCreator);
                Profile.SetInt("acctXp", 999); Account.SetSignature("guest sig"); Profile.SetInt("tutSeen", 1);
                Account.SignOut();
                Account.SignInGoogle("Tester A", "a@test.invalid", out n4);
                Chk("guest XP not saved", Account.Xp == 120); Chk("guest signature not saved", Account.Signature == "hello sig"); Chk("A still not new", !n4);
                // 舊版登入過、當時沒有 UID 的 Google 檔位:第一次讀就補發穩定的 8 碼
                Profile.AddNew(); Profile.ExitGuest(); Profile.SetInt("acctSet", 1); Profile.SetInt("acctGoogle", 1); Profile.SetString("acctEmail", "legacy@test.invalid");
                string lu = Account.Uid; Chk("legacy Google slot gets a UID on first read", Friends.ValidUid(lu)); Chk("legacy UID stable", Account.Uid == lu);
                Account.SignOut(); Chk("sign-out clears remembered account", Prefs.GetInt("lastGoogleSlot", 99) == -1 && !Account.RestoreLast());
                Account.SignInGoogle("Tester A", "a@test.invalid", out n4); Chk("sign-in remembers account", Prefs.GetInt("lastGoogleSlot", -1) == Profile.Index);
                Chk("long signature capped at 40", Account.Sanitize(new string('x', 99)).Length == 40); Chk("newlines stripped", Account.Sanitize("a\nb\r\nc") == "a b  c" || Account.Sanitize("a\nb\r\nc") == "a b c");
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); fail++; }
            sb.AppendLine("---\nfailures: " + fail);
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "accttest.txt"), sb.ToString());
            Application.Quit();
        }
    }
}
