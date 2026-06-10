# GPG verification fails with ASCII-armored keyrings

## Symptom

CI build of `firefox-anduinos` fails. `firmware-sof-anduinos` succeeds
(no keyring → `allowInsecure=true` → skips verification).

```
gpgv: invalid packet (ctb=2d)
gpgv: Can't check signature: No public key
```

## Root cause

`gpgv --keyring` requires **binary** OpenPGP keyring format (RFC 4880 §4).
Mozilla distributes their key as **ASCII-armored** format (RFC 4880 §6.2).
These are the same data in two different encodings — `gpgv` does not
auto-detect.

Binary format opens with packet type 0xC6 (public key).
ASCII-armored format opens with `-----BEGIN PGP PUBLIC KEY BLOCK-----`.
`gpgv` 2.x only accepts binary.

`firmware-sof-anduinos` works because `UpstreamSignedBy` is unset
→ `allowInsecure=true` → verification is skipped entirely.

## File examples

ASCII (`.asc`) — `firefox-anduinos/assets/mozilla-keyring.asc`:

```
-----BEGIN PGP PUBLIC KEY BLOCK-----

xsBNBGCRt7MBCADkYJHHQQoL6tKrW/LbmfR9ljz7ib2aWno4JO3VKQvLwjyUMPpq
/SXXMOnx8jXwgWizpPxQYDRJ0SQXS9ULJ1hXRL/OgMnZAYvYDeV2jBnKsAIEdiG/
...
-----END PGP PUBLIC KEY BLOCK-----
```

Binary (`.gpg`) — after `gpg --dearmor`:

```
00000000: c6c0 4d04 6091 b7b3 0108 00e4 6091 c741  ..M.`.......`..A
00000010: 0a0b ead2 ab5b f2db 99f4 7d96 3cfb 89bd  .....[....}.<...
00000020: 9a5a 7a38 24ed d529 0bcb c23c 9430 fa6a  .Zz8$..)...<.0.j
```

`gpgv` only accepts the second format.

## Reproduction

```bash
# FAILS — ASCII keyring
gpgv --keyring firefox-anduinos/assets/mozilla-keyring.asc /tmp/inrelease
# → gpgv: invalid packet (ctb=2d)
# → gpgv: Can't check signature: No public key

# WORKS — binary keyring
gpg --dearmor < firefox-anduinos/assets/mozilla-keyring.asc > /tmp/mozilla-keyring.gpg
gpgv --keyring /tmp/mozilla-keyring.gpg /tmp/inrelease
# → gpgv: Good signature from ...
```

## Fix

`src/Aiursoft.AptClient/AptGpgVerifier.cs` → `VerifyFileAsync()`

Before passing `keyringPath` to `gpgv`, detect ASCII format and convert.
Replace:

```csharp
var startInfo = new ProcessStartInfo
{
    FileName = "gpgv",
    Arguments = $"--status-fd 1 --keyring \"{keyringPath}\" \"{signedFilePath}\"",
    ...
};
```

With:

```csharp
string actualKeyring = keyringPath;
string firstLine = File.ReadLines(keyringPath).FirstOrDefault() ?? "";
if (firstLine.StartsWith("-----BEGIN PGP"))
{
    var tmp = Path.GetTempFileName();
    var psi = new ProcessStartInfo
    {
        FileName = "gpg",
        Arguments = $"--dearmor --output \"{tmp}\" \"{keyringPath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var p = Process.Start(psi)!;
    await p.WaitForExitAsync();
    if (p.ExitCode != 0)
        throw new InvalidOperationException($"Failed to dearmor: {keyringPath}");
    actualKeyring = tmp;
}

var startInfo = new ProcessStartInfo
{
    FileName = "gpgv",
    Arguments = $"--status-fd 1 --keyring \"{actualKeyring}\" \"{signedFilePath}\"",
    ...
};
```

## Acceptance criteria

- [ ] `firefox-anduinos` builds with `apkg publish` against live Mozilla APT repo
- [ ] Existing `.gpg` (binary) keyring packages still work unchanged
- [ ] `gpgv --keyring` never receives an ASCII-armored file

## Test requirements

- [ ] `.asc` input → auto-converts to binary → gpgv succeeds → `GOODSIG`
- [ ] `.gpg` input → passed through as-is → gpgv succeeds
- [ ] Missing keyring → original error behavior preserved
