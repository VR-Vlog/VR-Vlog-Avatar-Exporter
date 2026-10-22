# Transfer dependencies

Only the .NET Standard 2.0 core assembly below is redistributed. It uses
Editor-only plugin import settings and is referenced only by the transfer
Editor and test assemblies (`VRVlog.LanTransfer.Editor`). The package builder accepts this exact DLL path and verifies its
SHA-256 before producing a package; arbitrary DLL files remain excluded.

| Library | Fixed version | DLL SHA-256 |
| --- | --- | --- |
| ZXing.Net | 0.16.11 | `f3b823b6fd6492525a7547989056883def5d43be1e12c4f63fa54df73e3c5cfc` |

ZXing.Net is distributed under Apache-2.0: see `ZXing-LICENSE.txt`.
Copyright 2008 ZXing authors; Copyright 2012 Michael Jahn.
Official source: https://github.com/micjahn/ZXing.Net/tree/v0.16.11.0
Official package: https://www.nuget.org/packages/ZXing.Net/0.16.11
Original NuGet package SHA-256:
`7d39234d668e558b3d374d18116ed57021d7c0df482801f1e1db4f1db9314ec3`.

The redistributed DLL is unmodified. No online QR generation service,
NuGet-for-Unity runtime dependency, certificate store installation, or native
cryptographic binary is required.
