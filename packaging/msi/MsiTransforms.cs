// Used by Build-Msi.ps1 (Add-Type): makes language transforms and embeds them in the base MSI.
using System;
using System.Runtime.InteropServices;

public static class MsiTransforms
{
    const uint NoMoreItems = 259;

    public static void Generate(string languageMsi, string baseMsi, string transform)
    {
        IntPtr lang = Open(languageMsi, 0), basis = Open(baseMsi, 0);
        try
        {
            Check(MsiDatabaseGenerateTransform(lang, basis, transform, 0, 0), "generate transform");
            Check(MsiCreateTransformSummaryInfo(lang, basis, transform, 0, 0), "transform summary");
        }
        finally { MsiCloseHandle(basis); MsiCloseHandle(lang); }
    }

    public static void Embed(string baseMsi, string name, string transform)
    {
        IntPtr db = Open(baseMsi, 1), view = IntPtr.Zero, record = IntPtr.Zero;
        try
        {
            Check(MsiDatabaseOpenView(db, "SELECT `Name`,`Data` FROM `_Storages`", out view), "open _Storages");
            Check(MsiViewExecute(view, IntPtr.Zero), "execute");
            record = MsiCreateRecord(2);
            Check(MsiRecordSetString(record, 1, name), "record name");
            Check(MsiRecordSetStream(record, 2, transform), "record stream");
            Check(MsiViewModify(view, 3 /* assign */, record), "assign storage");
            MsiViewClose(view);
            Check(MsiDatabaseCommit(db), "commit");
        }
        finally
        {
            if (record != IntPtr.Zero) MsiCloseHandle(record);
            if (view != IntPtr.Zero) MsiCloseHandle(view);
            MsiCloseHandle(db);
        }
    }

    public static void SetTemplate(string baseMsi, string template)
    {
        IntPtr db = Open(baseMsi, 1), summary;
        try
        {
            Check(MsiGetSummaryInformation(db, null, 1, out summary), "summary");
            try
            {
                Check(MsiSummaryInfoSetProperty(summary, 7, 30 /* VT_LPSTR */, 0, IntPtr.Zero, template), "template");
                Check(MsiSummaryInfoPersist(summary), "persist summary");
            }
            finally { MsiCloseHandle(summary); }
            Check(MsiDatabaseCommit(db), "commit");
        }
        finally { MsiCloseHandle(db); }
    }

    public static bool HasStorage(string msi, string name)
    {
        IntPtr db = Open(msi, 0), view = IntPtr.Zero, record = IntPtr.Zero;
        try
        {
            Check(MsiDatabaseOpenView(db, "SELECT `Name` FROM `_Storages` WHERE `Name`='" + name + "'", out view), "open view");
            Check(MsiViewExecute(view, IntPtr.Zero), "execute");
            uint fetched = MsiViewFetch(view, out record);
            if (fetched == NoMoreItems) return false;
            Check(fetched, "fetch");
            return true;
        }
        finally
        {
            if (record != IntPtr.Zero) MsiCloseHandle(record);
            if (view != IntPtr.Zero) MsiCloseHandle(view);
            MsiCloseHandle(db);
        }
    }

    /// <summary>
    /// Forces the database code page (importing _ForceCodepage). Used to make the English base
    /// neutral (0), so a language transform can set its own code page (Hebrew needs 1255).
    /// Safe only while the database holds plain ASCII text.
    /// </summary>
    public static void SetCodepage(string msi, string codepage, string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "_ForceCodepage.idt"), "\r\n\r\n" + codepage + "\t_ForceCodepage\r\n", System.Text.Encoding.ASCII);
        IntPtr db = Open(msi, 1);
        try
        {
            Check(MsiDatabaseImport(db, folder, "_ForceCodepage.idt"), "import codepage");
            Check(MsiDatabaseCommit(db), "commit");
        }
        finally { MsiCloseHandle(db); }
    }

    /// <summary>The database's ANSI code page (exported _ForceCodepage).</summary>
    public static string Codepage(string msi, string folder)
    {
        IntPtr db = Open(msi, 0);
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            Check(MsiDatabaseExport(db, "_ForceCodepage", folder, "codepage.idt"), "export codepage");
            var lines = System.IO.File.ReadAllLines(System.IO.Path.Combine(folder, "codepage.idt"));
            return lines.Length > 2 ? lines[2].Split('\t')[0] : "?";
        }
        finally { MsiCloseHandle(db); }
    }

    static IntPtr Open(string path, int mode)
    {
        IntPtr handle;
        Check(MsiOpenDatabase(path, new IntPtr(mode), out handle), "open " + path);
        return handle;
    }

    static void Check(uint code, string what)
    {
        if (code != 0) throw new InvalidOperationException("Windows Installer error " + code + " (" + what + ")");
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiOpenDatabase(string path, IntPtr persist, out IntPtr db);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiDatabaseGenerateTransform(IntPtr db, IntPtr reference, string transform, int reserved1, int reserved2);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiCreateTransformSummaryInfo(IntPtr db, IntPtr reference, string transform, int errorConditions, int validation);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiDatabaseOpenView(IntPtr db, string query, out IntPtr view);
    [DllImport("msi.dll")] static extern uint MsiViewExecute(IntPtr view, IntPtr record);
    [DllImport("msi.dll")] static extern uint MsiViewFetch(IntPtr view, out IntPtr record);
    [DllImport("msi.dll")] static extern uint MsiViewModify(IntPtr view, int mode, IntPtr record);
    [DllImport("msi.dll")] static extern uint MsiViewClose(IntPtr view);
    [DllImport("msi.dll")] static extern IntPtr MsiCreateRecord(uint fields);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiRecordSetString(IntPtr record, uint field, string value);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiRecordSetStream(IntPtr record, uint field, string path);
    [DllImport("msi.dll")] static extern uint MsiDatabaseCommit(IntPtr db);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiGetSummaryInformation(IntPtr db, string path, uint updateCount, out IntPtr summary);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiSummaryInfoSetProperty(IntPtr summary, uint property, uint type, int intValue, IntPtr fileTime, string value);
    [DllImport("msi.dll")] static extern uint MsiSummaryInfoPersist(IntPtr summary);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiDatabaseImport(IntPtr db, string folder, string file);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiDatabaseExport(IntPtr db, string table, string folder, string file);
    [DllImport("msi.dll")] static extern uint MsiCloseHandle(IntPtr handle);
}
