using System;
using System.IO;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.UpdateSystem;

internal static partial class Program
{
    private static void UpdateShutdown()
    {
        var skin = Field<Skin>(editor, "_lastSkin");
        Check(skin != null && !skin.IsReadOnly, "Update shutdown fixture has an editable skin");
        skin.Dirty = true;
        Check(!editor.CloseForUpdate(() => DialogResult.Cancel), "Canceling save-before-update keeps the editor open");
        Check(!editor.IsDisposed && skin.Dirty && UpdateInstaller.PendingUpdate == null,
            "Canceled update preserves pending edits and does not schedule an installer");

        // A subsequent closing handler can still veto shutdown, even after the user chooses Save.
        FormClosingEventHandler veto = (_, args) => args.Cancel = true;
        editor.FormClosing += veto;
        try
        {
            Check(!editor.CloseForUpdate(() => DialogResult.Yes), "A canceled FormClosing prevents update restart");
            Check(!editor.IsDisposed && !skin.Dirty && UpdateInstaller.PendingUpdate == null,
                "Save-before-update writes edited skins without scheduling an installer after canceled shutdown");
            string previous = GlobalSettings.SkippedUpdateVersion;
            GlobalSettings.SkippedUpdateVersion = "2.0.1.0";
            GlobalSettings.Save();
            Check(File.ReadAllText(Path.Combine(scratch, "settings.ini")).Contains("SkippedUpdateVersion=2.0.1.0"),
                "Skipped update is persisted beside the isolated editor");
            GlobalSettings.SkippedUpdateVersion = previous;
        }
        finally { editor.FormClosing -= veto; }
    }
}
