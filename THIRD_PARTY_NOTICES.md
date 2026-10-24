# Third-party components

- **Tesseract 5**: Apache-2.0. Windows native binaries are distributed by NuGet package
  `Tesseract` 5.2.0 (Charles Weld / tesseract wrapper project). This application binds the native C API;
  the managed wrapper assembly is excluded. Native Windows runtime dependencies include Microsoft VC++ x64.
- **Leptonica**: BSD 2-clause; distributed alongside the Windows Tesseract binaries.
- **StbImageSharp 2.30.16 / StbImageWriteSharp 1.16.7**: MIT / public-domain STB ports.
  See their NuGet packages and https://github.com/StbSharp.
- **eng.traineddata**: https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0,
  Apache-2.0, full license stored in `assets/tessdata/LICENSE`.
  Source: https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/eng.traineddata
  SHA-256: `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`.
- Synthetic test images use DejaVu Sans, rendered with Pillow by the fixture generator.
  They are test data, not Dota 2 samples. The two full screenshots are user-provided fixtures;
  Dota 2 artwork belongs to its respective owners.
- Test-only dependencies: xUnit, Microsoft.NET.Test.Sdk, Visual Studio test adapter.

NuGet lockfiles record resolved package versions and integrity hashes. Retain upstream license
notices when redistributing native libraries and the language model.
