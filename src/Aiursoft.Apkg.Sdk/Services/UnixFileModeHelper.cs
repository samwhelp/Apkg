namespace Aiursoft.Apkg.Sdk.Services;

internal static class UnixFileModeHelper
{
    /// <summary>
    /// Parses a 3-digit octal string like "755" into UnixFileMode flags.
    /// Throws FormatException on invalid input.
    /// </summary>
    public static UnixFileMode ParseOctal(string octal)
    {
        if (string.IsNullOrWhiteSpace(octal))
            throw new FormatException("Mode string cannot be null or empty.");

        if (octal.Length != 3 ||
            octal[0] < '0' || octal[0] > '7' ||
            octal[1] < '0' || octal[1] > '7' ||
            octal[2] < '0' || octal[2] > '7')
            throw new FormatException($"Invalid mode string '{octal}'. Expected 3 octal digits (e.g. '755').");

        int owner = octal[0] - '0';
        int group = octal[1] - '0';
        int other = octal[2] - '0';

        var mode = (UnixFileMode)0;

        if ((owner & 4) != 0) mode |= UnixFileMode.UserRead;
        if ((owner & 2) != 0) mode |= UnixFileMode.UserWrite;
        if ((owner & 1) != 0) mode |= UnixFileMode.UserExecute;

        if ((group & 4) != 0) mode |= UnixFileMode.GroupRead;
        if ((group & 2) != 0) mode |= UnixFileMode.GroupWrite;
        if ((group & 1) != 0) mode |= UnixFileMode.GroupExecute;

        if ((other & 4) != 0) mode |= UnixFileMode.OtherRead;
        if ((other & 2) != 0) mode |= UnixFileMode.OtherWrite;
        if ((other & 1) != 0) mode |= UnixFileMode.OtherExecute;

        return mode;
    }

    /// <summary>
    /// Formats UnixFileMode flags back to a 3-digit octal string like "755".
    /// </summary>
    public static string ToOctalString(UnixFileMode mode)
    {
        int owner = ((mode & UnixFileMode.UserRead)    != 0 ? 4 : 0) |
                    ((mode & UnixFileMode.UserWrite)   != 0 ? 2 : 0) |
                    ((mode & UnixFileMode.UserExecute) != 0 ? 1 : 0);

        int group = ((mode & UnixFileMode.GroupRead)    != 0 ? 4 : 0) |
                    ((mode & UnixFileMode.GroupWrite)   != 0 ? 2 : 0) |
                    ((mode & UnixFileMode.GroupExecute) != 0 ? 1 : 0);

        int other = ((mode & UnixFileMode.OtherRead)    != 0 ? 4 : 0) |
                    ((mode & UnixFileMode.OtherWrite)   != 0 ? 2 : 0) |
                    ((mode & UnixFileMode.OtherExecute) != 0 ? 1 : 0);

        return $"{owner}{group}{other}";
    }
}
