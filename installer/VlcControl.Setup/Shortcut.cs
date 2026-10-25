using System;
using System.IO;

namespace VlcControl.Setup
{
    internal static class Shortcut
    {
        public static void Create(string linkPath, string targetPath, string workingDirectory = null, string description = null)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
            {
                throw new InvalidOperationException("Не удалось создать ярлык: нет WScript.Shell.");
            }

            object shell = Activator.CreateInstance(type);
            try
            {
                dynamic instance = shell;
                dynamic shortcut = instance.CreateShortcut(linkPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(targetPath);
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Description = description ?? "VLC Control";
                shortcut.Save();
            }
            finally
            {
                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
                }
            }
        }
    }
}
