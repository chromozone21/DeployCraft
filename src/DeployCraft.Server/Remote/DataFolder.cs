using System.Security.AccessControl;
using System.Security.Principal;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Le dossier de données (base, mots de passe chiffrés) est réservé à Système et aux Administrateurs :
/// un simple utilisateur du serveur ne peut ni lire la base ni déchiffrer les identifiants.
/// </summary>
public static class DataFolder
{
    public static void Restrict(string path, ILogger log)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            dir.Create();
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            dir.SetAccessControl(security);

            // Les fichiers déjà présents héritaient des droits de ProgramData (Utilisateurs en lecture) : on les aligne.
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                var fs = file.GetAccessControl();
                fs.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                foreach (FileSystemAccessRule rule in fs.GetAccessRules(true, false, typeof(SecurityIdentifier))) fs.RemoveAccessRule(rule);
                file.SetAccessControl(fs);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            log.LogWarning(ex, "Droits du dossier de données non restreints ({Path})", path);
        }
    }
}
