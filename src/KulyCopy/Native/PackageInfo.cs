namespace KulyCopy.Native;

/// <summary>Detecta se o processo roda dentro de um pacote MSIX (instalação pela Microsoft Store).</summary>
internal static class PackageInfo
{
    private static bool? _isPackaged;

    public static bool IsPackaged
    {
        get
        {
            if (_isPackaged is null)
            {
                try
                {
                    var length = 0;
                    var rc = NativeMethods.GetCurrentPackageFullName(ref length, null);
                    _isPackaged = rc != NativeMethods.APPMODEL_ERROR_NO_PACKAGE;
                }
                catch
                {
                    _isPackaged = false;
                }
            }
            return _isPackaged.Value;
        }
    }
}
